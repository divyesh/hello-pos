using HelloPos.Api.Features.Catalog;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using HelloPos.Kernel.Settings;
using HelloPos.Kernel.Staff;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Settings.PaymentTypes;

public sealed record NewPaymentTypeRequest(string? Name, string? Kind, bool? OpensDrawer, bool? AsksReference);

public sealed record PaymentTypeUpdateRequest(string? Name, string? Kind, bool OpensDrawer, bool AsksReference, int Version);

public sealed record PaymentTypeOrderRequest(List<Guid>? Ids);

public sealed record PaymentTypeResponse(
    Guid Id, string Name, string Kind, bool BuiltIn, bool OpensDrawer, bool AsksReference, bool GivesChange, int Position, bool Active, int Version);

/// <summary>
/// Payment types (feature #12, US1, clarification Q1): owners add, change, order and deactivate them;
/// owners and managers read them. Cash is built in: it can be renamed and moved, never deactivated or
/// changed in kind, and is the only type that gives change.
/// </summary>
public sealed class PaymentTypeEndpoints : IEndpointMapper
{
    private const string Duplicate = "Another payment type has this name.";

    public void Map(IEndpointRouteBuilder app)
    {
        var types = app.MapGroup("/settings/payment-types")
            .WithTags(SettingsSupport.Tag)
            .RequireRole(StaffRole.Owner, StaffRole.Manager)
            .RequireSameBusiness();
        types.MapGet("", ListAsync).WithName("listPaymentTypes");
        types.MapPost("", CreateAsync).WithName("createPaymentType").RequireClientHeader().RequireRole(StaffRole.Owner);
        types.MapPut("/order", ReorderAsync).WithName("reorderPaymentTypes").RequireClientHeader().RequireRole(StaffRole.Owner);
        types.MapPut("/{paymentTypeId:guid}", UpdateAsync).WithName("updatePaymentType").RequireClientHeader().RequireRole(StaffRole.Owner);
        types.MapPost("/{paymentTypeId:guid}/deactivate", (Guid paymentTypeId, SettingsVersionRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken ct) =>
            SetActiveAsync(paymentTypeId, request, active: false, current, db, clock, ct)).WithName("deactivatePaymentType").RequireClientHeader().RequireRole(StaffRole.Owner);
        types.MapPost("/{paymentTypeId:guid}/reactivate", (Guid paymentTypeId, SettingsVersionRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken ct) =>
            SetActiveAsync(paymentTypeId, request, active: true, current, db, clock, ct)).WithName("reactivatePaymentType").RequireClientHeader().RequireRole(StaffRole.Owner);
    }

    public static async Task<Ok<List<PaymentTypeResponse>>> ListAsync(HelloPosDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        return TypedResults.Ok((await OrderedAsync(db, cancellationToken).ConfigureAwait(false)).Select(ToResponse).ToList());
    }

