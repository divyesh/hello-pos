using HelloPos.Kernel.Staff;
using HelloPos.Web.ApiClient.Models;
using Microsoft.Kiota.Abstractions;
using ApiStaffRole = HelloPos.Web.ApiClient.Models.NewStaffMember_role;

namespace HelloPos.Web.Services;

/// <summary>A person in the staff list. The PIN itself never reaches the browser.</summary>
public sealed record StaffListItem(
    Guid Id, string DisplayName, StaffRole Role, bool Active, string? PosUsername, bool HasPin, bool HasBackOfficeAccess, int Version);

/// <summary>An invitation in the owner's list; <c>Status</c> is pending, accepted, revoked or expired.</summary>
public sealed record InvitationItem(Guid Id, string Email, StaffRole Role, string DisplayName, string Status, DateTimeOffset ExpiresAt);

/// <summary>What an invitation link offers.</summary>
public sealed record InvitationOffer(string BusinessName, StaffRole Role, string DisplayName);

/// <summary>Something loaded for a page, a stale tab, or why it failed.</summary>
public abstract record LoadResult<T>
{
    private LoadResult()
    {
    }

    public sealed record Loaded(T Value) : LoadResult<T>;

    /// <summary>Another business was opened in this session (another tab).</summary>
    public sealed record BusinessChanged : LoadResult<T>;

    public sealed record Failed(string Message) : LoadResult<T>;
}

