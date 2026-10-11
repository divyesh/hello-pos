using System.Net.Http.Json;
using System.Text.Json.Nodes;
using HelloPos.Api.Features.Staff;
using HelloPos.Api.Infrastructure.Identity.Email;
using HelloPos.Api.Tests.Tenancy;
using Microsoft.AspNetCore.Identity;

namespace HelloPos.Api.Tests.Features.Staff;

/// <summary>Calls and checks shared by the staff tests (feature 006).</summary>
internal static class StaffApi
{
    public const string BusinessHeader = "X-HelloPos-Business";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static JsonObject NewStaff(string displayName, string role, string posUsername, string pin = "4826") =>
        new() { ["displayName"] = displayName, ["role"] = role, ["posUsername"] = posUsername, ["pin"] = pin };

    public static Task<HttpResponseMessage> AddAsync(HttpClient client, Guid business, JsonObject body) =>
        SendAsync(client, HttpMethod.Post, "/staff", business, body);

    public static Task<HttpResponseMessage> ListAsync(HttpClient client, Guid business, string? status = null) =>
        SendAsync(client, HttpMethod.Get, status is null ? "/staff" : $"/staff?status={status}", business);

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, Guid business, Guid staffId) =>
        SendAsync(client, HttpMethod.Get, $"/staff/{staffId}", business);

    public static Task<HttpResponseMessage> InviteAsync(HttpClient client, Guid business, string email, string role = "manager", string displayName = "Invited") =>
        SendAsync(client, HttpMethod.Post, "/staff/invitations", business,
            new JsonObject { ["email"] = email, ["role"] = role, ["displayName"] = displayName });

    public static Task<HttpResponseMessage> ListInvitationsAsync(HttpClient client, Guid business) =>
        SendAsync(client, HttpMethod.Get, "/staff/invitations", business);

    public static Task<HttpResponseMessage> PreviewAsync(HttpClient client, string token) =>
        client.PostAsJsonAsync("/invitations/preview", new { token }, Ct);

    public static Task<HttpResponseMessage> AcceptAsync(HttpClient client, string token) =>
        client.PostAsJsonAsync("/invitations/accept", new { token }, Ct);

    /// <summary>The token from the newest invitation email to <paramref name="email"/>.</summary>
    public static string InvitationTokenFor(TenancyApp app, string email)
    {
        var message = app.Emails.Last(email, OwnerEmails.InvitationKind);
        var marker = "accept-invitation?token=";
        var start = message.TextBody.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = message.TextBody.IndexOfAny([' ', '\r', '\n'], start);
        return Uri.UnescapeDataString(message.TextBody[start..(end < 0 ? message.TextBody.Length : end)]);
    }

    /// <summary>Checks a PIN the way POS sign-in (#7) will.</summary>
    public static async Task<bool> PinVerifiesAsync(TenancyApp app, Guid business, string username, string pin)
    {
        await using var db = app.Db(business);
        var credentials = new StaffCredentials(new PasswordHasher<StaffMember>(), app.Clock);
        return await credentials.VerifyPinAsync(db, username, pin, Ct) is not null;
    }

    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, Guid business, JsonObject? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add(BusinessHeader, business.ToString());
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request, Ct);
    }
}
