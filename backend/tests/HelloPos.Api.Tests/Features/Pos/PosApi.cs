using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using HelloPos.Api.Tests.Features.Staff;
using HelloPos.Api.Tests.Tenancy;

namespace HelloPos.Api.Tests.Features.Pos;

/// <summary>A registered test device: its credential and the private key that opens its offline bundle.</summary>
internal sealed record TestDevice(Guid DeviceId, string Credential, RSA Key) : IDisposable
{
    public void Dispose() => Key.Dispose();
}

/// <summary>Calls and helpers shared by the device and POS tests (feature 007).</summary>
internal static class PosApi
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static Task<HttpResponseMessage> CreatePairingAsync(HttpClient owner, Guid business) =>
        StaffApi.SendAsync(owner, HttpMethod.Post, "/devices/pairings", business);

    public static Task<HttpResponseMessage> ListDevicesAsync(HttpClient owner, Guid business) =>
        StaffApi.SendAsync(owner, HttpMethod.Get, "/devices", business);

    public static Task<HttpResponseMessage> RenameAsync(HttpClient owner, Guid business, Guid deviceId, string name, int version) =>
        StaffApi.SendAsync(owner, HttpMethod.Put, $"/devices/{deviceId}", business, new JsonObject { ["name"] = name, ["version"] = version });

    public static Task<HttpResponseMessage> RevokeAsync(HttpClient owner, Guid business, Guid deviceId, int version) =>
        StaffApi.SendAsync(owner, HttpMethod.Post, $"/devices/{deviceId}/revoke", business, new JsonObject { ["version"] = version });

    public static string PublicKeyOf(RSA key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    public static Task<HttpResponseMessage> PairAsync(HttpClient client, string code, string deviceName, string publicKey) =>
        client.PostAsJsonAsync("/pos/pair", new { code, deviceName, publicKey }, Ct);

    /// <summary>Creates a code as <paramref name="owner"/> and pairs a new device with it.</summary>
    public static async Task<TestDevice> RegisterDeviceAsync(TenancyApp app, HttpClient owner, Guid business, string name = "Front till")
    {
        using var pairing = await CreatePairingAsync(owner, business);
        Assert.Equal(HttpStatusCode.Created, pairing.StatusCode);
        var code = (string)JsonNode.Parse(await pairing.Content.ReadAsStringAsync(Ct))!["code"]!;
        var key = RSA.Create(3072);
        using var anonymous = app.OwnerClient(withClientHeader: false);
        using var paired = await PairAsync(anonymous, code, name, PublicKeyOf(key));
        Assert.Equal(HttpStatusCode.OK, paired.StatusCode);
        var body = JsonNode.Parse(await paired.Content.ReadAsStringAsync(Ct))!;
        return new TestDevice(Guid.Parse((string)body["deviceId"]!), (string)body["deviceCredential"]!, key);
    }

    public static Task<HttpResponseMessage> PosSignInAsync(HttpClient device, string username, string pin) =>
        device.PostAsJsonAsync("/pos/sign-in", new { username, pin }, Ct);

    public static Task<HttpResponseMessage> PosRefreshAsync(HttpClient device, string refreshToken) =>
        device.PostAsJsonAsync("/pos/refresh", new { refreshToken }, Ct);

    public static Task<HttpResponseMessage> PosMeAsync(HttpClient session) =>
        session.GetAsync(new Uri("/pos/me", UriKind.Relative), Ct);

    public static Task<HttpResponseMessage> PosSignOutAsync(HttpClient session) =>
        session.PostAsync(new Uri("/pos/sign-out", UriKind.Relative), null, Ct);

    public static Task<HttpResponseMessage> PosChangePinAsync(HttpClient session, string currentPin, string newPin) =>
        session.PutAsJsonAsync("/pos/me/pin", new { currentPin, newPin }, Ct);

    public static Task<HttpResponseMessage> OfflineAccessAsync(HttpClient device) =>
        device.GetAsync(new Uri("/pos/offline-access", UriKind.Relative), Ct);

    public static Task<HttpResponseMessage> SessionFromOfflineAsync(HttpClient device, Guid staffId, DateTimeOffset signedInAt, DateTimeOffset? pinChangedAt) =>
        device.PostAsJsonAsync("/pos/sessions/offline", new { staffId, signedInAt, pinChangedAt }, Ct);

    public static Task<HttpResponseMessage> ReportAttemptsAsync(HttpClient device, params (string Username, bool Success, DateTimeOffset At)[] attempts) =>
        device.PostAsJsonAsync("/pos/attempts", new { attempts = attempts.Select(a => new { username = a.Username, success = a.Success, at = a.At }) }, Ct);

    /// <summary>Fetches and decrypts the offline bundle with <paramref name="key"/>; returns the parsed plaintext.</summary>
    public static async Task<JsonNode> OpenOfflineBundleAsync(HttpClient device, RSA key)
    {
        using var response = await OfflineAccessAsync(device);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bundle = Convert.FromBase64String((string)JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["bundle"]!);
        return JsonNode.Parse(HelloPos.Api.Features.Pos.OfflineAccess.Open(bundle, key))!;
    }

    /// <summary>Signs in and returns (access token, refresh token).</summary>
    public static async Task<(string Access, string Refresh)> SignedInAsync(HttpClient device, string username, string pin)
    {
        using var response = await PosSignInAsync(device, username, pin);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"sign-in failed: {(int)response.StatusCode} {body}");
        var tokens = JsonNode.Parse(body)!["tokens"]!;
        return ((string)tokens["accessToken"]!, (string)tokens["refreshToken"]!);
    }

    /// <summary>A client calling as the device (<c>Authorization: Device …</c>).</summary>
    public static HttpClient DeviceClient(TenancyApp app, string credential)
    {
        var client = app.OwnerClient(withClientHeader: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Device", credential);
        return client;
    }

    /// <summary>A client calling as a signed-in person (<c>Authorization: Bearer pat_…</c>).</summary>
    public static HttpClient SessionClient(TenancyApp app, string accessToken)
    {
        var client = app.OwnerClient(withClientHeader: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}
