using System.Globalization;
using System.Text.Json;
using HelloPos.Api.Features.Owners;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Kernel.Pricing;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HelloPos.Api.Features.Catalog;

/// <summary>Problems, history, dates and money shared by the catalog slices (feature #11).</summary>
public static class CatalogSupport
{
    public const string Tag = "Catalog";
    public const string CatalogChangedType = "urn:hellopos:problem:catalog-changed";
    public const string NotEditableType = "urn:hellopos:problem:not-editable";

    public static ProblemHttpResult CatalogChanged() => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict, type: CatalogChangedType,
        title: "Someone else changed this in the meantime. Reload and try again.");

    public static ProblemHttpResult NotEditable(string title) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict, type: NotEditableType, title: title);

    public static ProblemHttpResult NotFound() => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound);

    public static ValidationProblem Invalid(string field, string message) =>
        OwnerEndpoints.Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    public static ValidationProblem Invalid(Dictionary<string, List<string>> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return OwnerEndpoints.Invalid(errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal));
    }

    public static void Add(this Dictionary<string, List<string>> errors, string field, string? message)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (message is null)
        {
            return;
        }

        if (!errors.TryGetValue(field, out var list))
        {
            errors[field] = list = [];
        }

        list.Add(message);
    }

    /// <summary>Lower-case trimmed form used for case-insensitive uniqueness.</summary>
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Trim().ToLowerInvariant();
    }

    /// <summary>Adds a history entry to the unit of work (saved with the change it records; FR-015).</summary>
    public static void RecordChange(
        this HelloPosDbContext db, TimeProvider clock, Guid actorStaffId, string entity, Guid entityId, string action,
        IReadOnlyDictionary<string, object?[]>? changes = null, Guid? importId = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        db.CatalogChanges.Add(new CatalogChange
        {
            At = clock.GetUtcNow(),
            ActorStaffId = actorStaffId,
            Entity = entity,
            EntityId = entityId,
            Action = action,
            Changes = JsonSerializer.Serialize(changes ?? new Dictionary<string, object?[]>()),
            ImportId = importId,
        });
    }

    /// <summary>Adds <paramref name="field"/> to <paramref name="changes"/> when the value differs.</summary>
    public static void Diff<T>(this Dictionary<string, object?[]> changes, string field, T before, T after)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (!EqualityComparer<T>.Default.Equals(before, after))
        {
            changes[field] = [before, after];
        }
    }

    /// <summary>
    /// The open business's settings. Selected by id: on the web an owner may also read their other
    /// businesses (feature 006 owner-read policy), so "the only visible row" is not enough.
    /// </summary>
    public static Task<Businesses.Business> BusinessAsync(HelloPosDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var id = db.CurrentBusinessId ?? throw new InvalidOperationException("No business is open.");
        return db.Businesses.AsNoTracking().SingleAsync(b => b.Id == id, cancellationToken);
    }

    /// <summary>Today's date in the business's time zone.</summary>
    public static DateOnly Today(Businesses.Business business, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(business);
        ArgumentNullException.ThrowIfNull(clock);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZone);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
    }

    /// <summary>The calendar date of <paramref name="instant"/> in the business's time zone.</summary>
    public static DateOnly LocalDate(Businesses.Business business, DateTimeOffset instant)
    {
        ArgumentNullException.ThrowIfNull(business);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZone);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
    }

    /// <summary>Currency units to minor units (exact for amounts the currency can hold).</summary>
    public static long ToMinor(decimal amount, string currency) =>
        decimal.ToInt64(amount * Pow10(Currencies.DigitsOf(currency)));

    public static decimal FromMinor(long minor, string currency) => minor / Pow10(Currencies.DigitsOf(currency));

    /// <summary>Invariant decimal string without trailing zeros ("2.5", "13", "0.005").</summary>
    public static string Format(decimal value) => value.ToString("0.############", CultureInfo.InvariantCulture);

    /// <summary>Parses an invariant decimal string ("2.5"); no thousands separators, no exponent.</summary>
    public static decimal? ParseDecimal(string? value) =>
        value is not null && decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    /// <summary>True when the save failed on a unique index (a racing duplicate).</summary>
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception?.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static decimal Pow10(int digits) => digits switch
    {
        0 => 1m,
        1 => 10m,
        2 => 100m,
        3 => 1000m,
        _ => (decimal)Math.Pow(10, digits),
    };
}

/// <summary>History entity names (contract <c>CatalogChange.entity</c>).</summary>
public static class CatalogEntityNames
{
    public const string TaxProfile = "taxProfile";
    public const string TaxProfileVersion = "taxProfileVersion";
    public const string Category = "category";
    public const string Product = "product";
    public const string ItemDiscount = "itemDiscount";
    public const string Business = "business";
}

/// <summary>History actions (contract <c>CatalogChange.action</c>).</summary>
public static class CatalogActions
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Deactivated = "deactivated";
    public const string Reactivated = "reactivated";
    public const string Deleted = "deleted";
    public const string Imported = "imported";
}

public sealed record CatalogVersionRequest(int Version);
