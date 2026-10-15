using System.Text.Json.Serialization;
using HelloPos.Api.Features.Catalog;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using HelloPos.Kernel.Settings;
using HelloPos.Kernel.Staff;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Settings.Selling;

public sealed record SellingSettingsRequest(
    bool UsesTables, string? DiscountLimitPercent, long? DiscountLimitAmount, bool TipsEnabled, List<string>? TipPresets, int Version);

public sealed record SellingSettingsResponse(
    bool UsesTables, string DiscountLimitPercent,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? DiscountLimitAmount, string Currency, bool TipsEnabled, List<string> TipPresets, int Version);

/// <summary>
/// The tables switch, the discount approval limit and tips (feature #12, US2–US4, clarification Q2):
/// owners and managers read them; owners change them all, managers only the tables switch.
/// </summary>
public sealed class SellingEndpoints : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder app)
    {
        var selling = app.MapGroup("/settings/selling")
            .WithTags(SettingsSupport.Tag)
            .RequireRole(StaffRole.Owner, StaffRole.Manager)
            .RequireSameBusiness();
        selling.MapGet("", GetAsync).WithName("getSellingSettings");
        selling.MapPut("", UpdateAsync).WithName("updateSellingSettings").RequireClientHeader();
    }

    public static async Task<Ok<SellingSettingsResponse>> GetAsync(HelloPosDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var business = await CatalogSupport.BusinessAsync(db, cancellationToken).ConfigureAwait(false);
        var selling = await SettingsSupport.SellingAsync(db, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ToResponse(selling, business.CurrencyCode));
    }

    public static async Task<IResult> UpdateAsync(
        SellingSettingsRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        var business = await CatalogSupport.BusinessAsync(db, cancellationToken).ConfigureAwait(false);
        var currency = business.CurrencyCode;
        var selling = await SettingsSupport.SellingAsync(db, cancellationToken).ConfigureAwait(false);

        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var percent = CatalogSupport.ParseDecimal(request.DiscountLimitPercent);
        errors.Add("discountLimitPercent", percent is { } p ? SettingsRules.ValidateLimitPercent(p) : "Enter the limit as a percentage.");
        errors.Add("discountLimitAmount", SettingsRules.ValidateLimitAmount(request.DiscountLimitAmount));
        var presets = (request.TipPresets ?? []).Select(CatalogSupport.ParseDecimal).ToList();
        errors.Add("tipPresets", presets.Any(t => t is null)
            ? "Enter each tip as a percentage."
            : SettingsRules.ValidateTips(request.TipsEnabled, presets.Select(t => t!.Value).ToList()));
        if (errors.Count > 0)
        {
            return CatalogSupport.Invalid(errors);
        }

        var amount = request.DiscountLimitAmount is { } minor ? CatalogSupport.FromMinor(minor, currency) : (decimal?)null;
        var tips = presets.Select(t => t!.Value).Order().ToList();
        var changes = new Dictionary<string, object?[]>();
        changes.Diff("usesTables", selling.UsesTables, request.UsesTables);
        changes.Diff("discountLimitPercent", CatalogSupport.Format(selling.DiscountLimitPercent), CatalogSupport.Format(percent!.Value));
        changes.Diff("discountLimitAmount", Minor(selling.DiscountLimitAmount, currency), request.DiscountLimitAmount);
        changes.Diff("tipsEnabled", selling.TipsEnabled, request.TipsEnabled);
        changes.Diff("tipPresets", Presets(selling.TipPresets), Presets(tips));

        var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
        if (actor.Role != StaffRole.Owner && changes.Keys.Any(field => field != "usesTables"))
        {
            return SettingsSupport.Forbidden("Only an owner can change the discount limit and tips.");
        }

        if (selling.Version != request.Version)
        {
            return SettingsSupport.SettingsChanged();
        }

        if (changes.Count > 0)
        {
            selling.UsesTables = request.UsesTables;
            selling.DiscountLimitPercent = percent.Value;
            selling.DiscountLimitAmount = amount;
            selling.TipsEnabled = request.TipsEnabled;
            selling.TipPresets = tips;
            selling.Version++;
            selling.UpdatedAt = clock.GetUtcNow();
            db.RecordSettingsChange(clock, actor.Id, SettingsEntityNames.Selling, selling.Id, SettingsActions.Updated, changes);
        }

        return await SettingsSupport.SaveAsync(db, "version", "", cancellationToken).ConfigureAwait(false)
            ?? TypedResults.Ok(ToResponse(selling, currency));
    }

    public static SellingSettingsResponse ToResponse(SellingSettings selling, string currency)
    {
        ArgumentNullException.ThrowIfNull(selling);
        return new SellingSettingsResponse(selling.UsesTables, CatalogSupport.Format(selling.DiscountLimitPercent),
            Minor(selling.DiscountLimitAmount, currency), currency, selling.TipsEnabled,
            selling.TipPresets.Order().Select(CatalogSupport.Format).ToList(), selling.Version);
    }

    private static long? Minor(decimal? amount, string currency) => amount is { } a ? CatalogSupport.ToMinor(a, currency) : null;

    private static string Presets(IEnumerable<decimal> presets) => string.Join(", ", presets.Order().Select(CatalogSupport.Format));
}
