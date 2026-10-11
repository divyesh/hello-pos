using System.Security.Claims;
using System.Text.Encodings.Web;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy.Samples;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HelloPos.Api.Tests.Tenancy;

/// <summary>
/// A migrated test database with the sample tables, and the API hosted against it as the app login.
/// Requests authenticate through a test-only scheme: header <see cref="TestAuthHandler.Header"/>
/// becomes an authenticated identity with a <c>business_id</c> claim, so the production
/// <see cref="ClaimsCurrentBusiness"/> is what decides the current business (spec FR-006, FR-007).
/// </summary>
public sealed class TenancyApp : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> factory;

    private TenancyApp(DatabaseLogins logins, int maxPoolSize)
    {
        Logins = logins;
        AppConnectionString = new NpgsqlConnectionStringBuilder(logins.App) { MaxPoolSize = maxPoolSize }.ConnectionString;
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:HelloPos", AppConnectionString);
            builder.ConfigureTestServices(services =>
            {
                services.AddDbContext<SampleDbContext>(o => o.UseHelloPosDatabase(AppConnectionString));
                services.AddSingleton<IEndpointMapper, SampleEndpoints>();
                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            });
        });
    }

    public DatabaseLogins Logins { get; }

    public string AppConnectionString { get; }

    public static async Task<TenancyApp> CreateAsync(PostgresFixture postgres, int maxPoolSize = 20)
    {
        var logins = await postgres.CreateEmptyDatabaseAsync();
        await new Migrator(Microsoft.Extensions.Logging.Abstractions.NullLogger<Migrator>.Instance)
            .RunAsync(logins.Settings, CancellationToken.None);
        await SampleSchema.CreateAsync(logins.Migrator);
        return new TenancyApp(logins, maxPoolSize);
    }

    /// <summary>An HTTP client acting as <paramref name="businessId"/>, or anonymous when null.</summary>
    public HttpClient Client(Guid? businessId = null)
    {
        var client = factory.CreateClient();
        if (businessId is { } id)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.Header, id.ToString());
        }

        return client;
    }

    /// <summary>A context acting as <paramref name="businessId"/> through the app login.</summary>
    public SampleDbContext Db(Guid? businessId) => SampleDbContext.Create(AppConnectionString, businessId);

    /// <summary>A context as the migrator (table owner: RLS does not apply) for test assertions only.</summary>
    public SampleDbContext OwnerDb() => SampleDbContext.Create(Logins.Migrator, null);

    /// <summary>Creates a business as itself (RLS allows a session to write only its own business row).</summary>
    public async Task<Guid> CreateBusinessAsync(string name)
    {
        var id = Guid.CreateVersion7();
        await using var db = Db(id);
        db.Businesses.Add(new HelloPos.Api.Features.Businesses.Business
        {
            Id = id,
            Name = name,
            CountryCode = "IN",
            CurrencyCode = "INR",
            PricingMode = HelloPos.Api.Features.Businesses.PricingMode.TaxInclusive,
            FinancialYearStartMonth = 4,
            TimeZone = "Asia/Kolkata",
        });
        await db.SaveChangesAsync();
        return id;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();
}

/// <summary>Test-only authentication: never registered outside tests.</summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string Header = "X-Test-Authenticated-Business";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var value))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity([new Claim(ClaimsCurrentBusiness.ClaimType, value.ToString())], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