    public static async Task<IResult> CreateAsync(
        NewPaymentTypeRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        errors.Add("name", SettingsRules.ValidateName(request.Name, SettingsRules.PaymentTypeNameMaxLength));
        errors.Add("kind", SettingsRules.ValidateNewKind(request.Kind));
        if (!errors.ContainsKey("name") && await NameTakenAsync(db, request.Name!, null, cancellationToken).ConfigureAwait(false))
        {
            errors.Add("name", Duplicate);
        }

        var all = await db.PaymentTypes.ToListAsync(cancellationToken).ConfigureAwait(false);
        if (all.Count(t => t.Active) >= SettingsRules.MaxActivePaymentTypes)
        {
            errors.Add("name", $"A business can have at most {SettingsRules.MaxActivePaymentTypes} active payment types; deactivate one first.");
        }

        if (errors.Count > 0)
        {
            return CatalogSupport.Invalid(errors);
        }

        var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
        var now = clock.GetUtcNow();
        var type = new PaymentType
        {
            Name = request.Name!.Trim(),
            NormalizedName = SettingsSupport.Normalize(request.Name),
            Kind = PaymentKinds.Stored(request.Kind!),
            OpensDrawer = request.OpensDrawer ?? false,
            AsksReference = request.AsksReference ?? SettingsRules.ReferenceKinds.Contains(request.Kind!),
            Position = all.Count == 0 ? 0 : all.Max(t => t.Position) + 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.PaymentTypes.Add(type);
        db.RecordSettingsChange(clock, actor.Id, SettingsEntityNames.PaymentType, type.Id, SettingsActions.Created, new Dictionary<string, object?[]>
        {
            ["name"] = [null, type.Name], ["kind"] = [null, request.Kind], ["opensDrawer"] = [null, type.OpensDrawer], ["asksReference"] = [null, type.AsksReference],
        });
        return await SaveAsync(db, type, cancellationToken).ConfigureAwait(false) ?? TypedResults.Created($"/settings/payment-types/{type.Id}", ToResponse(type));
    }

    public static async Task<IResult> UpdateAsync(
        Guid paymentTypeId, PaymentTypeUpdateRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        var type = await db.PaymentTypes.SingleOrDefaultAsync(t => t.Id == paymentTypeId, cancellationToken).ConfigureAwait(false);
        if (type is null)
        {
            return CatalogSupport.NotFound();
        }

        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        errors.Add("name", SettingsRules.ValidateName(request.Name, SettingsRules.PaymentTypeNameMaxLength));
        if (type.BuiltIn)
        {
            errors.Add("kind", request.Kind == SettingsRules.Cash ? null : "Cash is always cash.");
        }
        else
        {
            errors.Add("kind", SettingsRules.ValidateNewKind(request.Kind));
        }

        if (errors.Count > 0)
        {
            return CatalogSupport.Invalid(errors);
        }

        if (type.Version != request.Version)
        {
            return SettingsSupport.SettingsChanged();
        }

        if (await NameTakenAsync(db, request.Name!, type.Id, cancellationToken).ConfigureAwait(false))
        {
            return CatalogSupport.Invalid("name", Duplicate);
        }

        var changes = new Dictionary<string, object?[]>();
        var name = request.Name!.Trim();
        var kind = PaymentKinds.Stored(request.Kind!);
        changes.Diff("name", type.Name, name);
        changes.Diff("kind", PaymentKinds.ToWire(type.Kind), request.Kind);
        changes.Diff("opensDrawer", type.OpensDrawer, request.OpensDrawer);
        changes.Diff("asksReference", type.AsksReference, request.AsksReference);
        if (changes.Count > 0)
        {
            var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
            type.Name = name;
            type.NormalizedName = SettingsSupport.Normalize(name);
            type.Kind = kind;
            type.OpensDrawer = request.OpensDrawer;
            type.AsksReference = request.AsksReference;
            Touch(type, clock);
            db.RecordSettingsChange(clock, actor.Id, SettingsEntityNames.PaymentType, type.Id, SettingsActions.Updated, changes);
        }

        return await SaveAsync(db, type, cancellationToken).ConfigureAwait(false) ?? TypedResults.Ok(ToResponse(type));
    }

    public static async Task<IResult> SetActiveAsync(
        Guid paymentTypeId, SettingsVersionRequest request, bool active, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        var type = await db.PaymentTypes.SingleOrDefaultAsync(t => t.Id == paymentTypeId, cancellationToken).ConfigureAwait(false);
        if (type is null)
        {
            return CatalogSupport.NotFound();
        }

        if (type.BuiltIn && !active)
        {
            return SettingsSupport.NotAllowed("Cash is always available and cannot be deactivated.");
        }

        if (type.Version != request.Version)
        {
            return SettingsSupport.SettingsChanged();
        }

        if (active && !type.Active
            && await db.PaymentTypes.CountAsync(t => t.Active, cancellationToken).ConfigureAwait(false) >= SettingsRules.MaxActivePaymentTypes)
        {
            return SettingsSupport.NotAllowed($"A business can have at most {SettingsRules.MaxActivePaymentTypes} active payment types; deactivate one first.");
        }

        if (type.Active != active)
        {
            var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
            type.Active = active;
            Touch(type, clock);
            db.RecordSettingsChange(clock, actor.Id, SettingsEntityNames.PaymentType, type.Id,
                active ? SettingsActions.Reactivated : SettingsActions.Deactivated, new Dictionary<string, object?[]> { ["active"] = [!active, active] });
        }

        return await SaveAsync(db, type, cancellationToken).ConfigureAwait(false) ?? TypedResults.Ok(ToResponse(type));
    }

    /// <summary>A new order: every payment type of the business exactly once (FR-005).</summary>
    public static async Task<IResult> ReorderAsync(
        PaymentTypeOrderRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        var ids = request.Ids ?? [];
        var all = await db.PaymentTypes.ToListAsync(cancellationToken).ConfigureAwait(false);
        if (ids.Count != all.Count || ids.Distinct().Count() != ids.Count || !ids.All(id => all.Any(t => t.Id == id)))
        {
            return CatalogSupport.Invalid("ids", "List every payment type exactly once.");
        }

        var before = all.OrderBy(t => t.Position).Select(t => t.Name).ToList();
        for (var position = 0; position < ids.Count; position++)
        {
            var type = all.Single(t => t.Id == ids[position]);
            if (type.Position != position)
            {
                type.Position = position;
                Touch(type, clock);
            }
        }

        var after = ids.Select(id => all.Single(t => t.Id == id).Name).ToList();
        if (!before.SequenceEqual(after))
        {
            var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
            db.RecordSettingsChange(clock, actor.Id, SettingsEntityNames.PaymentType, db.CurrentBusinessId!.Value, SettingsActions.Reordered,
                new Dictionary<string, object?[]> { ["order"] = [string.Join(", ", before), string.Join(", ", after)] });
        }

        var problem = await SettingsSupport.SaveAsync(db, "ids", Duplicate, cancellationToken).ConfigureAwait(false);
        return problem ?? TypedResults.Ok((await OrderedAsync(db, cancellationToken).ConfigureAwait(false)).Select(ToResponse).ToList());
    }

    public static PaymentTypeResponse ToResponse(PaymentType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new PaymentTypeResponse(type.Id, type.Name, PaymentKinds.ToWire(type.Kind), type.BuiltIn, type.OpensDrawer, type.AsksReference,
            type.GivesChange, type.Position, type.Active, type.Version);
    }

    private static Task<List<PaymentType>> OrderedAsync(HelloPosDbContext db, CancellationToken cancellationToken) =>
        db.PaymentTypes.AsNoTracking().OrderBy(t => t.Position).ThenBy(t => t.NormalizedName).ToListAsync(cancellationToken);

    private static Task<bool> NameTakenAsync(HelloPosDbContext db, string name, Guid? except, CancellationToken cancellationToken)
    {
        var normalized = SettingsSupport.Normalize(name);
        return db.PaymentTypes.AnyAsync(t => t.NormalizedName == normalized && t.Id != except, cancellationToken);
    }

    private static void Touch(PaymentType type, TimeProvider clock)
    {
        type.Version++;
        type.UpdatedAt = clock.GetUtcNow();
    }

    private static Task<IResult?> SaveAsync(HelloPosDbContext db, PaymentType type, CancellationToken cancellationToken)
    {
        _ = type;
        return SettingsSupport.SaveAsync(db, "name", Duplicate, cancellationToken);
    }
}
