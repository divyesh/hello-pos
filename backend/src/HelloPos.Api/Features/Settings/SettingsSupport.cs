using System.Text.Json;
using HelloPos.Api.Features.Catalog;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Kernel.Settings;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Settings;

/// <summary>Problems, history and defaults shared by the settings slices (feature #12).</summary>
public static class SettingsSupport
{
    public const string Tag = "Settings";
    public const string SettingsChangedType = "urn:hellopos:problem:settings-changed";
    public const string NotAllowedType = "urn:hellopos:problem:not-editable";

    public static ProblemHttpResult SettingsChanged() => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict, type: SettingsChangedType,
        title: "Someone else changed this in the meantime. Reload and try again.");

    public static ProblemHttpResult NotAllowed(string title) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict, type: NotAllowedType, title: title);

    public static ProblemHttpResult Forbidden(string title) => TypedResults.Problem(
        statusCode: StatusCodes.Status403Forbidden, title: title);

    /// <summary>Adds a history entry to the unit of work (saved with the change it records; FR-016).</summary>
    public static void RecordSettingsChange(
        this HelloPosDbContext db, TimeProvider clock, Guid actorStaffId, string entity, Guid entityId, string action,
        IReadOnlyDictionary<string, object?[]>? changes = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        db.SettingsChanges.Add(new SettingsChange
        {
            At = clock.GetUtcNow(),
            ActorStaffId = actorStaffId,
            Entity = entity,
            EntityId = entityId,
            Action = action,
            Changes = JsonSerializer.Serialize(changes ?? new Dictionary<string, object?[]>()),
        });
    }

    /// <summary>The built-in Cash a business starts with (research R2).</summary>
    public static PaymentType NewCash(DateTimeOffset now) => new()
    {
        Name = "Cash",
        NormalizedName = "cash",
        Kind = PaymentKinds.Cash,
        BuiltIn = true,
        OpensDrawer = true,
        AsksReference = false,
        Position = 0,
        CreatedAt = now,
        UpdatedAt = now,
    };

    /// <summary>A new business's selling settings: no tables, every cashier discount approved (0%), no tips.</summary>
    public static SellingSettings NewSelling(Guid businessId, DateTimeOffset now) => new()
    {
        Id = businessId,
        DiscountLimitPercent = 0m,
        UpdatedAt = now,
    };

    /// <summary>The open business's selling settings (always present; created with the business).</summary>
    public static Task<SellingSettings> SellingAsync(HelloPosDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var id = db.CurrentBusinessId ?? throw new InvalidOperationException("No business is open.");
        return db.SellingSettings.SingleAsync(s => s.Id == id, cancellationToken);
    }

    /// <summary>Saves; a racing stale version becomes 409 and a racing duplicate name a field error.</summary>
    public static async Task<IResult?> SaveAsync(HelloPosDbContext db, string nameField, string duplicateMessage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return SettingsChanged();
        }
        catch (DbUpdateException e) when (CatalogSupport.IsUniqueViolation(e))
        {
            return CatalogSupport.Invalid(nameField, duplicateMessage);
        }
    }

    public static string Normalize(string name) => SettingsRules.Normalize(name);
}

/// <summary>History entity names (contract <c>SettingsChange.entity</c>).</summary>
public static class SettingsEntityNames
{
    public const string PaymentType = "paymentType";
    public const string TableArea = "tableArea";
    public const string Table = "table";
    public const string Selling = "selling";
}

/// <summary>History actions (contract <c>SettingsChange.action</c>).</summary>
public static class SettingsActions
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Deactivated = "deactivated";
    public const string Reactivated = "reactivated";
    public const string Reordered = "reordered";
    public const string BulkCreated = "bulkCreated";
}

public sealed record SettingsVersionRequest(int Version);
