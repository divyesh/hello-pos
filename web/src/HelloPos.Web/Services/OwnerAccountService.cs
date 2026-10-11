using HelloPos.Web.ApiClient;
using HelloPos.Web.ApiClient.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;

namespace HelloPos.Web.Services;

/// <summary>Outcome of an owner account action, as the pages show it.</summary>
public abstract record OwnerActionResult
{
    private OwnerActionResult()
    {
    }

    public static readonly OwnerActionResult Success = new Succeeded();

    public sealed record Succeeded : OwnerActionResult;

    /// <summary>The API rejected fields; keys are the API's camelCase field names.</summary>
    public sealed record FieldErrors(IReadOnlyDictionary<string, string> Errors) : OwnerActionResult;

    /// <summary>Correct password, email not verified yet (sign-in only).</summary>
    public sealed record VerificationRequired : OwnerActionResult;

    /// <summary>Another business was opened in this session (another tab): this page is out of date.</summary>
    public sealed record BusinessChanged : OwnerActionResult;

    public sealed record Failed(string Message) : OwnerActionResult;
}

/// <summary>The signed-in owner, from <c>GET /owner/me</c>.</summary>
public sealed record OwnerSession(Guid OwnerId, string DisplayName, string Email, Guid? CurrentBusinessId);

public interface IOwnerAccountService
{
    Task<OwnerActionResult> SignUpAsync(string displayName, string email, string password, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> VerifyEmailAsync(Guid ownerId, string token, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> ResendVerificationAsync(string email, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> SignInAsync(string email, string password, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> SignOutAsync(CancellationToken cancellationToken = default);

    Task<OwnerActionResult> ForgotPasswordAsync(string email, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> ResetPasswordAsync(Guid ownerId, string token, string newPassword, CancellationToken cancellationToken = default);

    /// <summary>The signed-in owner, or <c>null</c> when there is no session.</summary>
    Task<OwnerSession?> GetSessionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Owner account calls through the client generated from the contract (feature 005, US1). Problem
/// responses become <see cref="OwnerActionResult"/>s with the server's messages; failures are logged
/// without request bodies (they carry passwords and tokens).
/// </summary>
public sealed partial class OwnerAccountService(HttpClient http, ILogger<OwnerAccountService> logger) : IOwnerAccountService
{
    public const string VerificationRequiredType = "urn:hellopos:problem:verification-required";
    public const string BusinessChangedType = "urn:hellopos:problem:business-changed";
    public const string Unreachable = "Hello POS cannot be reached. Check your connection and try again.";

    private HelloPosApiClient Api => ApiClients.Create(http);

    public Task<OwnerActionResult> SignUpAsync(string displayName, string email, string password, CancellationToken cancellationToken = default) =>
        RunAsync("sign-up", () => Api.Owner.SignUp.PostAsync(
            new SignUpRequest { DisplayName = displayName, Email = email, Password = password }, cancellationToken: cancellationToken));

    public Task<OwnerActionResult> VerifyEmailAsync(Guid ownerId, string token, CancellationToken cancellationToken = default) =>
        RunAsync("verify-email", () => Api.Owner.VerifyEmail.PostAsync(
            new OwnerTokenRequest { OwnerId = ownerId, Token = token }, cancellationToken: cancellationToken));

    public Task<OwnerActionResult> ResendVerificationAsync(string email, CancellationToken cancellationToken = default) =>
        RunAsync("resend-verification", () => Api.Owner.ResendVerification.PostAsync(
            new EmailRequest { Email = email }, cancellationToken: cancellationToken));

    public Task<OwnerActionResult> SignInAsync(string email, string password, CancellationToken cancellationToken = default) =>
        RunAsync("sign-in", () => Api.Owner.SignIn.PostAsync(
            new SignInRequest { Email = email, Password = password }, cancellationToken: cancellationToken));

    public Task<OwnerActionResult> SignOutAsync(CancellationToken cancellationToken = default) =>
        RunAsync("sign-out", () => Api.Owner.SignOut.PostAsync(cancellationToken: cancellationToken));

    public Task<OwnerActionResult> ForgotPasswordAsync(string email, CancellationToken cancellationToken = default) =>
        RunAsync("forgot-password", () => Api.Owner.ForgotPassword.PostAsync(
            new EmailRequest { Email = email }, cancellationToken: cancellationToken));

    public Task<OwnerActionResult> ResetPasswordAsync(Guid ownerId, string token, string newPassword, CancellationToken cancellationToken = default) =>
        RunAsync("reset-password", () => Api.Owner.ResetPassword.PostAsync(
            new ResetPasswordRequest { OwnerId = ownerId, Token = token, NewPassword = newPassword }, cancellationToken: cancellationToken));

    public async Task<OwnerSession?> GetSessionAsync(CancellationToken cancellationToken = default)
    {
        if (http.BaseAddress is null)
        {
            return null;
        }

        try
        {
            var me = await Api.Owner.Me.GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return me is { OwnerId: { } id, DisplayName: { } name, Email: { } email }
                ? new OwnerSession(id, name, email, me.CurrentBusinessId)
                : null;
        }
        catch (ProblemDetails)
        {
            return null; // 401: not signed in
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            LogFailed(logger, "me", ex);
            return null;
        }
    }

    private Task<OwnerActionResult> RunAsync(string operation, Func<Task> call) => RunAsync(http, logger, operation, call);

    /// <summary>
    /// Runs an API call and turns its outcome into an <see cref="OwnerActionResult"/>: field errors,
    /// verification required, the server's problem title, or "cannot be reached" (logged).
    /// </summary>
    internal static async Task<OwnerActionResult> RunAsync(HttpClient http, ILogger logger, string operation, Func<Task> call)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(call);
        if (http.BaseAddress is null)
        {
            return new OwnerActionResult.Failed(Unreachable);
        }

        try
        {
            await call().ConfigureAwait(false);
            return OwnerActionResult.Success;
        }
        catch (ValidationProblemDetails problem)
        {
            var errors = FieldMessages(problem);
            return errors.Count > 0
                ? new OwnerActionResult.FieldErrors(errors)
                : new OwnerActionResult.Failed(problem.Title ?? "The request was not accepted.");
        }
        catch (ProblemDetails problem) when (problem.Type == VerificationRequiredType)
        {
            return new OwnerActionResult.VerificationRequired();
        }
        catch (ProblemDetails problem) when (problem.Type == BusinessChangedType)
        {
            return new OwnerActionResult.BusinessChanged();
        }
        catch (ProblemDetails problem)
        {
            return new OwnerActionResult.Failed(problem.Title ?? "The request was not accepted.");
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            LogFailed(logger, operation, ex);
            return new OwnerActionResult.Failed(Unreachable);
        }
    }

    private static Dictionary<string, string> FieldMessages(ValidationProblemDetails problem)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (field, value) in problem.Errors?.AdditionalData ?? new Dictionary<string, object>())
        {
            var messages = value switch
            {
                UntypedArray array => array.GetValue().OfType<UntypedString>().Select(s => s.GetValue()).OfType<string>(),
                IEnumerable<object> list => list.Select(o => o?.ToString()).OfType<string>(),
                _ => [],
            };
            var text = string.Join(" ", messages);
            if (text.Length > 0)
            {
                errors[field] = text;
            }
        }

        return errors;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "API {Operation} request failed")]
    private static partial void LogFailed(ILogger logger, string operation, Exception exception);
}
