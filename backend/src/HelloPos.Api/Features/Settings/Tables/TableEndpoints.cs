using HelloPos.Api.Features.Catalog;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using HelloPos.Kernel.Settings;
using HelloPos.Kernel.Staff;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Settings.Tables;

public sealed record TableAreaRequest(string? Name);

public sealed record TableAreaUpdateRequest(string? Name, int Position, int Version);

public sealed record DiningTableRequest(Guid AreaId, string? Name, int? Seats);

public sealed record DiningTableUpdateRequest(Guid AreaId, string? Name, int Seats, int Position, int Version);

public sealed record TableRangeRequest(Guid AreaId, int From, int To, string? Prefix, string? Suffix, int? Seats);

public sealed record DiningTableResponse(Guid Id, Guid AreaId, string Name, int Seats, int Position, bool Active, int Version);

public sealed record TableAreaResponse(Guid Id, string Name, int Position, bool Active, int Version, List<DiningTableResponse> Tables);

/// <summary>
/// Areas and tables (feature #12, US3, FR-010–FR-013): owners and managers set them up. Names are unique
/// in the business ignoring case; nothing is deleted, only deactivated; an active table is always in an
/// active area. Positions are kept 0..n-1: areas among areas, tables within their area.
/// </summary>
public sealed class TableEndpoints : IEndpointMapper
{
    private const string DuplicateArea = "Another area has this name.";
    private const string DuplicateTable = "Another table has this name.";
    private const string InactiveArea = "This area is deactivated; reactivate it first.";

    public void Map(IEndpointRouteBuilder app)
    {
        var tables = app.MapGroup("/settings/tables")
            .WithTags(SettingsSupport.Tag)
            .RequireRole(StaffRole.Owner, StaffRole.Manager)
            .RequireSameBusiness();
        tables.MapGet("/areas", ListAsync).WithName("listTableAreas");
        tables.MapPost("/areas", CreateAreaAsync).WithName("createTableArea").RequireClientHeader();
        tables.MapPut("/areas/{areaId:guid}", UpdateAreaAsync).WithName("updateTableArea").RequireClientHeader();
        tables.MapPost("/areas/{areaId:guid}/deactivate", (Guid areaId, SettingsVersionRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken ct) =>
            SetAreaActiveAsync(areaId, request, active: false, current, db, clock, ct)).WithName("deactivateTableArea").RequireClientHeader();
        tables.MapPost("/areas/{areaId:guid}/reactivate", (Guid areaId, SettingsVersionRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken ct) =>
            SetAreaActiveAsync(areaId, request, active: true, current, db, clock, ct)).WithName("reactivateTableArea").RequireClientHeader();
        tables.MapPost("", CreateTableAsync).WithName("createTable").RequireClientHeader();
        tables.MapPost("/bulk", CreateTablesAsync).WithName("createTables").RequireClientHeader();
        tables.MapPut("/{tableId:guid}", UpdateTableAsync).WithName("updateTable").RequireClientHeader();
        tables.MapPost("/{tableId:guid}/deactivate", (Guid tableId, SettingsVersionRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken ct) =>
            SetTableActiveAsync(tableId, request, active: false, current, db, clock, ct)).WithName("deactivateTable").RequireClientHeader();
        tables.MapPost("/{tableId:guid}/reactivate", (Guid tableId, SettingsVersionRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken ct) =>
            SetTableActiveAsync(tableId, request, active: true, current, db, clock, ct)).WithName("reactivateTable").RequireClientHeader();
    }

    public static async Task<Ok<List<TableAreaResponse>>> ListAsync(HelloPosDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var areas = await db.TableAreas.AsNoTracking().OrderBy(a => a.Position).ThenBy(a => a.NormalizedName).ToListAsync(cancellationToken).ConfigureAwait(false);
        var tables = await db.DiningTables.AsNoTracking().OrderBy(t => t.Position).ThenBy(t => t.NormalizedName).ToListAsync(cancellationToken).ConfigureAwait(false);
        var byArea = tables.ToLookup(t => t.AreaId);
        return TypedResults.Ok(areas.Select(a => ToResponse(a, byArea[a.Id])).ToList());
    }

