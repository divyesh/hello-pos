using System.Globalization;
using System.Text.Json.Serialization;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using HelloPos.Kernel.Catalog;
using HelloPos.Kernel.Staff;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HelloPos.Api.Features.Catalog.Discounts;

public sealed record ItemDiscountRequest(string? Kind, string? Value, DateOnly? FromDate, DateOnly? ToDate);

/// <summary>Contract <c>ItemDiscount</c>: value is a percent, or a fixed amount per unit in minor units.</summary>
public sealed record ItemDiscountResponse(
    Guid Id,
    string Kind,
    string Value,
    DateOnly FromDate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? ToDate,
    string Status);

/// <summary>
/// Scheduled item discounts (feature #11, US3, FR-007, research R6). Periods of one product never
/// overlap: checked under a lock on the product row, with a database exclusion constraint behind it.
/// Future discounts may change or go; a running one may only end early; ended ones are history.
/// </summary>
public sealed class ItemDiscountEndpoints : IEndpointMapper
{
    public const string Future = "future";
    public const string Running = "running";
    public const string Ended = "ended";

    public void Map(IEndpointRouteBuilder app)
    {
        var discounts = app.MapGroup("/catalog/products/{productId:guid}/discounts")
            .WithTags(CatalogSupport.Tag)
            .RequireRole(StaffRole.Owner, StaffRole.Manager)
            .RequireSameBusiness();
        discounts.MapGet("", ListAsync).WithName("listItemDiscounts");
        discounts.MapPost("", CreateAsync).WithName("createItemDiscount").RequireClientHeader();
        discounts.MapPut("/{discountId:guid}", UpdateAsync).WithName("updateItemDiscount").RequireClientHeader();
        discounts.MapDelete("/{discountId:guid}", DeleteAsync).WithName("deleteItemDiscount").RequireClientHeader();
    }

    public static async Task<Results<Ok<List<ItemDiscountResponse>>, ProblemHttpResult>> ListAsync(
        Guid productId, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (!await db.Products.AnyAsync(p => p.Id == productId, cancellationToken).ConfigureAwait(false))
        {
            return CatalogSupport.NotFound();
        }

        var business = await CatalogSupport.BusinessAsync(db, cancellationToken).ConfigureAwait(false);
        var today = CatalogSupport.Today(business, clock);
        var discounts = await db.ItemDiscounts.AsNoTracking().Where(d => d.ProductId == productId)
            .OrderByDescending(d => d.FromDate).ToListAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(discounts.Select(d => ToResponse(d, today, business.CurrencyCode)).ToList());
    }

    public static async Task<Results<Created<ItemDiscountResponse>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        Guid productId, ItemDiscountRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (!await LockProductAsync(db, productId, cancellationToken).ConfigureAwait(false))
        {
            return CatalogSupport.NotFound();
        }

        var business = await CatalogSupport.BusinessAsync(db, cancellationToken).ConfigureAwait(false);
        var today = CatalogSupport.Today(business, clock);
        var (values, errors) = await ValidateAsync(db, productId, request, business.CurrencyCode, today, null, cancellationToken).ConfigureAwait(false);
        if (values is null)
        {
            return CatalogSupport.Invalid(errors);
        }

