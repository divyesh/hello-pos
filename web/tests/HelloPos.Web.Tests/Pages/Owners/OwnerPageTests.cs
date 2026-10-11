using AngleSharp.Dom;
using Bunit;
using HelloPos.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace HelloPos.Web.Tests.Pages.Owners;

/// <summary>Shared setup for the owner account pages (feature 005, US1): a fake account service.</summary>
public abstract class OwnerPageTests : BunitContext
{
    protected OwnerPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IOwnerAccountService>(Accounts);
        Services.AddSingleton<OwnerAuthenticationStateProvider>();
    }

    protected FakeOwnerAccountService Accounts { get; } = new();

    protected NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    protected static void Type(IRenderedComponent<IComponent> page, string testId, string value) =>
        InputOf(page.Find($"[data-testid={testId}]")).Change(value);

    protected static void Click(IRenderedComponent<IComponent> page, string testId) =>
        page.Find($"[data-testid={testId}]").Click();

    protected static OwnerActionResult Fields(string field, string message) =>
        new OwnerActionResult.FieldErrors(new Dictionary<string, string> { [field] = message });

    private static IElement InputOf(IElement element) =>
        element.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase) ? element : element.QuerySelector("input")!;
}

/// <summary>Records calls and returns the result queued for each operation (success by default).</summary>
public sealed class FakeOwnerAccountService : IOwnerAccountService
{
    private readonly Dictionary<string, OwnerActionResult> next = new(StringComparer.Ordinal);

    public List<(string Operation, string[] Arguments)> Calls { get; } = [];

    public OwnerSession? Session { get; set; }

    public void Next(string operation, OwnerActionResult result) => next[operation] = result;

    /// <summary>Call number <paramref name="index"/> as "operation|argument|...".</summary>
    public string Describe(int index) => Describe(Calls[index].Operation, Calls[index].Arguments);

    public static string Describe(string operation, params string[] arguments) =>
        string.Join('|', [operation, .. arguments]);

    public Task<OwnerActionResult> SignUpAsync(string displayName, string email, string password, CancellationToken cancellationToken = default) =>
        Record("sign-up", displayName, email, password);

    public Task<OwnerActionResult> VerifyEmailAsync(Guid ownerId, string token, CancellationToken cancellationToken = default) =>
        Record("verify-email", ownerId.ToString(), token);

    public Task<OwnerActionResult> ResendVerificationAsync(string email, CancellationToken cancellationToken = default) =>
        Record("resend-verification", email);

    public Task<OwnerActionResult> SignInAsync(string email, string password, CancellationToken cancellationToken = default) =>
        Record("sign-in", email, password);

    public Task<OwnerActionResult> SignOutAsync(CancellationToken cancellationToken = default) => Record("sign-out");

    public Task<OwnerActionResult> ForgotPasswordAsync(string email, CancellationToken cancellationToken = default) =>
        Record("forgot-password", email);

    public Task<OwnerActionResult> ResetPasswordAsync(Guid ownerId, string token, string newPassword, CancellationToken cancellationToken = default) =>
        Record("reset-password", ownerId.ToString(), token, newPassword);

    public Task<OwnerSession?> GetSessionAsync(CancellationToken cancellationToken = default) => Task.FromResult(Session);

    private Task<OwnerActionResult> Record(string operation, params string[] arguments)
    {
        Calls.Add((operation, arguments));
        return Task.FromResult(next.Remove(operation, out var result) ? result : OwnerActionResult.Success);
    }
}
