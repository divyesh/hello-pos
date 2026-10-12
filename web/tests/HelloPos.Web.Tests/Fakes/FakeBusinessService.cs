using HelloPos.Kernel.Businesses;
using HelloPos.Web.Services;

namespace HelloPos.Web.Tests.Fakes;

/// <summary>In-memory businesses for page tests; results can be queued per operation.</summary>
public sealed class FakeBusinessService : IBusinessService
{
    public List<BusinessListItem> Businesses { get; } = [];

    public List<BusinessSettings> Created { get; } = [];

    public List<(Guid PageBusiness, BusinessSettings Settings)> Updated { get; } = [];

    public List<Guid> Opened { get; } = [];

    public OwnerActionResult NextCreate { get; set; } = OwnerActionResult.Success;

    public OwnerActionResult NextUpdate { get; set; } = OwnerActionResult.Success;

    public CurrentBusinessResult? NextGetCurrent { get; set; }

    public Dictionary<Guid, BusinessSettings> Settings { get; } = [];

    public int ListCalls { get; private set; }

    public Task<BusinessListResult> ListMineAsync(CancellationToken cancellationToken = default)
    {
        ListCalls++;
        return Task.FromResult<BusinessListResult>(new BusinessListResult.Loaded([.. Businesses]));
    }

    public Task<OwnerActionResult> CreateAsync(BusinessSettings settings, CancellationToken cancellationToken = default)
    {
        if (NextCreate is OwnerActionResult.Succeeded)
        {
            Created.Add(settings);
        }

        return Task.FromResult(NextCreate);
    }

    public Task<OwnerActionResult> OpenAsync(Guid businessId, CancellationToken cancellationToken = default)
    {
        Opened.Add(businessId);
        return Task.FromResult(OwnerActionResult.Success);
    }

    public Task<CurrentBusinessResult> GetCurrentAsync(Guid pageBusiness, CancellationToken cancellationToken = default) =>
        Task.FromResult(NextGetCurrent ?? (Settings.TryGetValue(pageBusiness, out var settings)
            ? new CurrentBusinessResult.Loaded(pageBusiness, settings)
            : new CurrentBusinessResult.Failed("not found")));

    public List<bool?> DepositsTaxableSent { get; } = [];

    public Task<OwnerActionResult> UpdateCurrentAsync(Guid pageBusiness, BusinessSettings settings, bool? depositsTaxable = null, CancellationToken cancellationToken = default)
    {
        if (NextUpdate is OwnerActionResult.Succeeded)
        {
            Updated.Add((pageBusiness, settings));
            DepositsTaxableSent.Add(depositsTaxable);
            Settings[pageBusiness] = settings;
        }

        return Task.FromResult(NextUpdate);
    }
}