        var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
        var discount = new ItemDiscount
        {
            ProductId = productId,
            Kind = values.Kind,
            Value = values.Value,
            FromDate = values.From,
            ToDate = values.To,
            CreatedAt = clock.GetUtcNow(),
        };
        db.ItemDiscounts.Add(discount);
        db.RecordChange(clock, actor.Id, CatalogEntityNames.ItemDiscount, discount.Id, CatalogActions.Created, Describe(null, discount, business.CurrencyCode));
        if (await SaveAsync(db, cancellationToken).ConfigureAwait(false) is { } overlap)
        {
            return overlap;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"/catalog/products/{productId}/discounts/{discount.Id}", ToResponse(discount, today, business.CurrencyCode));
    }

    public static async Task<Results<Ok<ItemDiscountResponse>, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        Guid productId, Guid discountId, ItemDiscountRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (!await LockProductAsync(db, productId, cancellationToken).ConfigureAwait(false))
        {
            return CatalogSupport.NotFound();
        }

        var discount = await db.ItemDiscounts.SingleOrDefaultAsync(d => d.Id == discountId && d.ProductId == productId, cancellationToken).ConfigureAwait(false);
        if (discount is null)
        {
            return CatalogSupport.NotFound();
        }

        var business = await CatalogSupport.BusinessAsync(db, cancellationToken).ConfigureAwait(false);
        var today = CatalogSupport.Today(business, clock);
        var status = StatusOf(discount, today);
        if (status == Ended)
        {
            return CatalogSupport.NotEditable("This discount has ended; it is kept as history and cannot change.");
        }

        Values? values;
        if (status == Running)
        {
            values = EndEarly(discount, request, business.CurrencyCode, today);
            if (values is null)
            {
                return CatalogSupport.NotEditable($"A running discount can only end early: keep its amount and start, and choose an end date from {today:yyyy-MM-dd} on.");
            }
        }
        else
        {
            var (validated, errors) = await ValidateAsync(db, productId, request, business.CurrencyCode, today, discount.Id, cancellationToken).ConfigureAwait(false);
            if (validated is null)
            {
                return CatalogSupport.Invalid(errors);
            }

            values = validated;
        }

        var before = new ItemDiscount { ProductId = productId, Kind = discount.Kind, Value = discount.Value, FromDate = discount.FromDate, ToDate = discount.ToDate };
        (discount.Kind, discount.Value, discount.FromDate, discount.ToDate) = (values.Kind, values.Value, values.From, values.To);
        var changes = Describe(before, discount, business.CurrencyCode);
        if (changes.Count > 0)
        {
            var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
            db.RecordChange(clock, actor.Id, CatalogEntityNames.ItemDiscount, discount.Id, CatalogActions.Updated, changes);
            if (await SaveAsync(db, cancellationToken).ConfigureAwait(false) is { } overlap)
            {
                return overlap;
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ToResponse(discount, today, business.CurrencyCode));
    }

    public static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> DeleteAsync(
        Guid productId, Guid discountId, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        var discount = await db.ItemDiscounts.SingleOrDefaultAsync(d => d.Id == discountId && d.ProductId == productId, cancellationToken).ConfigureAwait(false);
        if (discount is null)
        {
            return CatalogSupport.NotFound();
        }

        var business = await CatalogSupport.BusinessAsync(db, cancellationToken).ConfigureAwait(false);
        if (StatusOf(discount, CatalogSupport.Today(business, clock)) != Future)
        {
            return CatalogSupport.NotEditable("Only a discount that has not started can be removed; end a running one early instead.");
        }

        var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
        db.RecordChange(clock, actor.Id, CatalogEntityNames.ItemDiscount, discount.Id, CatalogActions.Deleted, Describe(discount, null, business.CurrencyCode));
        db.ItemDiscounts.Remove(discount);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    internal sealed record Values(string Kind, decimal Value, DateOnly From, DateOnly? To);

    internal static string StatusOf(ItemDiscount discount, DateOnly today) =>
        discount.FromDate > today ? Future : discount.ToDate is { } end && end < today ? Ended : Running;

    /// <summary>Field rules, then the no-overlap rule (the product row is locked by the caller).</summary>
    private static async Task<(Values? Values, Dictionary<string, List<string>> Errors)> ValidateAsync(
        HelloPosDbContext db, Guid productId, ItemDiscountRequest request, string currency, DateOnly today, Guid? except, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var percent = request.Kind == "percent";
        if (request.Kind is not ("percent" or "fixed"))
        {
            errors.Add("kind", "Choose a percentage or a fixed amount per unit.");
        }

        var value = CatalogSupport.ParseDecimal(request.Value);
        if (value is null)
        {
            errors.Add("value", "Enter the discount as a number.");
        }
        else if (request.Kind is "percent" or "fixed")
        {
            errors.Add("value", CatalogRules.ValidateDiscount(percent, value.Value));
        }

        if (request.FromDate is not { } from)
        {
            errors.Add("fromDate", "Choose the first day.");
            return (null, errors);
        }

        if (from < today)
        {
            errors.Add("fromDate", "Choose today or a later day.");
        }

        errors.Add("toDate", CatalogRules.ValidateDiscountPeriod(from, request.ToDate));
        if (errors.Count == 0)
        {
            var to = request.ToDate;
            var other = await db.ItemDiscounts.AsNoTracking()
                .Where(d => d.ProductId == productId && d.Id != (except ?? Guid.Empty)
                    && (to == null || d.FromDate <= to) && (d.ToDate == null || d.ToDate >= from))
                .OrderBy(d => d.FromDate).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (other is not null)
            {
                errors.Add("fromDate", $"This overlaps the discount from {other.FromDate:yyyy-MM-dd}{(other.ToDate is { } end ? $" to {end:yyyy-MM-dd}" : " (no end date)")}.");
            }
        }

        return errors.Count > 0
            ? (null, errors)
            : (new Values(request.Kind!, percent ? value!.Value : CatalogSupport.FromMinor(decimal.ToInt64(value!.Value), currency), from, request.ToDate), errors);
    }

    /// <summary>The only change allowed while running: an earlier end, today or later. Null when the request asks for more.</summary>
    private static Values? EndEarly(ItemDiscount discount, ItemDiscountRequest request, string currency, DateOnly today)
    {
        var current = discount.Kind == "percent" ? discount.Value : CatalogSupport.ToMinor(discount.Value, currency);
        var sameTerms = request.Kind == discount.Kind && CatalogSupport.ParseDecimal(request.Value) == current && request.FromDate == discount.FromDate;
        var earlier = request.ToDate is { } end && end >= today && (discount.ToDate is null || end <= discount.ToDate);
        var unchanged = request.ToDate == discount.ToDate;
        return sameTerms && (earlier || unchanged) ? new Values(discount.Kind, discount.Value, discount.FromDate, request.ToDate) : null;
    }

    private static async Task<bool> LockProductAsync(HelloPosDbContext db, Guid productId, CancellationToken cancellationToken)
    {
        var locked = await db.Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM pos.products WHERE id = {productId} FOR UPDATE")
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return locked.Count == 1;
    }

    /// <summary>Saves; an exclusion-constraint hit (a racing overlap) becomes the same field error.</summary>
    private static async Task<ValidationProblem?> SaveAsync(HelloPosDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation })
        {
            return CatalogSupport.Invalid("fromDate", "This overlaps another discount of this product.");
        }
    }

    private static Dictionary<string, object?[]> Describe(ItemDiscount? before, ItemDiscount? after, string currency)
    {
        string? Value(ItemDiscount? d) => d is null ? null : d.Kind == "percent" ? CatalogSupport.Format(d.Value) : CatalogSupport.ToMinor(d.Value, currency).ToString(CultureInfo.InvariantCulture);
        var changes = new Dictionary<string, object?[]>();
        changes.Diff("kind", before?.Kind, after?.Kind);
        changes.Diff("value", Value(before), Value(after));
        changes.Diff("fromDate", before?.FromDate, after?.FromDate);
        changes.Diff("toDate", before?.ToDate, after?.ToDate);
        return changes;
    }

    private static ItemDiscountResponse ToResponse(ItemDiscount discount, DateOnly today, string currency) => new(
        discount.Id,
        discount.Kind,
        discount.Kind == "percent" ? CatalogSupport.Format(discount.Value) : CatalogSupport.ToMinor(discount.Value, currency).ToString(CultureInfo.InvariantCulture),
        discount.FromDate,
        discount.ToDate,
        StatusOf(discount, today));
}
