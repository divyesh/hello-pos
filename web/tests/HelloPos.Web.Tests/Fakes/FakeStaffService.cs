using HelloPos.Kernel.Staff;
using HelloPos.Web.Services;

namespace HelloPos.Web.Tests.Fakes;

/// <summary>In-memory staff and invitations for page tests; results can be queued.</summary>
public sealed class FakeStaffService : IStaffService
{
    public List<StaffListItem> People { get; } = [];

    public List<InvitationItem> Invitations { get; } = [];

    public List<(string DisplayName, StaffRole Role, string PosUsername, string Pin)> Added { get; } = [];

    public List<(string Email, StaffRole Role, string DisplayName)> Invited { get; } = [];

    public List<string> Accepted { get; } = [];

    public List<string> ListedStatuses { get; } = [];

    public OwnerActionResult NextAdd { get; set; } = OwnerActionResult.Success;

    public OwnerActionResult NextAccept { get; set; } = OwnerActionResult.Success;

    public LoadResult<InvitationOffer> NextPreview { get; set; } =
        new LoadResult<InvitationOffer>.Loaded(new InvitationOffer("Patel Grocers", StaffRole.Manager, "Mina"));

    public Task<LoadResult<IReadOnlyList<StaffListItem>>> ListAsync(Guid pageBusiness, string status, CancellationToken cancellationToken = default)
    {
        ListedStatuses.Add(status);
        IReadOnlyList<StaffListItem> matching = People
            .Where(p => status == "all" || p.Active == (status == "active"))
            .ToList();
        return Task.FromResult<LoadResult<IReadOnlyList<StaffListItem>>>(new LoadResult<IReadOnlyList<StaffListItem>>.Loaded(matching));
    }

    public Task<OwnerActionResult> AddAsync(Guid pageBusiness, string displayName, StaffRole role, string posUsername, string pin, CancellationToken cancellationToken = default)
    {
        if (NextAdd is OwnerActionResult.Succeeded)
        {
            Added.Add((displayName, role, posUsername, pin));
            People.Add(new StaffListItem(Guid.NewGuid(), displayName, role, true, posUsername, true, false, 1));
        }

        return Task.FromResult(NextAdd);
    }

    public Task<LoadResult<IReadOnlyList<InvitationItem>>> ListInvitationsAsync(Guid pageBusiness, CancellationToken cancellationToken = default) =>
        Task.FromResult<LoadResult<IReadOnlyList<InvitationItem>>>(new LoadResult<IReadOnlyList<InvitationItem>>.Loaded([.. Invitations]));

    public Task<OwnerActionResult> InviteAsync(Guid pageBusiness, string email, StaffRole role, string displayName, CancellationToken cancellationToken = default)
    {
        Invited.Add((email, role, displayName));
        Invitations.Add(new InvitationItem(Guid.NewGuid(), email, role, displayName, "pending", DateTimeOffset.UtcNow.AddDays(7)));
        return Task.FromResult(OwnerActionResult.Success);
    }

    public Task<LoadResult<InvitationOffer>> PreviewAsync(string token, CancellationToken cancellationToken = default) =>
        Task.FromResult(NextPreview);

    public Task<OwnerActionResult> AcceptAsync(string token, CancellationToken cancellationToken = default)
    {
        Accepted.Add(token);
        return Task.FromResult(NextAccept);
    }
}