    public static async Task<IResult> CreateAreaAsync(
        TableAreaRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        var error = SettingsRules.ValidateName(request.Name, SettingsRules.AreaNameMaxLength)
            ?? (await AreaNameTakenAsync(db, request.Name!, null, cancellationToken).ConfigureAwait(false) ? DuplicateArea : null);
        if (error is not null)
        {
            return CatalogSupport.Invalid("name", error);
        }

        var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
        var now = clock.GetUtcNow();
        var positions = await db.TableAreas.Select(a => (int?)a.Position).MaxAsync(cancellationToken).ConfigureAwait(false);
        var area = new TableArea
        {
            Name = request.Name!.Trim(),
            NormalizedName = SettingsSupport.Normalize(request.Name),
            Position = (positions ?? -1) + 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.TableAreas.Add(area);
        db.RecordSettingsChange(clock, actor.Id, SettingsEntityNames.TableArea, area.Id, SettingsActions.Created,
            new Dictionary<string, object?[]> { ["name"] = [null, area.Name] });
        return await SettingsSupport.SaveAsync(db, "name", DuplicateArea, cancellationToken).ConfigureAwait(false)
            ?? TypedResults.Created($"/settings/tables/areas/{area.Id}", ToResponse(area, []));
    }

    /// <summary>Renames an area and moves it to <c>position</c> among the areas.</summary>
    public static async Task<IResult> UpdateAreaAsync(
        Guid areaId, TableAreaUpdateRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        var areas = await db.TableAreas.OrderBy(a => a.Position).ThenBy(a => a.NormalizedName).ToListAsync(cancellationToken).ConfigureAwait(false);
        var area = areas.SingleOrDefault(a => a.Id == areaId);
        if (area is null)
        {
            return CatalogSupport.NotFound();
        }

        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        errors.Add("name", SettingsRules.ValidateName(request.Name, SettingsRules.AreaNameMaxLength));
        errors.Add("position", request.Position >= 0 ? null : "Use 0 or more.");
        if (errors.Count > 0)
        {
            return CatalogSupport.Invalid(errors);
        }

        if (area.Version != request.Version)
        {
            return SettingsSupport.SettingsChanged();
        }

        if (await AreaNameTakenAsync(db, request.Name!, area.Id, cancellationToken).ConfigureAwait(false))
        {
            return CatalogSupport.Invalid("name", DuplicateArea);
        }

        var name = request.Name!.Trim();
        var before = area.Position;
        var changes = new Dictionary<string, object?[]>();
        changes.Diff("name", area.Name, name);
        Place(areas.Where(a => a != area).ToList(), area, request.Position, a => a.Position, (a, p) => a.Position = p, a => Touch(a, clock));
        changes.Diff("position", before, area.Position);
        if (changes.Count > 0)
        {
            var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
            area.Name = name;
            area.NormalizedName = SettingsSupport.Normalize(name);
            Touch(area, clock);
            db.RecordSettingsChange(clock, actor.Id, SettingsEntityNames.TableArea, area.Id, SettingsActions.Updated, changes);
        }

        var problem = await SettingsSupport.SaveAsync(db, "name", DuplicateArea, cancellationToken).ConfigureAwait(false);
        return problem ?? TypedResults.Ok(ToResponse(area, await TablesOfAsync(db, area.Id, cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>Deactivates or reactivates an area; one with active tables stays active (FR-013).</summary>
    public static async Task<IResult> SetAreaActiveAsync(
        Guid areaId, SettingsVersionRequest request, bool active, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        var area = await db.TableAreas.SingleOrDefaultAsync(a => a.Id == areaId, cancellationToken).ConfigureAwait(false);
        if (area is null)
        {
            return CatalogSupport.NotFound();
        }

        if (area.Version != request.Version)
        {
            return SettingsSupport.SettingsChanged();
        }

        if (!active && await db.DiningTables.AnyAsync(t => t.AreaId == area.Id && t.Active, cancellationToken).ConfigureAwait(false))
        {
            return SettingsSupport.NotAllowed("This area has active tables; deactivate or move them first.");
        }

        if (area.Active != active)
        {
            var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
            area.Active = active;
            Touch(area, clock);
            db.RecordSettingsChange(clock, actor.Id, SettingsEntityNames.TableArea, area.Id,
                active ? SettingsActions.Reactivated : SettingsActions.Deactivated, new Dictionary<string, object?[]> { ["active"] = [!active, active] });
        }

        var problem = await SettingsSupport.SaveAsync(db, "name", DuplicateArea, cancellationToken).ConfigureAwait(false);
        return problem ?? TypedResults.Ok(ToResponse(area, await TablesOfAsync(db, area.Id, cancellationToken).ConfigureAwait(false)));
    }

    public static async Task<IResult> CreateTableAsync(
        DiningTableRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        var area = await db.TableAreas.SingleOrDefaultAsync(a => a.Id == request.AreaId, cancellationToken).ConfigureAwait(false);
        if (area is null)
        {
            return CatalogSupport.NotFound();
        }

        var seats = request.Seats ?? SettingsRules.DefaultSeats;
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        errors.Add("name", SettingsRules.ValidateName(request.Name, SettingsRules.TableNameMaxLength));
        errors.Add("seats", SettingsRules.ValidateSeats(seats));
        errors.Add("areaId", area.Active ? null : InactiveArea);
        if (!errors.ContainsKey("name") && await TableNameTakenAsync(db, request.Name!, null, cancellationToken).ConfigureAwait(false))
        {
            errors.Add("name", DuplicateTable);
        }

        if (errors.Count > 0)
        {
            return CatalogSupport.Invalid(errors);
        }

        var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
        var table = NewTable(area.Id, request.Name!, seats, await NextPositionAsync(db, area.Id, cancellationToken).ConfigureAwait(false), clock.GetUtcNow());
        db.DiningTables.Add(table);
        db.RecordSettingsChange(clock, actor.Id, SettingsEntityNames.Table, table.Id, SettingsActions.Created, new Dictionary<string, object?[]>
        {
            ["name"] = [null, table.Name], ["area"] = [null, area.Name], ["seats"] = [null, seats],
        });
        return await SettingsSupport.SaveAsync(db, "name", DuplicateTable, cancellationToken).ConfigureAwait(false)
            ?? TypedResults.Created($"/settings/tables/{table.Id}", ToResponse(table));
    }

    /// <summary>A numbered range in one area, all or nothing, at most 200 (FR-012, research R5).</summary>
    public static async Task<IResult> CreateTablesAsync(
        TableRangeRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        var area = await db.TableAreas.SingleOrDefaultAsync(a => a.Id == request.AreaId, cancellationToken).ConfigureAwait(false);
        if (area is null)
        {
            return CatalogSupport.NotFound();
        }

        var seats = request.Seats ?? SettingsRules.DefaultSeats;
        var (names, rangeError) = SettingsRules.TableRange(request.From, request.To, request.Prefix, request.Suffix);
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        errors.Add("to", rangeError);
        errors.Add("seats", SettingsRules.ValidateSeats(seats));
        errors.Add("areaId", area.Active ? null : InactiveArea);
        if (rangeError is null)
        {
            var normalized = names.Select(SettingsSupport.Normalize).ToList();
            var taken = await db.DiningTables.Where(t => normalized.Contains(t.NormalizedName)).Select(t => t.NormalizedName)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var clashes = names.Where(n => taken.Contains(SettingsSupport.Normalize(n))).ToList();
            errors.Add("names", clashes.Count == 0 ? null : $"These table names are already used: {string.Join(", ", clashes)}.");
        }

        if (errors.Count > 0)
        {
            return CatalogSupport.Invalid(errors);
        }

        var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
        var now = clock.GetUtcNow();
        var first = await NextPositionAsync(db, area.Id, cancellationToken).ConfigureAwait(false);
        var created = names.Select((name, i) => NewTable(area.Id, name, seats, first + i, now)).ToList();
        db.DiningTables.AddRange(created);
        db.RecordSettingsChange(clock, actor.Id, SettingsEntityNames.Table, area.Id, SettingsActions.BulkCreated, new Dictionary<string, object?[]>
        {
            ["count"] = [null, created.Count], ["names"] = [null, $"{names[0]}–{names[^1]}"], ["area"] = [null, area.Name], ["seats"] = [null, seats],
        });
        var problem = await SettingsSupport.SaveAsync(db, "names", "Some of these table names are already used.", cancellationToken).ConfigureAwait(false);
        return problem ?? TypedResults.Created($"/settings/tables/areas/{area.Id}", created.Select(ToResponse).ToList());
    }

    /// <summary>Renames, resizes, moves a table to an area and to <c>position</c> within it.</summary>
    public static async Task<IResult> UpdateTableAsync(
        Guid tableId, DiningTableUpdateRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        var table = await db.DiningTables.SingleOrDefaultAsync(t => t.Id == tableId, cancellationToken).ConfigureAwait(false);
        var area = await db.TableAreas.SingleOrDefaultAsync(a => a.Id == request.AreaId, cancellationToken).ConfigureAwait(false);
        if (table is null || area is null)
        {
            return CatalogSupport.NotFound();
        }

        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        errors.Add("name", SettingsRules.ValidateName(request.Name, SettingsRules.TableNameMaxLength));
        errors.Add("seats", SettingsRules.ValidateSeats(request.Seats));
        errors.Add("position", request.Position >= 0 ? null : "Use 0 or more.");
        errors.Add("areaId", area.Active || !table.Active || area.Id == table.AreaId ? null : InactiveArea);
        if (errors.Count > 0)
        {
            return CatalogSupport.Invalid(errors);
        }

        if (table.Version != request.Version)
        {
            return SettingsSupport.SettingsChanged();
        }

        if (await TableNameTakenAsync(db, request.Name!, table.Id, cancellationToken).ConfigureAwait(false))
        {
            return CatalogSupport.Invalid("name", DuplicateTable);
        }

        var name = request.Name!.Trim();
        var oldAreaId = table.AreaId;
        var oldPosition = table.Position;
        var changes = new Dictionary<string, object?[]>();
        changes.Diff("name", table.Name, name);
        changes.Diff("seats", (int)table.Seats, request.Seats);
        if (oldAreaId != area.Id)
        {
            var oldArea = await db.TableAreas.AsNoTracking().SingleAsync(a => a.Id == oldAreaId, cancellationToken).ConfigureAwait(false);
            changes["area"] = [oldArea.Name, area.Name];
        }

        var others = await db.DiningTables.Where(t => (t.AreaId == area.Id || t.AreaId == oldAreaId) && t.Id != table.Id)
            .OrderBy(t => t.Position).ThenBy(t => t.NormalizedName).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (oldAreaId != area.Id)
        {
            Renumber(others.Where(t => t.AreaId == oldAreaId).ToList(), null, t => t.Position, (t, p) => t.Position = p, t => Touch(t, clock));
        }

        Place(others.Where(t => t.AreaId == area.Id).ToList(), table, request.Position, t => t.Position, (t, p) => t.Position = p, t => Touch(t, clock));
        changes.Diff("position", oldPosition, table.Position);
        if (changes.Count > 0)
        {
            var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
            table.Name = name;
            table.NormalizedName = SettingsSupport.Normalize(name);
            table.Seats = (short)request.Seats;
            table.AreaId = area.Id;
            Touch(table, clock);
            db.RecordSettingsChange(clock, actor.Id, SettingsEntityNames.Table, table.Id, SettingsActions.Updated, changes);
        }

        return await SettingsSupport.SaveAsync(db, "name", DuplicateTable, cancellationToken).ConfigureAwait(false) ?? TypedResults.Ok(ToResponse(table));
    }

    public static async Task<IResult> SetTableActiveAsync(
        Guid tableId, SettingsVersionRequest request, bool active, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        var table = await db.DiningTables.SingleOrDefaultAsync(t => t.Id == tableId, cancellationToken).ConfigureAwait(false);
        if (table is null)
        {
            return CatalogSupport.NotFound();
        }

        if (table.Version != request.Version)
        {
            return SettingsSupport.SettingsChanged();
        }

        if (active && !table.Active
            && !await db.TableAreas.AnyAsync(a => a.Id == table.AreaId && a.Active, cancellationToken).ConfigureAwait(false))
        {
            return SettingsSupport.NotAllowed(InactiveArea);
        }

        if (table.Active != active)
        {
            var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
            table.Active = active;
            Touch(table, clock);
            db.RecordSettingsChange(clock, actor.Id, SettingsEntityNames.Table, table.Id,
                active ? SettingsActions.Reactivated : SettingsActions.Deactivated, new Dictionary<string, object?[]> { ["active"] = [!active, active] });
        }

        return await SettingsSupport.SaveAsync(db, "name", DuplicateTable, cancellationToken).ConfigureAwait(false) ?? TypedResults.Ok(ToResponse(table));
    }

    public static TableAreaResponse ToResponse(TableArea area, IEnumerable<DiningTable> tables)
    {
        ArgumentNullException.ThrowIfNull(area);
        return new TableAreaResponse(area.Id, area.Name, area.Position, area.Active, area.Version, tables.Select(ToResponse).ToList());
    }

    public static DiningTableResponse ToResponse(DiningTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return new DiningTableResponse(table.Id, table.AreaId, table.Name, table.Seats, table.Position, table.Active, table.Version);
    }

    /// <summary>
    /// Puts <paramref name="item"/> at <paramref name="position"/> (clamped) among <paramref name="others"/> (in their
    /// order, without it) and numbers them all 0..n-1.
    /// </summary>
    private static void Place<T>(List<T> others, T item, int position, Func<T, int> getPosition, Action<T, int> setPosition, Action<T> touch)
        where T : class
    {
        others.Insert(Math.Clamp(position, 0, others.Count), item);
        Renumber(others, item, getPosition, setPosition, touch);
    }

    /// <summary>
    /// Numbers <paramref name="ordered"/> 0..n-1, touching those whose position changes except <paramref name="item"/>,
    /// which the caller touches once with its other changes.
    /// </summary>
    private static void Renumber<T>(List<T> ordered, T? item, Func<T, int> getPosition, Action<T, int> setPosition, Action<T> touch)
        where T : class
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            if (getPosition(ordered[i]) != i)
            {
                setPosition(ordered[i], i);
                if (ordered[i] != item)
                {
                    touch(ordered[i]);
                }
            }
        }
    }

    private static DiningTable NewTable(Guid areaId, string name, int seats, int position, DateTimeOffset now) => new()
    {
        AreaId = areaId,
        Name = name.Trim(),
        NormalizedName = SettingsSupport.Normalize(name),
        Seats = (short)seats,
        Position = position,
        CreatedAt = now,
        UpdatedAt = now,
    };

    private static async Task<int> NextPositionAsync(HelloPosDbContext db, Guid areaId, CancellationToken cancellationToken) =>
        (await db.DiningTables.Where(t => t.AreaId == areaId).Select(t => (int?)t.Position).MaxAsync(cancellationToken).ConfigureAwait(false) ?? -1) + 1;

    private static Task<List<DiningTable>> TablesOfAsync(HelloPosDbContext db, Guid areaId, CancellationToken cancellationToken) =>
        db.DiningTables.AsNoTracking().Where(t => t.AreaId == areaId).OrderBy(t => t.Position).ThenBy(t => t.NormalizedName).ToListAsync(cancellationToken);

    private static Task<bool> AreaNameTakenAsync(HelloPosDbContext db, string name, Guid? except, CancellationToken cancellationToken)
    {
        var normalized = SettingsSupport.Normalize(name);
        return db.TableAreas.AnyAsync(a => a.NormalizedName == normalized && a.Id != except, cancellationToken);
    }

    private static Task<bool> TableNameTakenAsync(HelloPosDbContext db, string name, Guid? except, CancellationToken cancellationToken)
    {
        var normalized = SettingsSupport.Normalize(name);
        return db.DiningTables.AnyAsync(t => t.NormalizedName == normalized && t.Id != except, cancellationToken);
    }

    private static void Touch(TableArea area, TimeProvider clock)
    {
        area.Version++;
        area.UpdatedAt = clock.GetUtcNow();
    }

    private static void Touch(DiningTable table, TimeProvider clock)
    {
        table.Version++;
        table.UpdatedAt = clock.GetUtcNow();
    }
}
