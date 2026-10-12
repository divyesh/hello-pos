using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using HelloPos.Kernel.Catalog;
using HelloPos.Kernel.Staff;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Catalog.TaxProfiles;

public sealed record TaxComponentDto(string? Label, string? RatePercent);

public sealed record NewTaxProfileRequest(string? Name, string? Kind, DateOnly? EffectiveFrom, List<TaxComponentDto>? Components);

public sealed record TaxProfileRenameRequest(string? Name, int Version);

public sealed record TaxProfileVersionRequest(DateOnly? EffectiveFrom, List<TaxComponentDto>? Components, int Version);

public sealed record TaxProfileVersionResponse(Guid Id, DateOnly EffectiveFrom, List<TaxComponentDto> Components, bool Current, bool Editable);

public sealed record TaxProfileResponse(Guid Id, string Name, string Kind, bool Active, int Version, List<TaxProfileVersionResponse> Versions);

/// <summary>
/// Tax profiles (feature #11, US1, research R2): owners create and change them; owners and managers read
/// them. Rates live in versions effective from a date in the business's time zone; versions already in
/// force never change, so completed sales keep the tax they were sold with.
/// </summary>
public sealed class TaxProfileEndpoints : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder app)
    {
        var profiles = app.MapGroup("/catalog/tax-profiles")
            .WithTags(CatalogSupport.Tag)
            .RequireRole(StaffRole.Owner, StaffRole.Manager)
            .RequireSameBusiness();
        profiles.MapGet("", ListAsync).WithName("listTaxProfiles");
        profiles.MapGet("/{taxProfileId:guid}", GetAsync).WithName("getTaxProfile");
        profiles.MapPost("", CreateAsync).WithName("createTaxProfile").RequireClientHeader().RequireRole(StaffRole.Owner);
        profiles.MapPut("/{taxProfileId:guid}", RenameAsync).WithName("renameTaxProfile").RequireClientHeader().RequireRole(StaffRole.Owner);
        profiles.MapPost("/{taxProfileId:guid}/versions", AddVersionAsync).WithName("addTaxProfileVersion").RequireClientHeader().RequireRole(StaffRole.Owner);
        profiles.MapPut("/{taxProfileId:guid}/versions/{taxProfileVersionId:guid}", UpdateVersionAsync)
            .WithName("updateTaxProfileVersion").RequireClientHeader().RequireRole(StaffRole.Owner);
        profiles.MapDelete("/{taxProfileId:guid}/versions/{taxProfileVersionId:guid}", DeleteVersionAsync)
            .WithName("deleteTaxProfileVersion").RequireClientHeader().RequireRole(StaffRole.Owner);
        profiles.MapPost("/{taxProfileId:guid}/deactivate", (Guid taxProfileId, CatalogVersionRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken ct) =>
            SetActiveAsync(taxProfileId, request, active: false, current, db, clock, ct)).WithName("deactivateTaxProfile").RequireClientHeader().RequireRole(StaffRole.Owner);
        profiles.MapPost("/{taxProfileId:guid}/reactivate", (Guid taxProfileId, CatalogVersionRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken ct) =>
            SetActiveAsync(taxProfileId, request, active: true, current, db, clock, ct)).WithName("reactivateTaxProfile").RequireClientHeader().RequireRole(StaffRole.Owner);
    }

    public static async Task<Ok<List<TaxProfileResponse>>> ListAsync(bool? active, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var profiles = await db.TaxProfiles.AsNoTracking()
            .Where(p => active == null || p.Active == active)
            .OrderBy(p => p.NormalizedName)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var today = CatalogSupport.Today(await CatalogSupport.BusinessAsync(db, cancellationToken).ConfigureAwait(false), clock);
        var versions = await VersionsAsync(db, profiles.Select(p => p.Id).ToList(), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(profiles.Select(p => ToResponse(p, versions.GetValueOrDefault(p.Id, []), today)).ToList());
    }

    public static async Task<Results<Ok<TaxProfileResponse>, ProblemHttpResult>> GetAsync(
        Guid taxProfileId, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var profile = await db.TaxProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == taxProfileId, cancellationToken).ConfigureAwait(false);
        return profile is null
            ? CatalogSupport.NotFound()
            : TypedResults.Ok(await ResponseAsync(db, profile, clock, cancellationToken).ConfigureAwait(false));
    }

    public static async Task<Results<Created<TaxProfileResponse>, ValidationProblem>> CreateAsync(
        NewTaxProfileRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        errors.Add("name", CatalogRules.ValidateName(request.Name, CatalogRules.TaxProfileNameMaxLength));
        var kind = KindFromWire(request.Kind);
        if (kind is null)
        {
            errors.Add("kind", "Choose taxable, zero-rated or exempt.");
        }

        var components = ParseComponents(request.Components, kind, errors);
        if (!errors.ContainsKey("name") && await NameTakenAsync(db, request.Name!, null, cancellationToken).ConfigureAwait(false))
        {
            errors.Add("name", "Another tax profile has this name.");
        }

        if (errors.Count > 0)
        {
            return CatalogSupport.Invalid(errors);
        }

        var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
        var today = CatalogSupport.Today(await CatalogSupport.BusinessAsync(db, cancellationToken).ConfigureAwait(false), clock);
        var now = clock.GetUtcNow();
        var profile = new TaxProfile
        {
            Name = request.Name!.Trim(),
            NormalizedName = CatalogSupport.Normalize(request.Name),
            Kind = kind!,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.TaxProfiles.Add(profile);
        AddVersion(db, profile.Id, request.EffectiveFrom ?? today, components);
        db.RecordChange(clock, actor.Id, CatalogEntityNames.TaxProfile, profile.Id, CatalogActions.Created,
            new Dictionary<string, object?[]> { ["name"] = [null, profile.Name], ["kind"] = [null, request.Kind] });
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException e) when (CatalogSupport.IsUniqueViolation(e))
        {
            return CatalogSupport.Invalid("name", "Another tax profile has this name.");
        }

        return TypedResults.Created($"/catalog/tax-profiles/{profile.Id}", await ResponseAsync(db, profile, clock, cancellationToken).ConfigureAwait(false));
    }

    public static async Task<Results<Ok<TaxProfileResponse>, ValidationProblem, ProblemHttpResult>> RenameAsync(
        Guid taxProfileId, TaxProfileRenameRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        if (CatalogRules.ValidateName(request.Name, CatalogRules.TaxProfileNameMaxLength) is { } invalid)
        {
            return CatalogSupport.Invalid("name", invalid);
        }

        var profile = await db.TaxProfiles.SingleOrDefaultAsync(p => p.Id == taxProfileId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return CatalogSupport.NotFound();
        }

        if (profile.Version != request.Version)
        {
            return CatalogSupport.CatalogChanged();
        }

        if (await NameTakenAsync(db, request.Name!, profile.Id, cancellationToken).ConfigureAwait(false))
        {
            return CatalogSupport.Invalid("name", "Another tax profile has this name.");
        }

        var name = request.Name!.Trim();
        if (name != profile.Name)
        {
            var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
            db.RecordChange(clock, actor.Id, CatalogEntityNames.TaxProfile, profile.Id, CatalogActions.Updated,
                new Dictionary<string, object?[]> { ["name"] = [profile.Name, name] });
            profile.Name = name;
            profile.NormalizedName = CatalogSupport.Normalize(name);
        }

        return await SaveAsync(db, profile, request.Version, clock, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Results<Created<TaxProfileResponse>, ValidationProblem, ProblemHttpResult>> AddVersionAsync(
        Guid taxProfileId, TaxProfileVersionRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        var profile = await db.TaxProfiles.SingleOrDefaultAsync(p => p.Id == taxProfileId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return CatalogSupport.NotFound();
        }

        if (profile.Version != request.Version)
        {
            return CatalogSupport.CatalogChanged();
        }

        var today = CatalogSupport.Today(await CatalogSupport.BusinessAsync(db, cancellationToken).ConfigureAwait(false), clock);
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var components = ParseComponents(request.Components, profile.Kind, errors);
        var from = await ValidateDateAsync(db, profile.Id, request.EffectiveFrom, today, null, errors, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return CatalogSupport.Invalid(errors);
        }

        var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
        var version = AddVersion(db, profile.Id, from, components);
        db.RecordChange(clock, actor.Id, CatalogEntityNames.TaxProfileVersion, version.Id, CatalogActions.Created,
            new Dictionary<string, object?[]> { ["effectiveFrom"] = [null, from.ToString("O")], ["components"] = [null, Describe(components)] });
        var saved = await SaveAsync(db, profile, request.Version, clock, cancellationToken, touch: true).ConfigureAwait(false);
        return saved.Result switch
        {
            Ok<TaxProfileResponse> ok => TypedResults.Created($"/catalog/tax-profiles/{profile.Id}", ok.Value),
            ValidationProblem invalid => invalid,
            _ => (ProblemHttpResult)saved.Result,
        };
    }

    public static async Task<Results<Ok<TaxProfileResponse>, ValidationProblem, ProblemHttpResult>> UpdateVersionAsync(
        Guid taxProfileId, Guid taxProfileVersionId, TaxProfileVersionRequest request, ICurrentStaff current, HelloPosDbContext db,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        var profile = await db.TaxProfiles.SingleOrDefaultAsync(p => p.Id == taxProfileId, cancellationToken).ConfigureAwait(false);
        var version = await db.TaxProfileVersions.SingleOrDefaultAsync(v => v.Id == taxProfileVersionId && v.TaxProfileId == taxProfileId, cancellationToken).ConfigureAwait(false);
        if (profile is null || version is null)
        {
            return CatalogSupport.NotFound();
        }

        var today = CatalogSupport.Today(await CatalogSupport.BusinessAsync(db, cancellationToken).ConfigureAwait(false), clock);
        if (version.EffectiveFrom <= today)
        {
            return CatalogSupport.NotEditable("This version is already in force and cannot change. Add a new version instead.");
        }

        if (profile.Version != request.Version)
        {
            return CatalogSupport.CatalogChanged();
        }

        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var components = ParseComponents(request.Components, profile.Kind, errors);
        var from = await ValidateDateAsync(db, profile.Id, request.EffectiveFrom, today, version.Id, errors, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return CatalogSupport.Invalid(errors);
        }

        var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
        var old = await db.TaxComponents.Where(c => c.TaxProfileVersionId == version.Id).OrderBy(c => c.Position).ToListAsync(cancellationToken).ConfigureAwait(false);
        db.RecordChange(clock, actor.Id, CatalogEntityNames.TaxProfileVersion, version.Id, CatalogActions.Updated, new Dictionary<string, object?[]>
        {
            ["effectiveFrom"] = [version.EffectiveFrom.ToString("O"), from.ToString("O")],
            ["components"] = [Describe(old.Select(c => (c.Label, c.RatePercent)).ToList()), Describe(components)],
        });
        db.TaxComponents.RemoveRange(old);
        version.EffectiveFrom = from;
        AddComponents(db, version.Id, components);
        return await SaveAsync(db, profile, request.Version, clock, cancellationToken, touch: true).ConfigureAwait(false);
    }

    public static async Task<Results<Ok<TaxProfileResponse>, ValidationProblem, ProblemHttpResult>> DeleteVersionAsync(
        Guid taxProfileId, Guid taxProfileVersionId, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        var profile = await db.TaxProfiles.SingleOrDefaultAsync(p => p.Id == taxProfileId, cancellationToken).ConfigureAwait(false);
        var version = await db.TaxProfileVersions.SingleOrDefaultAsync(v => v.Id == taxProfileVersionId && v.TaxProfileId == taxProfileId, cancellationToken).ConfigureAwait(false);
        if (profile is null || version is null)
        {
            return CatalogSupport.NotFound();
        }

        var today = CatalogSupport.Today(await CatalogSupport.BusinessAsync(db, cancellationToken).ConfigureAwait(false), clock);
        if (version.EffectiveFrom <= today)
        {
            return CatalogSupport.NotEditable("This version is already in force and cannot be removed.");
        }

        var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
        db.RecordChange(clock, actor.Id, CatalogEntityNames.TaxProfileVersion, version.Id, CatalogActions.Deleted,
            new Dictionary<string, object?[]> { ["effectiveFrom"] = [version.EffectiveFrom.ToString("O"), null] });
        db.TaxProfileVersions.Remove(version);
        return await SaveAsync(db, profile, profile.Version, clock, cancellationToken, touch: true).ConfigureAwait(false);
    }

    public static async Task<Results<Ok<TaxProfileResponse>, ValidationProblem, ProblemHttpResult>> SetActiveAsync(
        Guid taxProfileId, CatalogVersionRequest request, bool active, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        var profile = await db.TaxProfiles.SingleOrDefaultAsync(p => p.Id == taxProfileId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return CatalogSupport.NotFound();
        }

        if (profile.Version != request.Version)
        {
            return CatalogSupport.CatalogChanged();
        }

        if (profile.Active != active)
        {
            var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
            db.RecordChange(clock, actor.Id, CatalogEntityNames.TaxProfile, profile.Id, active ? CatalogActions.Reactivated : CatalogActions.Deactivated);
            profile.Active = active;
        }

        return await SaveAsync(db, profile, request.Version, clock, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The tax kind as stored, from the wire name; null when unknown.</summary>
    internal static string? KindFromWire(string? kind) => kind switch
    {
        "taxable" => TaxKinds.Taxable,
        "zeroRated" => TaxKinds.ZeroRated,
        "exempt" => TaxKinds.Exempt,
        _ => null,
    };

    internal static string KindToWire(string kind) => kind switch
    {
        TaxKinds.ZeroRated => "zeroRated",
        TaxKinds.Exempt => "exempt",
        _ => "taxable",
    };

    /// <summary>Validates the components for the profile's kind; returns them parsed (empty when invalid).</summary>
    private static List<(string Label, decimal Rate)> ParseComponents(List<TaxComponentDto>? input, string? kind, Dictionary<string, List<string>> errors)
    {
        var components = new List<(string Label, decimal Rate)>();
        var given = input ?? [];
        if (kind == TaxKinds.Taxable && given.Count == 0)
        {
            errors.Add("components", "A taxable profile needs at least one tax component.");
        }
        else if (kind is TaxKinds.ZeroRated or TaxKinds.Exempt && given.Count > 0)
        {
            errors.Add("components", "Zero-rated and exempt profiles have no tax components.");
        }

        if (given.Count > CatalogRules.MaxComponents)
        {
            errors.Add("components", $"Use at most {CatalogRules.MaxComponents} components.");
        }

        foreach (var component in given)
        {
            var labelError = CatalogRules.ValidateComponentLabel(component.Label);
            var rate = CatalogSupport.ParseDecimal(component.RatePercent);
            var rateError = rate is null ? "Enter the rate as a number, for example 2.5." : CatalogRules.ValidateRate(rate.Value);
            errors.Add("components", labelError is null ? null : $"Label: {labelError}");
            errors.Add("components", rateError is null ? null : $"{component.Label}: {rateError}");
            if (labelError is null && rateError is null)
            {
                components.Add((component.Label!.Trim(), rate!.Value));
            }
        }

        if (components.Select(c => c.Label.ToLowerInvariant()).Distinct().Count() < components.Count)
        {
            errors.Add("components", "Each component needs a different label.");
        }

        return components;
    }

    /// <summary>A version date: today or later and not another version's date.</summary>
    private static async Task<DateOnly> ValidateDateAsync(
        HelloPosDbContext db, Guid profileId, DateOnly? requested, DateOnly today, Guid? exceptVersion, Dictionary<string, List<string>> errors,
        CancellationToken cancellationToken)
    {
        if (requested is not { } from)
        {
            errors.Add("effectiveFrom", "Choose the first day the rates apply.");
            return today;
        }

        if (from < today)
        {
            errors.Add("effectiveFrom", "Choose today or a later date; past rates cannot change.");
        }
        else if (await db.TaxProfileVersions.AnyAsync(v => v.TaxProfileId == profileId && v.EffectiveFrom == from && v.Id != exceptVersion, cancellationToken).ConfigureAwait(false))
        {
            errors.Add("effectiveFrom", "Another version starts on this day.");
        }

        return from;
    }

    private static TaxProfileVersion AddVersion(HelloPosDbContext db, Guid profileId, DateOnly from, List<(string Label, decimal Rate)> components)
    {
        var version = new TaxProfileVersion { TaxProfileId = profileId, EffectiveFrom = from };
        db.TaxProfileVersions.Add(version);
        AddComponents(db, version.Id, components);
        return version;
    }

    private static void AddComponents(HelloPosDbContext db, Guid versionId, List<(string Label, decimal Rate)> components)
    {
        for (var i = 0; i < components.Count; i++)
        {
            db.TaxComponents.Add(new TaxComponent { TaxProfileVersionId = versionId, Position = (short)i, Label = components[i].Label, RatePercent = components[i].Rate });
        }
    }

    private static string Describe(List<(string Label, decimal Rate)> components) =>
        string.Join(" + ", components.Select(c => $"{c.Label} {CatalogSupport.Format(c.Rate)}%"));

    private static Task<bool> NameTakenAsync(HelloPosDbContext db, string name, Guid? except, CancellationToken cancellationToken)
    {
        var normalized = CatalogSupport.Normalize(name);
        return db.TaxProfiles.AnyAsync(p => p.NormalizedName == normalized && p.Id != except, cancellationToken);
    }

    private static async Task<Results<Ok<TaxProfileResponse>, ValidationProblem, ProblemHttpResult>> SaveAsync(
        HelloPosDbContext db, TaxProfile profile, int version, TimeProvider clock, CancellationToken cancellationToken, bool touch = false)
    {
        if (db.ChangeTracker.HasChanges() || touch)
        {
            db.Entry(profile).Property(p => p.Version).OriginalValue = version;
            profile.Version = version + 1;
            profile.UpdatedAt = clock.GetUtcNow();
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
                return CatalogSupport.Invalid("name", "Another tax profile has this name, or another version starts on this day.");
            }
        }

        return TypedResults.Ok(await ResponseAsync(db, profile, clock, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<TaxProfileResponse> ResponseAsync(HelloPosDbContext db, TaxProfile profile, TimeProvider clock, CancellationToken cancellationToken)
    {
        var today = CatalogSupport.Today(await CatalogSupport.BusinessAsync(db, cancellationToken).ConfigureAwait(false), clock);
        var versions = await VersionsAsync(db, [profile.Id], cancellationToken).ConfigureAwait(false);
        return ToResponse(profile, versions.GetValueOrDefault(profile.Id, []), today);
    }

    private static async Task<Dictionary<Guid, List<(TaxProfileVersion Version, List<TaxComponent> Components)>>> VersionsAsync(
        HelloPosDbContext db, List<Guid> profileIds, CancellationToken cancellationToken)
    {
        var versions = await db.TaxProfileVersions.AsNoTracking()
            .Where(v => profileIds.Contains(v.TaxProfileId)).OrderBy(v => v.EffectiveFrom)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var versionIds = versions.Select(v => v.Id).ToList();
        var components = (await db.TaxComponents.AsNoTracking()
                .Where(c => versionIds.Contains(c.TaxProfileVersionId)).OrderBy(c => c.Position)
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .GroupBy(c => c.TaxProfileVersionId).ToDictionary(g => g.Key, g => g.ToList());
        return versions.GroupBy(v => v.TaxProfileId).ToDictionary(
            g => g.Key,
            g => g.Select(v => (v, components.GetValueOrDefault(v.Id, []))).ToList());
    }

    /// <summary>The version in force today and the future ones (older versions are history, not shown).</summary>
    private static TaxProfileResponse ToResponse(TaxProfile profile, List<(TaxProfileVersion Version, List<TaxComponent> Components)> versions, DateOnly today)
    {
        var current = versions.LastOrDefault(v => v.Version.EffectiveFrom <= today).Version;
        var shown = versions.Where(v => v.Version.EffectiveFrom > today || v.Version == current).Select(v => new TaxProfileVersionResponse(
            v.Version.Id,
            v.Version.EffectiveFrom,
            v.Components.Select(c => new TaxComponentDto(c.Label, CatalogSupport.Format(c.RatePercent))).ToList(),
            v.Version == current,
            v.Version.EffectiveFrom > today)).ToList();
        return new TaxProfileResponse(profile.Id, profile.Name, KindToWire(profile.Kind), profile.Active, profile.Version, shown);
    }
}