public interface IStaffService
{
    Task<LoadResult<IReadOnlyList<StaffListItem>>> ListAsync(Guid pageBusiness, string status, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> AddAsync(Guid pageBusiness, string displayName, StaffRole role, string posUsername, string pin, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> UpdateAsync(Guid pageBusiness, StaffListItem person, string displayName, StaffRole role, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> SetActiveAsync(Guid pageBusiness, StaffListItem person, bool active, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> SetPinAsync(Guid pageBusiness, StaffListItem person, string posUsername, string pin, CancellationToken cancellationToken = default);

    Task<LoadResult<IReadOnlyList<InvitationItem>>> ListInvitationsAsync(Guid pageBusiness, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> InviteAsync(Guid pageBusiness, string email, StaffRole role, string displayName, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> RevokeInvitationAsync(Guid pageBusiness, Guid invitationId, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> ResendInvitationAsync(Guid pageBusiness, Guid invitationId, CancellationToken cancellationToken = default);

    Task<LoadResult<InvitationOffer>> PreviewAsync(string token, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> AcceptAsync(string token, CancellationToken cancellationToken = default);
}

/// <summary>Staff and invitation calls (feature 006) through the client generated from the contract.</summary>
public sealed partial class StaffService(HttpClient http, ILogger<StaffService> logger) : IStaffService
{
    public const string InvalidInvitation = "This invitation link is invalid, already used or expired.";

    public Task<LoadResult<IReadOnlyList<StaffListItem>>> ListAsync(Guid pageBusiness, string status, CancellationToken cancellationToken = default) =>
        LoadAsync<IReadOnlyList<StaffListItem>>("list-staff", async () =>
        {
            var people = await ApiClients.Create(http).Staff.GetAsync(c =>
            {
                c.Headers.Add(BusinessService.BusinessHeader, pageBusiness.ToString());
                c.QueryParameters.Status = Enum.Parse<ApiClient.Staff.GetStatusQueryParameterType>(status, ignoreCase: true);
            }, cancellationToken).ConfigureAwait(false) ?? [];
            return people.Where(p => p.Id is not null).Select(p => new StaffListItem(
                p.Id!.Value, p.DisplayName ?? string.Empty, ParseRole(p.Role?.ToString()), p.Active ?? false, p.PosUsername,
                p.HasPin ?? false, p.HasBackOfficeAccess ?? false, p.Version ?? 0)).ToList();
        });

    public Task<OwnerActionResult> AddAsync(Guid pageBusiness, string displayName, StaffRole role, string posUsername, string pin, CancellationToken cancellationToken = default) =>
        OwnerAccountService.RunAsync(http, logger, "add-staff", () => ApiClients.Create(http).Staff.PostAsync(
            new NewStaffMember
            {
                DisplayName = displayName,
                Role = role == StaffRole.Manager ? ApiStaffRole.Manager : ApiStaffRole.Cashier,
                PosUsername = posUsername,
                Pin = pin,
            },
            c => c.Headers.Add(BusinessService.BusinessHeader, pageBusiness.ToString()), cancellationToken));

    public Task<OwnerActionResult> UpdateAsync(Guid pageBusiness, StaffListItem person, string displayName, StaffRole role, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(person);
        return OwnerAccountService.RunAsync(http, logger, "update-staff", () => ApiClients.Create(http).Staff[person.Id].PutAsync(
            new StaffUpdate
            {
                DisplayName = displayName,
                Role = Enum.Parse<StaffUpdate_role>(role.ToString()),
                Version = person.Version,
            },
            c => c.Headers.Add(BusinessService.BusinessHeader, pageBusiness.ToString()), cancellationToken));
    }

    public Task<OwnerActionResult> SetActiveAsync(Guid pageBusiness, StaffListItem person, bool active, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(person);
        var body = new StaffVersion { Version = person.Version };
        var item = ApiClients.Create(http).Staff[person.Id];
        return OwnerAccountService.RunAsync(http, logger, active ? "reactivate-staff" : "deactivate-staff", () => active
            ? item.Reactivate.PostAsync(body, c => c.Headers.Add(BusinessService.BusinessHeader, pageBusiness.ToString()), cancellationToken)
            : item.Deactivate.PostAsync(body, c => c.Headers.Add(BusinessService.BusinessHeader, pageBusiness.ToString()), cancellationToken));
    }

    public Task<OwnerActionResult> SetPinAsync(Guid pageBusiness, StaffListItem person, string posUsername, string pin, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(person);
        return OwnerAccountService.RunAsync(http, logger, "set-staff-pin", () => ApiClients.Create(http).Staff[person.Id].Pin.PutAsync(
            new StaffPin { PosUsername = posUsername, Pin = pin, Version = person.Version },
            c => c.Headers.Add(BusinessService.BusinessHeader, pageBusiness.ToString()), cancellationToken));
    }

    public Task<LoadResult<IReadOnlyList<InvitationItem>>> ListInvitationsAsync(Guid pageBusiness, CancellationToken cancellationToken = default) =>
        LoadAsync<IReadOnlyList<InvitationItem>>("list-invitations", async () =>
        {
            var invitations = await ApiClients.Create(http).Staff.Invitations
                .GetAsync(c => c.Headers.Add(BusinessService.BusinessHeader, pageBusiness.ToString()), cancellationToken)
                .ConfigureAwait(false) ?? [];
            return invitations.Where(i => i.Id is not null).Select(i => new InvitationItem(
                i.Id!.Value, i.Email ?? string.Empty, ParseRole(i.Role?.ToString()), i.DisplayName ?? string.Empty,
                i.Status?.ToString().ToLowerInvariant() ?? "pending", i.ExpiresAt ?? default)).ToList();
        });

    public Task<OwnerActionResult> InviteAsync(Guid pageBusiness, string email, StaffRole role, string displayName, CancellationToken cancellationToken = default) =>
        OwnerAccountService.RunAsync(http, logger, "invite-staff", () => ApiClients.Create(http).Staff.Invitations.PostAsync(
            new NewInvitation
            {
                Email = email,
                Role = role == StaffRole.Owner ? NewInvitation_role.Owner : NewInvitation_role.Manager,
                DisplayName = displayName,
            },
            c => c.Headers.Add(BusinessService.BusinessHeader, pageBusiness.ToString()), cancellationToken));

    public Task<OwnerActionResult> RevokeInvitationAsync(Guid pageBusiness, Guid invitationId, CancellationToken cancellationToken = default) =>
        OwnerAccountService.RunAsync(http, logger, "revoke-invitation", () => ApiClients.Create(http).Staff.Invitations[invitationId].Revoke
            .PostAsync(c => c.Headers.Add(BusinessService.BusinessHeader, pageBusiness.ToString()), cancellationToken));

    public Task<OwnerActionResult> ResendInvitationAsync(Guid pageBusiness, Guid invitationId, CancellationToken cancellationToken = default) =>
        OwnerAccountService.RunAsync(http, logger, "resend-invitation", () => ApiClients.Create(http).Staff.Invitations[invitationId].Resend
            .PostAsync(c => c.Headers.Add(BusinessService.BusinessHeader, pageBusiness.ToString()), cancellationToken));

    public async Task<LoadResult<InvitationOffer>> PreviewAsync(string token, CancellationToken cancellationToken = default)
    {
        var result = await LoadAsync<InvitationOffer>("preview-invitation", async () =>
        {
            var offer = await ApiClients.Create(http).Invitations.Preview
                .PostAsync(new InvitationToken { Token = token }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return new InvitationOffer(offer?.BusinessName ?? string.Empty, ParseRole(offer?.Role?.ToString()), offer?.DisplayName ?? string.Empty);
        }).ConfigureAwait(false);
        return result is LoadResult<InvitationOffer>.Failed { Message: not OwnerAccountService.Unreachable }
            ? new LoadResult<InvitationOffer>.Failed(InvalidInvitation)
            : result;
    }

    public Task<OwnerActionResult> AcceptAsync(string token, CancellationToken cancellationToken = default) =>
        OwnerAccountService.RunAsync(http, logger, "accept-invitation", () => ApiClients.Create(http).Invitations.Accept
            .PostAsync(new InvitationToken { Token = token }, cancellationToken: cancellationToken));

    private static StaffRole ParseRole(string? role) =>
        Enum.TryParse<StaffRole>(role, ignoreCase: true, out var parsed) ? parsed : StaffRole.Cashier;

    private Task<LoadResult<T>> LoadAsync<T>(string operation, Func<Task<T>> load) => LoadAsync(http, logger, operation, load);

    /// <summary>Runs a load call and turns its outcome into a <see cref="LoadResult{T}"/> (shared by the web services).</summary>
    internal static async Task<LoadResult<T>> LoadAsync<T>(HttpClient http, ILogger logger, string operation, Func<Task<T>> load)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(load);
        if (http.BaseAddress is null)
        {
            return new LoadResult<T>.Failed(OwnerAccountService.Unreachable);
        }

        try
        {
            return new LoadResult<T>.Loaded(await load().ConfigureAwait(false));
        }
        catch (ProblemDetails problem) when (problem.Type == OwnerAccountService.BusinessChangedType)
        {
            return new LoadResult<T>.BusinessChanged();
        }
        catch (ValidationProblemDetails problem)
        {
            var messages = OwnerAccountService.FieldMessages(problem).Values;
            return new LoadResult<T>.Failed(messages.Count > 0 ? string.Join(" ", messages) : problem.Title ?? "The request was not accepted.");
        }
        catch (ProblemDetails problem)
        {
            return new LoadResult<T>.Failed(problem.Status switch
            {
                401 => "No business is open, or your session has ended.",
                403 => "Your role in this business does not allow this.",
                _ => problem.Title ?? "This could not be loaded.",
            });
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            LogFailed(logger, operation, ex);
            return new LoadResult<T>.Failed(OwnerAccountService.Unreachable);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "API {Operation} request failed")]
    private static partial void LogFailed(ILogger logger, string operation, Exception exception);
}
