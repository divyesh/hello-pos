using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using HelloPos.Api.Infrastructure.Pos;
using HelloPos.Api.Tests.Features.Pos;
using HelloPos.Api.Tests.Infrastructure.Database;
using static HelloPos.Api.Tests.Features.Businesses.OpenAndEditTests;

namespace HelloPos.Api.Tests.Tenancy;

/// <summary>
/// Feature 007 FR-013, SC-005 on the shared harness: owner X has business A open, owner Y business B; A
/// never lists, renames or revokes B's devices, and a device credential cannot be bent to another business.
/// </summary>
public sealed class DeviceIsolationTests(PostgresFixture postgres) : IsolationTests(postgres)
{
    private readonly Dictionary<Guid, string> sessions = [];
    private readonly Dictionary<string, TestDevice> devices = [];

    protected override async Task<(Guid A, Guid B)> CreateBusinessesAsync() =>
        (await OwnerWithOpenBusinessAsync("x@example.com", "Business A"),
         await OwnerWithOpenBusinessAsync("y@example.com", "Business B"));

    protected override HttpClient ClientFor(Guid business) => App.OwnerClient(withClientHeader: true, sessions[business]);

    protected override async Task<IReadOnlyList<Guid>> SeedAsync(HttpClient asBusiness, string label)
    {
        var device = await PosApi.RegisterDeviceAsync(App, asBusiness, label == "A" ? BusinessA : BusinessB, $"{label} till");
        devices[label] = device;
        return [device.DeviceId];
    }

    [Fact]
    public Task ListDevices() => AssertCannotList("GET /devices", c => PosApi.ListDevicesAsync(c, BusinessA));

    [Fact]
    public Task RenameDevice() => AssertCannotModify("PUT /devices/{id}",
        id => Json(HttpMethod.Put, $"/devices/{id}", new JsonObject { ["name"] = "Taken", ["version"] = 1 }),
        id => $"/devices/{id}");

    [Fact]
    public Task RevokeDevice() => AssertCannotModify("POST /devices/{id}/revoke",
        id => Json(HttpMethod.Post, $"/devices/{id}/revoke", new JsonObject { ["version"] = 1 }),
        id => $"/devices/{id}");

    [Fact]
    public async Task ACredential_CannotBeBentToAnotherBusiness()
    {
        var a = devices["A"];
        var b = devices["B"];
        var parsedA = PosSecrets.Parse(PosSecretKind.DeviceCredential, a.Credential)!.Value;
        var parsedB = PosSecrets.Parse(PosSecretKind.DeviceCredential, b.Credential)!.Value;

        // A's secret with B's business and device id, and A's secret with B's business and A's device id.
        var forged = new[]
        {
            a.Credential.Replace($"{parsedA.BusinessId:N}.{parsedA.Id:N}", $"{parsedB.BusinessId:N}.{parsedB.Id:N}", StringComparison.Ordinal),
            a.Credential.Replace($"{parsedA.BusinessId:N}", $"{parsedB.BusinessId:N}", StringComparison.Ordinal),
        };

        foreach (var credential in forged)
        {
            using var client = App.OwnerClient(withClientHeader: false);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Device", credential);
            using var response = await client.GetAsync(new Uri("/pos/device", UriKind.Relative), Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    private static HttpRequestMessage Json(HttpMethod method, string path, JsonObject body) =>
        new(method, path) { Content = JsonContent.Create(body) };

    private async Task<Guid> OwnerWithOpenBusinessAsync(string email, string name)
    {
        using var owner = await App.SignUpVerifiedOwnerAsync(email);
        var id = await CreateIdAsync(owner, name);
        using var opened = await OpenAsync(owner, id);
        Assert.Equal(HttpStatusCode.NoContent, opened.StatusCode);
        sessions[id] = TenancyApp.SessionCookieOf(opened) ?? throw new InvalidOperationException("Opening did not renew the session cookie.");
        return id;
    }
}
