using HelloPos.Web.ApiClient.Models;
using Microsoft.Kiota.Abstractions;

namespace HelloPos.Web.Services;

/// <summary>A payment type; <see cref="Kind"/> is the wire name (cash, externalCard, digitalWallet, cheque, voucher, other).</summary>
public sealed record PaymentTypeItem(
    Guid Id, string Name, string Kind, bool BuiltIn, bool OpensDrawer, bool AsksReference, bool GivesChange, int Position, bool Active, int Version);

/// <summary>Selling settings; percentages as decimal strings, the amount limit in minor units of <see cref="Currency"/>.</summary>
public sealed record SellingItem(
    bool UsesTables, string DiscountLimitPercent, long? DiscountLimitAmount, string Currency, bool TipsEnabled, IReadOnlyList<string> TipPresets, int Version);

/// <summary>A table area with its tables, in order.</summary>
public sealed record TableAreaItem(Guid Id, string Name, int Position, bool Active, int Version, IReadOnlyList<DiningTableItem> Tables);

public sealed record DiningTableItem(Guid Id, Guid AreaId, string Name, int Seats, int Position, bool Active, int Version);

public interface ISettingsService
{
    Task<LoadResult<IReadOnlyList<PaymentTypeItem>>> ListPaymentTypesAsync(Guid pageBusiness, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> CreatePaymentTypeAsync(Guid pageBusiness, string name, string kind, bool opensDrawer, bool asksReference, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> UpdatePaymentTypeAsync(Guid pageBusiness, PaymentTypeItem type, string name, string kind, bool opensDrawer, bool asksReference, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> SetPaymentTypeActiveAsync(Guid pageBusiness, PaymentTypeItem type, bool active, CancellationToken cancellationToken = default);

    /// <summary>A new order: every payment type of the business exactly once.</summary>
    Task<OwnerActionResult> ReorderPaymentTypesAsync(Guid pageBusiness, IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);

    Task<LoadResult<IReadOnlyList<TableAreaItem>>> ListAreasAsync(Guid pageBusiness, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> CreateAreaAsync(Guid pageBusiness, string name, CancellationToken cancellationToken = default);

    /// <summary>Renames <paramref name="area"/> and moves it to <paramref name="position"/> among the areas.</summary>
    Task<OwnerActionResult> UpdateAreaAsync(Guid pageBusiness, TableAreaItem area, string name, int position, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> SetAreaActiveAsync(Guid pageBusiness, TableAreaItem area, bool active, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> CreateTableAsync(Guid pageBusiness, Guid areaId, string name, int seats, CancellationToken cancellationToken = default);

    /// <summary>Tables named prefix + n + suffix for n from <paramref name="first"/> to <paramref name="last"/>, all or nothing.</summary>
    Task<OwnerActionResult> CreateTablesAsync(Guid pageBusiness, Guid areaId, int first, int last, string prefix, string suffix, int seats, CancellationToken cancellationToken = default);

    /// <summary>Renames, resizes and moves <paramref name="table"/> to <paramref name="position"/> in <paramref name="areaId"/>.</summary>
    Task<OwnerActionResult> UpdateTableAsync(Guid pageBusiness, DiningTableItem table, Guid areaId, string name, int seats, int position, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> SetTableActiveAsync(Guid pageBusiness, DiningTableItem table, bool active, CancellationToken cancellationToken = default);

    Task<LoadResult<SellingItem>> GetSellingAsync(Guid pageBusiness, CancellationToken cancellationToken = default);

    /// <summary>Saves <paramref name="changed"/>; its <see cref="SellingItem.Version"/> is the one that was read.</summary>
    Task<OwnerActionResult> UpdateSellingAsync(Guid pageBusiness, SellingItem changed, CancellationToken cancellationToken = default);
}

/// <summary>Business settings calls (feature #12) through the client generated from the contract.</summary>
public sealed class SettingsService(HttpClient http, ILogger<SettingsService> logger) : ISettingsService
{
    /// <summary>Kinds an owner may choose, with their labels (cash belongs to the built-in Cash only).</summary>
    public static readonly IReadOnlyList<(string Kind, string Label)> Kinds =
    [
        ("externalCard", "Card on an external terminal"),
        ("digitalWallet", "UPI or digital wallet"),
        ("cheque", "Cheque"),
        ("voucher", "Voucher"),
        ("other", "Other"),
    ];

    public static string KindLabel(string kind) => kind == "cash" ? "Cash" : Kinds.FirstOrDefault(k => k.Kind == kind).Label ?? kind;

    public Task<LoadResult<IReadOnlyList<PaymentTypeItem>>> ListPaymentTypesAsync(Guid pageBusiness, CancellationToken cancellationToken = default) =>
        StaffService.LoadAsync<IReadOnlyList<PaymentTypeItem>>(http, logger, "list-payment-types", async () =>
            (await ApiClients.Create(http).Settings.PaymentTypes.GetAsync(c => Page(c, pageBusiness), cancellationToken).ConfigureAwait(false) ?? [])
            .Select(ToItem).ToList());

    public Task<OwnerActionResult> CreatePaymentTypeAsync(Guid pageBusiness, string name, string kind, bool opensDrawer, bool asksReference, CancellationToken cancellationToken = default) =>
        Run("create-payment-type", () => ApiClients.Create(http).Settings.PaymentTypes.PostAsync(
            new PaymentTypeInput { Name = name, Kind = Enum.Parse<PaymentTypeInput_kind>(kind, ignoreCase: true), OpensDrawer = opensDrawer, AsksReference = asksReference },
            c => Page(c, pageBusiness), cancellationToken));

    public Task<OwnerActionResult> UpdatePaymentTypeAsync(Guid pageBusiness, PaymentTypeItem type, string name, string kind, bool opensDrawer, bool asksReference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Run("update-payment-type", () => ApiClients.Create(http).Settings.PaymentTypes[type.Id].PutAsync(
            new PaymentTypeUpdate
            {
                Name = name, Kind = Enum.Parse<PaymentKind>(kind, ignoreCase: true), OpensDrawer = opensDrawer, AsksReference = asksReference, Version = type.Version,
            },
            c => Page(c, pageBusiness), cancellationToken));
    }

    public Task<OwnerActionResult> SetPaymentTypeActiveAsync(Guid pageBusiness, PaymentTypeItem type, bool active, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        var body = new SettingsVersion { Version = type.Version };
        var item = ApiClients.Create(http).Settings.PaymentTypes[type.Id];
        return active
            ? Run("reactivate-payment-type", () => item.Reactivate.PostAsync(body, c => Page(c, pageBusiness), cancellationToken))
            : Run("deactivate-payment-type", () => item.Deactivate.PostAsync(body, c => Page(c, pageBusiness), cancellationToken));
    }

    public Task<OwnerActionResult> ReorderPaymentTypesAsync(Guid pageBusiness, IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) =>
        Run("reorder-payment-types", () => ApiClients.Create(http).Settings.PaymentTypes.Order.PutAsync(
            new PaymentTypeOrder { Ids = ids.Select(id => (Guid?)id).ToList() }, c => Page(c, pageBusiness), cancellationToken));

    public Task<LoadResult<IReadOnlyList<TableAreaItem>>> ListAreasAsync(Guid pageBusiness, CancellationToken cancellationToken = default) =>
        StaffService.LoadAsync<IReadOnlyList<TableAreaItem>>(http, logger, "list-table-areas", async () =>
            (await ApiClients.Create(http).Settings.Tables.Areas.GetAsync(c => Page(c, pageBusiness), cancellationToken).ConfigureAwait(false) ?? [])
            .Select(ToItem).ToList());

    public Task<OwnerActionResult> CreateAreaAsync(Guid pageBusiness, string name, CancellationToken cancellationToken = default) =>
        Run("create-table-area", () => ApiClients.Create(http).Settings.Tables.Areas.PostAsync(
            new TableAreaInput { Name = name }, c => Page(c, pageBusiness), cancellationToken));

    public Task<OwnerActionResult> UpdateAreaAsync(Guid pageBusiness, TableAreaItem area, string name, int position, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(area);
        return Run("update-table-area", () => ApiClients.Create(http).Settings.Tables.Areas[area.Id].PutAsync(
            new TableAreaUpdate { Name = name, Position = position, Version = area.Version }, c => Page(c, pageBusiness), cancellationToken));
    }

    public Task<OwnerActionResult> SetAreaActiveAsync(Guid pageBusiness, TableAreaItem area, bool active, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(area);
        var body = new SettingsVersion { Version = area.Version };
        var item = ApiClients.Create(http).Settings.Tables.Areas[area.Id];
        return active
            ? Run("reactivate-table-area", () => item.Reactivate.PostAsync(body, c => Page(c, pageBusiness), cancellationToken))
            : Run("deactivate-table-area", () => item.Deactivate.PostAsync(body, c => Page(c, pageBusiness), cancellationToken));
    }

    public Task<OwnerActionResult> CreateTableAsync(Guid pageBusiness, Guid areaId, string name, int seats, CancellationToken cancellationToken = default) =>
        Run("create-table", () => ApiClients.Create(http).Settings.Tables.PostAsync(
            new DiningTableInput { AreaId = areaId, Name = name, Seats = seats }, c => Page(c, pageBusiness), cancellationToken));

    public Task<OwnerActionResult> CreateTablesAsync(
        Guid pageBusiness, Guid areaId, int first, int last, string prefix, string suffix, int seats, CancellationToken cancellationToken = default) =>
        Run("create-tables", () => ApiClients.Create(http).Settings.Tables.Bulk.PostAsync(
            new TableRangeInput
            {
                AreaId = areaId,
                From = first,
                To = last,
                Prefix = string.IsNullOrEmpty(prefix) ? null : prefix,
                Suffix = string.IsNullOrEmpty(suffix) ? null : suffix,
                Seats = seats,
            },
            c => Page(c, pageBusiness), cancellationToken));

    public Task<OwnerActionResult> UpdateTableAsync(
        Guid pageBusiness, DiningTableItem table, Guid areaId, string name, int seats, int position, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        return Run("update-table", () => ApiClients.Create(http).Settings.Tables[table.Id].PutAsync(
            new DiningTableUpdate { AreaId = areaId, Name = name, Seats = seats, Position = position, Version = table.Version },
            c => Page(c, pageBusiness), cancellationToken));
    }

    public Task<OwnerActionResult> SetTableActiveAsync(Guid pageBusiness, DiningTableItem table, bool active, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        var body = new SettingsVersion { Version = table.Version };
        var item = ApiClients.Create(http).Settings.Tables[table.Id];
        return active
            ? Run("reactivate-table", () => item.Reactivate.PostAsync(body, c => Page(c, pageBusiness), cancellationToken))
            : Run("deactivate-table", () => item.Deactivate.PostAsync(body, c => Page(c, pageBusiness), cancellationToken));
    }

    public Task<LoadResult<SellingItem>> GetSellingAsync(Guid pageBusiness, CancellationToken cancellationToken = default) =>
        StaffService.LoadAsync(http, logger, "get-selling", async () =>
            ToItem((await ApiClients.Create(http).Settings.Selling.GetAsync(c => Page(c, pageBusiness), cancellationToken).ConfigureAwait(false))!));

    public Task<OwnerActionResult> UpdateSellingAsync(Guid pageBusiness, SellingItem changed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changed);
        return Run("update-selling", () => ApiClients.Create(http).Settings.Selling.PutAsync(
            new SellingSettingsUpdate
            {
                UsesTables = changed.UsesTables,
                DiscountLimitPercent = changed.DiscountLimitPercent,
                DiscountLimitAmount = changed.DiscountLimitAmount,
                TipsEnabled = changed.TipsEnabled,
                TipPresets = changed.TipPresets.ToList(),
                Version = changed.Version,
            },
            c => Page(c, pageBusiness), cancellationToken));
    }

    private static PaymentTypeItem ToItem(PaymentType type) => new(
        type.Id ?? Guid.Empty,
        type.Name ?? string.Empty,
        Wire(type.Kind),
        type.BuiltIn ?? false,
        type.OpensDrawer ?? false,
        type.AsksReference ?? false,
        type.GivesChange ?? false,
        type.Position ?? 0,
        type.Active ?? false,
        type.Version ?? 0);

    private static TableAreaItem ToItem(TableArea area) => new(
        area.Id ?? Guid.Empty,
        area.Name ?? string.Empty,
        area.Position ?? 0,
        area.Active ?? false,
        area.Version ?? 0,
        (area.Tables ?? []).Select(ToItem).ToList());

    private static DiningTableItem ToItem(DiningTable table) => new(
        table.Id ?? Guid.Empty,
        table.AreaId ?? Guid.Empty,
        table.Name ?? string.Empty,
        table.Seats ?? 0,
        table.Position ?? 0,
        table.Active ?? false,
        table.Version ?? 0);

    private static SellingItem ToItem(SellingSettings selling) => new(
        selling.UsesTables ?? false,
        selling.DiscountLimitPercent ?? "0",
        selling.DiscountLimitAmount,
        selling.Currency ?? string.Empty,
        selling.TipsEnabled ?? false,
        selling.TipPresets ?? [],
        selling.Version ?? 0);

    /// <summary>Enum member to wire name ("DigitalWallet" to "digitalWallet").</summary>
    private static string Wire(PaymentKind? kind)
    {
        var name = (kind ?? PaymentKind.Other).ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private Task<OwnerActionResult> Run(string operation, Func<Task> call) => OwnerAccountService.RunAsync(http, logger, operation, call);

    private static void Page<T>(RequestConfiguration<T> configuration, Guid pageBusiness)
        where T : class, new() =>
        configuration.Headers.Add(BusinessService.BusinessHeader, pageBusiness.ToString());
}
