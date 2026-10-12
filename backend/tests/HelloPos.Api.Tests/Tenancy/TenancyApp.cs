using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Identity.Email;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using HelloPos.Api.Tests.Infrastructure;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy.Samples;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
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
/// Requests without that header use the real owner session cookie (feature 005); each
/// <see cref="OwnerClient"/> keeps its own cookies. Emails are captured in <see cref="Emails"/>, the
/// clock is <see cref="Clock"/>, and the owner rate limit is raised unless a test sets it.
/// </summary>
public sealed class TenancyApp : IAsyncDisposable
{
    public const string DefaultPassword = "a long passphrase nobody uses";

    private const string TestOrSession = "TestOrSession";

    private readonly WebApplicationFactory<Program> factory;

    private TenancyApp(
        DatabaseLogins logins, int maxPoolSize, Action<IServiceCollection, DatabaseLogins>? configureServices,
        IReadOnlyDictionary<string, string?>? settings)
    {
        Logins = logins;
        AppConnectionString = new NpgsqlConnectionStringBuilder(logins.App) { MaxPoolSize = maxPoolSize }.ConnectionString;
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:HelloPos", AppConnectionString);
            builder.UseSetting("RateLimits:OwnerAuthPerMinute", "100000");
            builder.UseSetting("RateLimits:PosAuthPerMinute", "100000");
            foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddDbContext<SampleDbContext>(o => o.UseHelloPosDatabase(AppConnectionString));
                services.AddSingleton<IEndpointMapper, SampleEndpoints>();
                services.AddSingleton<IEmailSender>(Emails);
                services.AddSingleton<TimeProvider>(Clock);

                // The test header when present, otherwise the production choice (POS headers, else the cookie).
                services.AddAuthentication(TestOrSession)
                    .AddPolicyScheme(TestOrSession, TestOrSession, o => o.ForwardDefaultSelector = context =>
                        context.Request.Headers.ContainsKey(TestAuthHandler.Header)
                            ? TestAuthHandler.SchemeName
                            : HelloPos.Api.Infrastructure.Pos.PosAuthentication.SchemeFor(context.Request) ?? IdentityConstants.ApplicationScheme)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                configureServices?.Invoke(services, logins);
            });
        });
    }

    public DatabaseLogins Logins { get; }

    public string AppConnectionString { get; }

    public InMemoryEmailSender Emails { get; } = new();

    public TestClock Clock { get; } = new();

    public static async Task<TenancyApp> CreateAsync(
        PostgresFixture postgres, int maxPoolSize = 20, Action<IServiceCollection, DatabaseLogins>? configureServices = null,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        var logins = await postgres.CreateEmptyDatabaseAsync();
        await new Migrator(Microsoft.Extensions.Logging.Abstractions.NullLogger<Migrator>.Instance)
            .RunAsync(logins.Settings, CancellationToken.None);
        await SampleSchema.CreateAsync(logins.Migrator);
        return new TenancyApp(logins, maxPoolSize, configureServices, settings);
    }

    /// <summary>
    /// A browser-like client for the owner endpoints: keeps its own cookies and sends the CSRF header
    /// (<c>X-HelloPos-Client: web</c>). Not signed in until it signs in.
    /// </summary>
    public HttpClient OwnerClient(bool withClientHeader = true) => OwnerClient(withClientHeader, sessionCookie: null);

    /// <summary>
    /// With <paramref name="sessionCookie"/> (a <c>name=value</c> pair from a <c>Set-Cookie</c>
    /// header), every request sends that session and the client stores no cookies of its own.
    /// </summary>
    public HttpClient OwnerClient(bool withClientHeader, string? sessionCookie)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = sessionCookie is null });
        if (withClientHeader)
        {
            client.DefaultRequestHeaders.Add(RequireClientHeaderExtensions.HeaderName, RequireClientHeaderExtensions.WebClient);
        }

        if (sessionCookie is not null)
        {
            client.DefaultRequestHeaders.Add("Cookie", sessionCookie);
        }

        return client;
    }

    /// <summary>The session cookie (<c>name=value</c>) a response set, or null.</summary>
    public static string? SessionCookieOf(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(v => v.Split(';')[0]).LastOrDefault(v => v.StartsWith(IdentitySetup.CookieName + "=", StringComparison.Ordinal))
            : null;
    }

    /// <summary>Signs up <paramref name="email"/>, follows the verification link and signs in; returns that client.</summary>
    public async Task<HttpClient> SignUpVerifiedOwnerAsync(string email, string password = DefaultPassword, string displayName = "Owner")
    {
        var client = OwnerClient();
        await ExpectAsync(HttpStatusCode.Accepted,
            await client.PostAsJsonAsync("/owner/sign-up", new { displayName, email, password }));
        var (ownerId, token) = InMemoryEmailSender.LinkOf(Emails.Last(email, OwnerEmails.VerificationKind));
        await ExpectAsync(HttpStatusCode.NoContent,
            await client.PostAsJsonAsync("/owner/verify-email", new { ownerId, token }));
        await ExpectAsync(HttpStatusCode.NoContent,
            await client.PostAsJsonAsync("/owner/sign-in", new { email, password }));
        return client;
    }

    /// <summary>The signed-in account's id (from <c>/owner/me</c>).</summary>
    public static async Task<Guid> AccountIdAsync(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        using var me = await client.GetAsync(new Uri("/owner/me", UriKind.Relative));
        return (await me.Content.ReadFromJsonAsync<AccountId>())!.OwnerId;
    }

    /// <summary>Adds a staff member to <paramref name="business"/> directly (acting as the business); returns its id.</summary>
    public async Task<Guid> AddStaffAsync(
        Guid business, HelloPos.Kernel.Staff.StaffRole role, Guid? accountId = null, string displayName = "Person",
        string? posUsername = null, string? pin = null, bool active = true)
    {
        await using var db = Db(business);
        var staff = new HelloPos.Api.Features.Staff.StaffMember
        {
            DisplayName = displayName,
            Role = role,
            AccountId = accountId,
            Active = active,
        };
        if (posUsername is not null)
        {
            new HelloPos.Api.Features.Staff.StaffCredentials(
                    new PasswordHasher<HelloPos.Api.Features.Staff.StaffMember>(), Clock)
                .SetPin(staff, posUsername, pin ?? "4826");
        }

        db.StaffMembers.Add(staff);
        await db.SaveChangesAsync();
        return staff.Id;
    }

    /// <summary>
    /// A signed-in back-office client for a new account that is <paramref name="role"/> staff of
    /// <paramref name="business"/>, with that business open.
    /// </summary>
    public async Task<(HttpClient Client, Guid StaffId)> SignedInStaffClientAsync(
        string email, Guid business, HelloPos.Kernel.Staff.StaffRole role)
    {
        var client = await SignUpVerifiedOwnerAsync(email, displayName: email.Split('@')[0]);
        var staffId = await AddStaffAsync(business, role, await AccountIdAsync(client), email.Split('@')[0]);
        await ExpectAsync(HttpStatusCode.NoContent,
            await client.PostAsync(new Uri($"/owner/businesses/{business}/open", UriKind.Relative), null));
        return (client, staffId);
    }

    private sealed record AccountId(Guid OwnerId);

    /// <summary>The API's services (for calling slices directly in tests).</summary>
    public IServiceProvider Services => factory.Services;

    /// <summary>The API's own context, acting as <paramref name="businessId"/> (as device and session calls do).</summary>
    public static HelloPos.Api.Infrastructure.Persistence.HelloPosDbContext ActingAs(IServiceProvider services, Guid businessId) =>
        HelloPos.Api.Infrastructure.Pos.PosAuthentication.ActingAs(services, businessId);

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
            PricingMode = HelloPos.Kernel.Businesses.PricingMode.TaxInclusive,
            FinancialYearStartMonth = 4,
            TimeZone = "Asia/Kolkata",
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task ExpectAsync(HttpStatusCode expected, HttpResponseMessage response)
    {
        if (response.StatusCode != expected)
        {
            throw new InvalidOperationException(
                $"{response.RequestMessage?.RequestUri} returned {(int)response.StatusCode}, expected {(int)expected}: "
                + await response.Content.ReadAsStringAsync());
        }
    }

    public async ValueTask DisposeAsync()
    {
        await factory.DisposeAsync();

        // Close this database's idle pooled connections so finished tests do not hold server slots.
        foreach (var connectionString in new[] { AppConnectionString, Logins.App, Logins.Migrator, Logins.Admin })
        {
            using var connection = new NpgsqlConnection(ConnectionStrings.Normalize(connectionString));
            NpgsqlConnection.ClearPool(connection);
        }
    }
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
