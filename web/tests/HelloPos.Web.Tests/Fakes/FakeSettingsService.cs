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

    /// <summary>Areas without their tables (composed on listing from <see cref="Tables"/>).</summary>
    public List<TableAreaItem> Areas { get; } = [];

    public List<DiningTableItem> Tables { get; } = [];

    public TableAreaItem AddArea(string name, params string[] tables)
    {
        var area = new TableAreaItem(Guid.NewGuid(), name, Areas.Count, true, 1, []);
        Areas.Add(area);
        foreach (var table in tables)
        {
            AddTable(area.Id, table);
        }

        return area;
    }

    public void AddTable(Guid areaId, string name, int seats = 4, bool active = true) =>
        Tables.Add(new DiningTableItem(Guid.NewGuid(), areaId, name, seats, Tables.Count(t => t.AreaId == areaId), active, 1));

    public Task<LoadResult<IReadOnlyList<TableAreaItem>>> ListAreasAsync(Guid pageBusiness, CancellationToken cancellationToken = default) =>
        Task.FromResult<LoadResult<IReadOnlyList<TableAreaItem>>>(new LoadResult<IReadOnlyList<TableAreaItem>>.Loaded(
            Areas.OrderBy(a => a.Position)
                .Select(a => a with { Tables = Tables.Where(t => t.AreaId == a.Id).OrderBy(t => t.Position).ToList() })
                .ToList()));

    public Task<OwnerActionResult> CreateAreaAsync(Guid pageBusiness, string name, CancellationToken cancellationToken = default) =>
        Change($"create area {name}", () => AddArea(name));

    public Task<OwnerActionResult> UpdateAreaAsync(Guid pageBusiness, TableAreaItem area, string name, int position, CancellationToken cancellationToken = default) =>
        Change($"update area {area.Name} -> {name} at {position} v{area.Version}", () =>
        {
            var others = Areas.Where(a => a.Id != area.Id).OrderBy(a => a.Position).ToList();
            others.Insert(Math.Clamp(position, 0, others.Count), Areas.Single(a => a.Id == area.Id) with { Name = name, Version = area.Version + 1 });
            Areas.Clear();
            Areas.AddRange(others.Select((a, i) => a with { Position = i }));
        });

    public Task<OwnerActionResult> SetAreaActiveAsync(Guid pageBusiness, TableAreaItem area, bool active, CancellationToken cancellationToken = default) =>
        Change($"{(active ? "reactivate" : "deactivate")} area {area.Name}",
            () => Areas[Areas.FindIndex(a => a.Id == area.Id)] = area with { Active = active, Version = area.Version + 1, Tables = [] });

    public Task<OwnerActionResult> CreateTableAsync(Guid pageBusiness, Guid areaId, string name, int seats, CancellationToken cancellationToken = default) =>
        Change($"create table {name} seats={seats} in {AreaName(areaId)}", () => AddTable(areaId, name, seats));

    public Task<OwnerActionResult> CreateTablesAsync(
        Guid pageBusiness, Guid areaId, int first, int last, string prefix, string suffix, int seats, CancellationToken cancellationToken = default) =>
        Change($"create tables {prefix}[{first}-{last}]{suffix} seats={seats} in {AreaName(areaId)}", () =>
        {
            for (var n = first; n <= last; n++)
            {
                AddTable(areaId, $"{prefix}{n}{suffix}", seats);
            }
        });

    public Task<OwnerActionResult> UpdateTableAsync(
        Guid pageBusiness, DiningTableItem table, Guid areaId, string name, int seats, int position, CancellationToken cancellationToken = default) =>
        Change($"update table {table.Name} -> {name} seats={seats} in {AreaName(areaId)} at {position} v{table.Version}", () =>
        {
            Tables.RemoveAll(t => t.Id == table.Id);
            var others = Tables.Where(t => t.AreaId == areaId).OrderBy(t => t.Position).ToList();
            others.Insert(Math.Clamp(position, 0, others.Count), table with { AreaId = areaId, Name = name, Seats = seats, Version = table.Version + 1 });
            Tables.RemoveAll(t => t.AreaId == areaId);
            Tables.AddRange(others.Select((t, i) => t with { Position = i }));
        });

    public Task<OwnerActionResult> SetTableActiveAsync(Guid pageBusiness, DiningTableItem table, bool active, CancellationToken cancellationToken = default) =>
        Change($"{(active ? "reactivate" : "deactivate")} table {table.Name}",
            () => Tables[Tables.FindIndex(t => t.Id == table.Id)] = table with { Active = active, Version = table.Version + 1 });

    public Task<LoadResult<SellingItem>> GetSellingAsync(Guid pageBusiness, CancellationToken cancellationToken = default) =>
        Task.FromResult<LoadResult<SellingItem>>(new LoadResult<SellingItem>.Loaded(Selling));

    private static string Tips(SellingItem selling) =>
        selling.TipsEnabled ? $" tips {string.Join("/", selling.TipPresets)}" : selling.TipPresets.Count > 0 ? " tips off" : string.Empty;

    public Task<OwnerActionResult> UpdateSellingAsync(Guid pageBusiness, SellingItem changed, CancellationToken cancellationToken = default) =>
        Change($"selling {changed.DiscountLimitPercent}% {changed.DiscountLimitAmount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}{Tips(changed)} v{changed.Version}",
            () => Selling = changed with { Version = changed.Version + 1 });

    /// <summary>The whole history, newest first; served in pages of <c>History.PageSize</c>.</summary>
    public List<SettingsChangeItem> History { get; } = [];

    /// <summary>What the page asked for: "entityId-or-all page".</summary>
    public List<string> HistoryRequests { get; } = [];

    public LoadResult<IReadOnlyList<SettingsChangeItem>>? NextHistory { get; set; }

    public Task<LoadResult<IReadOnlyList<SettingsChangeItem>>> ListChangesAsync(Guid pageBusiness, Guid? entityId, int page, CancellationToken cancellationToken = default)
    {
        HistoryRequests.Add($"{entityId?.ToString() ?? "all"} {page}");
        return Task.FromResult(NextHistory ?? new LoadResult<IReadOnlyList<SettingsChangeItem>>.Loaded(History
            .Where(c => entityId is null || c.EntityId == entityId)
            .Skip((page - 1) * HelloPos.Web.Pages.Settings.History.PageSize).Take(HelloPos.Web.Pages.Settings.History.PageSize).ToList()));
    }

    private Task<OwnerActionResult> Change(string call, Action apply)
    {
        if (NextChange is OwnerActionResult.Succeeded)
        {
            Calls.Add(call);
            apply();
        }

        return Task.FromResult(NextChange);
    }

    private string AreaName(Guid areaId) => Areas.Single(a => a.Id == areaId).Name;

    private void Replace(PaymentTypeItem old, PaymentTypeItem updated) => Types[Types.FindIndex(t => t.Id == old.Id)] = updated with { Version = old.Version + 1 };
}
