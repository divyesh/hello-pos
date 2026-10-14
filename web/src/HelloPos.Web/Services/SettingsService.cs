using HelloPos.Web.ApiClient.Models;
using Microsoft.Kiota.Abstractions;

namespace HelloPos.Web.Services;

/// <summary>A payment type; <see cref="Kind"/> is the wire name (cash, externalCard, digitalWallet, cheque, voucher, other).</summary>
public sealed record PaymentTypeItem(
    Guid Id, string Name, string Kind, bool BuiltIn, bool OpensDrawer, bool AsksReference, bool GivesChange, int Position, bool Active, int Version);

/// <summary>Selling settings; percentages as decimal strings, the amount limit in minor units of <see cref="Currency"/>.</summary>
public sealed record SellingItem(
    bool UsesTables, string DiscountLimitPercent, long? DiscountLimitAmount, string Currency, bool TipsEnabled, IReadOnlyList<string> TipPresets, int Version);

public interface ISettingsService
{
    Task<LoadResult<IReadOnlyList<PaymentTypeItem>>> ListPaymentTypesAsync(Guid pageBusiness, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> CreatePaymentTypeAsync(Guid pageBusiness, string name, string kind, bool opensDrawer, bool asksReference, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> UpdatePaymentTypeAsync(Guid pageBusiness, PaymentTypeItem type, string name, string kind, bool opensDrawer, bool asksReference, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> SetPaymentTypeActiveAsync(Guid pageBusiness, PaymentTypeItem type, bool active, CancellationToken cancellationToken = default);

    /// <summary>A new order: every payment type of the business exactly once.</summary>
    Task<OwnerActionResult> ReorderPaymentTypesAsync(Guid pageBusiness, IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);

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
