using System.Net;
using HelloPos.Api.Tests.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Sdk;

namespace HelloPos.Api.Tests.Tenancy;

/// <summary>A cross-business leak found by the isolation harness.</summary>
public sealed class IsolationViolationException(string message) : XunitException(message);

/// <summary>
/// The shared isolation harness (spec FR-015, FR-016). A feature slice's isolation test derives from
/// this class, says how to create one of its records through its own API (<see cref="SeedAsync"/>),
/// and calls the assertions with its own operations. The harness creates businesses A and B, seeds
/// both, and checks that acting as A never reaches B's records. Failures name the operation and the
/// leaked record. See docs/tenancy.md.
/// </summary>
[Collection(PostgresTests.Name)]
public abstract class IsolationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private TenancyApp? app;

    protected TenancyApp App => app ?? throw new InvalidOperationException("Not initialised.");

    protected Guid BusinessA { get; private set; }

    protected Guid BusinessB { get; private set; }

    /// <summary>Records created as business A by <see cref="SeedAsync"/>.</summary>
    protected IReadOnlyList<Guid> RecordsOfA { get; private set; } = [];

    /// <summary>Records created as business B by <see cref="SeedAsync"/>: none of these may leak to A.</summary>
    protected IReadOnlyList<Guid> RecordsOfB { get; private set; } = [];

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Creates a few records through the slice's API as the given business; returns their ids.</summary>
    protected abstract Task<IReadOnlyList<Guid>> SeedAsync(HttpClient asBusiness, string label);

    /// <summary>Extra test services (only for the harness's own negative tests).</summary>
    protected virtual void ConfigureServices(IServiceCollection services, DatabaseLogins logins)
    {
    }

    public async ValueTask InitializeAsync()
    {
        app = await TenancyApp.CreateAsync(postgres, configureServices: ConfigureServices);
        BusinessA = await App.CreateBusinessAsync("Business A");
        BusinessB = await App.CreateBusinessAsync("Business B");
        using var asA = App.Client(BusinessA);
        using var asB = App.Client(BusinessB);
        RecordsOfA = await SeedAsync(asA, "A");
        RecordsOfB = await SeedAsync(asB, "B");
        if (RecordsOfB.Count == 0)
        {
            throw new InvalidOperationException("SeedAsync must create at least one record, or nothing is tested.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null)
        {
            await app.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    protected HttpClient AsA() => App.Client(BusinessA);

    protected HttpClient AsB() => App.Client(BusinessB);

    /// <summary>Listing as A returns none of B's records.</summary>
    protected async Task AssertCannotList(string operation, Func<HttpClient, Task<HttpResponseMessage>> list)
    {
        ArgumentNullException.ThrowIfNull(list);
        using var client = AsA();
        using var response = await list(client);
        var body = await response.Content.ReadAsStringAsync(Ct);
        foreach (var leaked in RecordsOfB.Where(id => body.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new IsolationViolationException($"{operation}: acting as business A, the response contained business B's record {leaked}.");
        }
    }

    /// <summary>Reading any of B's records by id as A is "not found".</summary>
    protected async Task AssertCannotRead(string operation, Func<Guid, string> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var client = AsA();
        foreach (var id in RecordsOfB)
        {
            using var response = await client.GetAsync(new Uri(path(id), UriKind.Relative), Ct);
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                throw new IsolationViolationException(
                    $"{operation}: acting as business A, reading business B's record {id} returned {(int)response.StatusCode} instead of 404.");
            }
        }
    }

    /// <summary>
    /// Changing (update or delete) any of B's records as A is "not found", and the record is unchanged
    /// when B reads it afterwards.
    /// </summary>
    protected async Task AssertCannotModify(string operation, Func<Guid, HttpRequestMessage> modify, Func<Guid, string> readPath)
    {
        ArgumentNullException.ThrowIfNull(modify);
        ArgumentNullException.ThrowIfNull(readPath);
        using var asA = AsA();
        using var asB = AsB();
        foreach (var id in RecordsOfB)
        {
            var before = await asB.GetStringAsync(new Uri(readPath(id), UriKind.Relative), Ct);
            using var request = modify(id);
            using var response = await asA.SendAsync(request, Ct);
            using var after = await asB.GetAsync(new Uri(readPath(id), UriKind.Relative), Ct);
            var afterBody = await after.Content.ReadAsStringAsync(Ct);
            if (response.StatusCode != HttpStatusCode.NotFound || after.StatusCode != HttpStatusCode.OK || afterBody != before)
            {
                throw new IsolationViolationException(
                    $"{operation}: acting as business A, {request.Method} on business B's record {id} returned {(int)response.StatusCode} " +
                    $"and the record is now {(after.StatusCode == HttpStatusCode.OK ? (afterBody == before ? "unchanged" : "changed") : "gone")}.");
            }
        }
    }

    /// <summary>A write as A that refers to any of B's records is "not found" and creates nothing.</summary>
    protected async Task AssertCannotReference(string operation, Func<Guid, HttpRequestMessage> referTo, Func<HttpClient, Task<HttpResponseMessage>> listOwn)
    {
        ArgumentNullException.ThrowIfNull(referTo);
        ArgumentNullException.ThrowIfNull(listOwn);
        using var asA = AsA();
        using var asB = AsB();
        foreach (var id in RecordsOfB)
        {
            var ownBefore = await ReadAsync(asA, listOwn);
            var theirsBefore = await ReadAsync(asB, listOwn);
            using var request = referTo(id);
            using var response = await asA.SendAsync(request, Ct);
            var ownAfter = await ReadAsync(asA, listOwn);
            var theirsAfter = await ReadAsync(asB, listOwn);
            if (response.StatusCode != HttpStatusCode.NotFound || ownAfter != ownBefore || theirsAfter != theirsBefore)
            {
                throw new IsolationViolationException(
                    $"{operation}: acting as business A, a write referring to business B's record {id} returned {(int)response.StatusCode} " +
                    "and changed stored data.");
            }
        }
    }

    private static async Task<string> ReadAsync(HttpClient client, Func<HttpClient, Task<HttpResponseMessage>> list)
    {
        using var response = await list(client);
        return await response.Content.ReadAsStringAsync(Ct);
    }
}
