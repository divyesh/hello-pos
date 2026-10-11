using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using HelloPos.Api.Infrastructure.Identity.Email;
using HelloPos.Api.Tests.Contract;
using HelloPos.Api.Tests.Infrastructure;
using HelloPos.Api.Tests.Tenancy;

namespace HelloPos.Api.Tests.Features.Owners;

/// <summary>Calls and checks shared by the owner account tests (feature 005, US1).</summary>
internal static class OwnerApi
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static Task<HttpResponseMessage> SignUpAsync(HttpClient client, string email, string password = TenancyApp.DefaultPassword, string displayName = "Asha Patel") =>
        client.PostAsJsonAsync("/owner/sign-up", new { displayName, email, password }, Ct);

    public static Task<HttpResponseMessage> VerifyAsync(HttpClient client, Guid ownerId, string token) =>
        client.PostAsJsonAsync("/owner/verify-email", new { ownerId, token }, Ct);

    public static Task<HttpResponseMessage> SignInAsync(HttpClient client, string email, string password = TenancyApp.DefaultPassword) =>
        client.PostAsJsonAsync("/owner/sign-in", new { email, password }, Ct);

    public static Task<HttpResponseMessage> MeAsync(HttpClient client) =>
        client.GetAsync(new Uri("/owner/me", UriKind.Relative), Ct);

    /// <summary>Signs up and returns the verification link's owner id and token (not yet verified).</summary>
    public static async Task<(Guid OwnerId, string Token)> SignUpUnverifiedAsync(TenancyApp app, HttpClient client, string email)
    {
        Assert.Equal(HttpStatusCode.Accepted, (await SignUpAsync(client, email)).StatusCode);
        return InMemoryEmailSender.LinkOf(app.Emails.Last(email, OwnerEmails.VerificationKind));
    }

    /// <summary>Asserts the status and that the body matches the contract for that operation and status.</summary>
    public static async Task<string> AssertContractAsync(
        HttpResponseMessage response, HttpStatusCode status, string operationId)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(status == response.StatusCode, $"expected {(int)status}, got {(int)response.StatusCode}: {body}");
        if (body.Length > 0)
        {
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "application/json";
            var violations = OpenApiContract.Validate(
                OpenApiContract.ResponseSchema(operationId, ((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture), mediaType), body);
            Assert.True(violations.Count == 0,
                $"{operationId} {(int)status} does not match the contract:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}{Environment.NewLine}{body}");
        }

        return body;
    }

    /// <summary>The field names reported in a validation problem.</summary>
    public static IReadOnlyCollection<string> ErrorFields(string problemJson) =>
        JsonNode.Parse(problemJson)?["errors"]?.AsObject().Select(e => e.Key).ToList() ?? [];

    /// <summary>A problem body without its per-request trace id, to compare responses.</summary>
    public static string WithoutTraceId(string problemJson)
    {
        var node = JsonNode.Parse(problemJson)!.AsObject();
        node.Remove("traceId");
        return node.ToJsonString();
    }
}
