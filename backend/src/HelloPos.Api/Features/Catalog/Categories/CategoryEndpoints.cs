using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using HelloPos.Kernel.Catalog;
using HelloPos.Kernel.Staff;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Catalog.Categories;

public sealed record CategoryRequest(string? Name, int SortOrder, Guid? DefaultTaxProfileId, int Version = 0);

public sealed record CategoryResponse(Guid Id, string Name, int SortOrder, Guid DefaultTaxProfileId, bool Active, int Version);

/// <summary>Categories with a default tax profile (feature #11, US1, FR-003); owners and managers.</summary>
public sealed class CategoryEndpoints : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder app)
    {
        var categories = app.MapGroup("/catalog/categories")
            .WithTags(CatalogSupport.Tag)
            .RequireRole(StaffRole.Owner, StaffRole.Manager)
            .RequireSameBusiness();
        categories.MapGet("", ListAsync).WithName("listCategories");
        categories.MapGet("/{categoryId:guid}", GetAsync).WithName("getCategory");
        categories.MapPost("", CreateAsync).WithName("createCategory").RequireClientHeader();
        categories.MapPut("/{categoryId:guid}", UpdateAsync).WithName("updateCategory").RequireClientHeader();
        categories.MapPost("/{categoryId:guid}/deactivate", (Guid categoryId, CatalogVersionRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken ct) =>
            SetActiveAsync(categoryId, request, active: false, current, db, clock, ct)).WithName("deactivateCategory").RequireClientHeader();
        categories.MapPost("/{categoryId:guid}/reactivate", (Guid categoryId, CatalogVersionRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken ct) =>
            SetActiveAsync(categoryId, request, active: true, current, db, clock, ct)).WithName("reactivateCategory").RequireClientHeader();
    }

    public static async Task<Ok<List<CategoryResponse>>> ListAsync(bool? active, HelloPosDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var categories = await db.Categories.AsNoTracking()
            .Where(c => active == null || c.Active == active)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.NormalizedName)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(categories.Select(ToResponse).ToList());
    }

    public static async Task<Results<Ok<CategoryResponse>, ProblemHttpResult>> GetAsync(Guid categoryId, HelloPosDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var category = await db.Categories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == categoryId, cancellationToken).ConfigureAwait(false);
        return category is null ? CatalogSupport.NotFound() : TypedResults.Ok(ToResponse(category));
    }

    public static async Task<Results<Created<CategoryResponse>, ValidationProblem>> CreateAsync(
        CategoryRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        var errors = await ValidateAsync(db, request, null, null, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return CatalogSupport.Invalid(errors);
        }

        var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
        var now = clock.GetUtcNow();
        var category = new Category
        {
            Name = request.Name!.Trim(),
            NormalizedName = CatalogSupport.Normalize(request.Name),
            SortOrder = request.SortOrder,
            DefaultTaxProfileId = request.DefaultTaxProfileId!.Value,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Categories.Add(category);
        db.RecordChange(clock, actor.Id, CatalogEntityNames.Category, category.Id, CatalogActions.Created, new Dictionary<string, object?[]>
        {
            ["name"] = [null, category.Name],
            ["sortOrder"] = [null, category.SortOrder],
            ["defaultTaxProfileId"] = [null, category.DefaultTaxProfileId],
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException e) when (CatalogSupport.IsUniqueViolation(e))
        {
            return CatalogSupport.Invalid("name", "Another category has this name.");
        }

        return TypedResults.Created($"/catalog/categories/{category.Id}", ToResponse(category));
    }

    public static async Task<Results<Ok<CategoryResponse>, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        Guid categoryId, CategoryRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == categoryId, cancellationToken).ConfigureAwait(false);
        if (category is null)
        {
            return CatalogSupport.NotFound();
        }

        if (category.Version != request.Version)
        {
            return CatalogSupport.CatalogChanged();
        }

        var errors = await ValidateAsync(db, request, category.Id, category.DefaultTaxProfileId, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return CatalogSupport.Invalid(errors);
        }

        var changes = new Dictionary<string, object?[]>();
        changes.Diff("name", category.Name, request.Name!.Trim());
        changes.Diff("sortOrder", category.SortOrder, request.SortOrder);
        changes.Diff("defaultTaxProfileId", category.DefaultTaxProfileId, request.DefaultTaxProfileId!.Value);
        if (changes.Count > 0)
        {
            var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
            db.RecordChange(clock, actor.Id, CatalogEntityNames.Category, category.Id, CatalogActions.Updated, changes);
            category.Name = request.Name!.Trim();
            category.NormalizedName = CatalogSupport.Normalize(request.Name);
            category.SortOrder = request.SortOrder;
            category.DefaultTaxProfileId = request.DefaultTaxProfileId!.Value;
        }

        return await SaveAsync(db, category, request.Version, clock, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Results<Ok<CategoryResponse>, ValidationProblem, ProblemHttpResult>> SetActiveAsync(
        Guid categoryId, CatalogVersionRequest request, bool active, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == categoryId, cancellationToken).ConfigureAwait(false);
        if (category is null)
        {
            return CatalogSupport.NotFound();
        }

        if (category.Version != request.Version)
        {
            return CatalogSupport.CatalogChanged();
        }

        if (category.Active != active)
        {
            var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
            db.RecordChange(clock, actor.Id, CatalogEntityNames.Category, category.Id, active ? CatalogActions.Reactivated : CatalogActions.Deactivated);
            category.Active = active;
        }

        return await SaveAsync(db, category, request.Version, clock, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Field rules; the default tax profile must be an active profile of this business (row-level
    /// security hides other businesses' profiles, so their ids are simply unknown here). Keeping an
    /// already-linked profile that was deactivated later is allowed.
    /// </summary>
    private static async Task<Dictionary<string, List<string>>> ValidateAsync(
        HelloPosDbContext db, CategoryRequest request, Guid? except, Guid? currentProfile, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        errors.Add("name", CatalogRules.ValidateName(request.Name, CatalogRules.CategoryNameMaxLength));
        if (!errors.ContainsKey("name"))
        {
            var normalized = CatalogSupport.Normalize(request.Name!);
            if (await db.Categories.AnyAsync(c => c.NormalizedName == normalized && c.Id != except, cancellationToken).ConfigureAwait(false))
            {
                errors.Add("name", "Another category has this name.");
            }
        }

        if (request.DefaultTaxProfileId is not { } profileId)
        {
            errors.Add("defaultTaxProfileId", "Choose a tax profile.");
        }
        else if (profileId != currentProfile
            && !await db.TaxProfiles.AnyAsync(p => p.Id == profileId && p.Active, cancellationToken).ConfigureAwait(false))
        {
            errors.Add("defaultTaxProfileId", "Choose an active tax profile of this business.");
        }

        return errors;
    }

    private static async Task<Results<Ok<CategoryResponse>, ValidationProblem, ProblemHttpResult>> SaveAsync(
        HelloPosDbContext db, Category category, int version, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (db.ChangeTracker.HasChanges())
        {
            db.Entry(category).Property(c => c.Version).OriginalValue = version;
            category.Version = version + 1;
            category.UpdatedAt = clock.GetUtcNow();
            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                return CatalogSupport.CatalogChanged();
            }
            catch (DbUpdateException e) when (CatalogSupport.IsUniqueViolation(e))
            {
                return CatalogSupport.Invalid("name", "Another category has this name.");
            }
        }

        return TypedResults.Ok(ToResponse(category));
    }

    private static CategoryResponse ToResponse(Category category) =>
        new(category.Id, category.Name, category.SortOrder, category.DefaultTaxProfileId, category.Active, category.Version);
}
