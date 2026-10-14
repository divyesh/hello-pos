using HelloPos.Web.Services;

namespace HelloPos.Web.Tests.Fakes;

/// <summary>In-memory business settings for page tests; starts with Cash and a 0% limit; the next change result can be set.</summary>
public sealed class FakeSettingsService : ISettingsService
{
    public FakeSettingsService() => Types.Add(new PaymentTypeItem(Guid.NewGuid(), "Cash", "cash", true, true, false, true, 0, true, 1));

    public List<PaymentTypeItem> Types { get; } = [];

    public SellingItem Selling { get; set; } = new(false, "0", null, "INR", false, [], 1);

    public List<string> Calls { get; } = [];

    public OwnerActionResult NextChange { get; set; } = OwnerActionResult.Success;

    public PaymentTypeItem Add(string name, string kind = "digitalWallet", bool active = true)
    {
        var type = new PaymentTypeItem(Guid.NewGuid(), name, kind, false, false, true, false, Types.Count, active, 1);
        Types.Add(type);
        return type;
    }

    public Task<LoadResult<IReadOnlyList<PaymentTypeItem>>> ListPaymentTypesAsync(Guid pageBusiness, CancellationToken cancellationToken = default) =>
        Task.FromResult<LoadResult<IReadOnlyList<PaymentTypeItem>>>(new LoadResult<IReadOnlyList<PaymentTypeItem>>.Loaded(Types.OrderBy(t => t.Position).ToList()));

    public Task<OwnerActionResult> CreatePaymentTypeAsync(Guid pageBusiness, string name, string kind, bool opensDrawer, bool asksReference, CancellationToken cancellationToken = default) =>
        Change($"create {name} {kind} drawer={opensDrawer} reference={asksReference}",
            () => Types.Add(new PaymentTypeItem(Guid.NewGuid(), name, kind, false, opensDrawer, asksReference, false, Types.Count, true, 1)));

    public Task<OwnerActionResult> UpdatePaymentTypeAsync(Guid pageBusiness, PaymentTypeItem type, string name, string kind, bool opensDrawer, bool asksReference, CancellationToken cancellationToken = default) =>
        Change($"update {type.Name} -> {name} {kind} drawer={opensDrawer} reference={asksReference} v{type.Version}",
            () => Replace(type, type with { Name = name, Kind = kind, OpensDrawer = opensDrawer, AsksReference = asksReference }));

    public Task<OwnerActionResult> SetPaymentTypeActiveAsync(Guid pageBusiness, PaymentTypeItem type, bool active, CancellationToken cancellationToken = default) =>
        Change($"{(active ? "reactivate" : "deactivate")} {type.Name}", () => Replace(type, type with { Active = active }));

    public Task<OwnerActionResult> ReorderPaymentTypesAsync(Guid pageBusiness, IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) =>
        Change($"reorder {string.Join(", ", ids.Select(id => Types.Single(t => t.Id == id).Name))}", () =>
        {
            for (var i = 0; i < ids.Count; i++)
            {
                var type = Types.Single(t => t.Id == ids[i]);
                Types[Types.IndexOf(type)] = type with { Position = i };
            }
        });

    public Task<LoadResult<SellingItem>> GetSellingAsync(Guid pageBusiness, CancellationToken cancellationToken = default) =>
        Task.FromResult<LoadResult<SellingItem>>(new LoadResult<SellingItem>.Loaded(Selling));

    public Task<OwnerActionResult> UpdateSellingAsync(Guid pageBusiness, SellingItem changed, CancellationToken cancellationToken = default) =>
        Change($"selling {changed.DiscountLimitPercent}% {changed.DiscountLimitAmount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"} v{changed.Version}",
            () => Selling = changed with { Version = changed.Version + 1 });

    private Task<OwnerActionResult> Change(string call, Action apply)
    {
        if (NextChange is OwnerActionResult.Succeeded)
        {
            Calls.Add(call);
            apply();
        }

        return Task.FromResult(NextChange);
    }

    private void Replace(PaymentTypeItem old, PaymentTypeItem updated) => Types[Types.FindIndex(t => t.Id == old.Id)] = updated with { Version = old.Version + 1 };
}
