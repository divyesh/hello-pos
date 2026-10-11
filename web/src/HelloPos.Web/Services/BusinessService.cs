using HelloPos.Kernel.Businesses;
using HelloPos.Web.ApiClient.Models;
using Microsoft.Kiota.Abstractions;
using ApiBusinessSettings = HelloPos.Web.ApiClient.Models.BusinessSettings;
using BusinessSettings = HelloPos.Kernel.Businesses.BusinessSettings;

namespace HelloPos.Web.Services;

/// <summary>A business in the owner's list.</summary>
public sealed record BusinessListItem(Guid Id, string Name, string CountryCode, string CurrencyCode);

/// <summary>The owner's businesses, or why they could not be loaded.</summary>
public abstract record BusinessListResult
{
    private BusinessListResult()
    {
    }

    public sealed record Loaded(IReadOnlyList<BusinessListItem> Businesses) : BusinessListResult;

    public sealed record Failed(string Message) : BusinessListResult;
}

/// <summary>The open business's settings, the business was switched in another tab, or a failure.</summary>
public abstract record CurrentBusinessResult
{
    private CurrentBusinessResult()
    {
    }

    public sealed record Loaded(Guid Id, BusinessSettings Settings) : CurrentBusinessResult;

    public sealed record BusinessChanged : CurrentBusinessResult;

    public sealed record Failed(string Message) : CurrentBusinessResult;
}

public interface IBusinessService
{
    Task<BusinessListResult> ListMineAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a business; field errors use the kernel's names (<c>cashRoundingStep</c>, ...).</summary>
    Task<OwnerActionResult> CreateAsync(BusinessSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Opens one of the owner's businesses in this session (switching from any other).</summary>
    Task<OwnerActionResult> OpenAsync(Guid businessId, CancellationToken cancellationToken = default);

    /// <summary>The open business; <paramref name="pageBusiness"/> is the business this page was opened for.</summary>
    Task<CurrentBusinessResult> GetCurrentAsync(Guid pageBusiness, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> UpdateCurrentAsync(Guid pageBusiness, BusinessSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>Owner business calls (feature 005, US2) through the client generated from the contract.</summary>
public sealed partial class BusinessService(HttpClient http, ILogger<BusinessService> logger) : IBusinessService
{
    /// <summary>Stale-tab guard header: the business this page was opened for (never selects it).</summary>
    public const string BusinessHeader = "X-HelloPos-Business";

    /// <summary>Minor units per currency unit; INR, USD and CAD all have two decimals.</summary>
    private const decimal MinorPerUnit = 100m;

    public async Task<BusinessListResult> ListMineAsync(CancellationToken cancellationToken = default)
    {
        if (http.BaseAddress is null)
        {
            return new BusinessListResult.Failed(OwnerAccountService.Unreachable);
        }

        try
        {
            var businesses = await ApiClients.Create(http).Owner.Businesses.GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false) ?? [];
            return new BusinessListResult.Loaded(businesses
                .Where(b => b.Id is not null)
                .Select(b => new BusinessListItem(b.Id!.Value, b.Name ?? string.Empty, b.CountryCode?.ToString() ?? string.Empty, b.CurrencyCode ?? string.Empty))
                .ToList());
        }
        catch (ProblemDetails problem)
        {
            return new BusinessListResult.Failed(problem.Status == 401
                ? "Your session has ended. Sign in again."
                : problem.Title ?? "Your businesses could not be loaded.");
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            LogFailed(logger, "list", ex);
            return new BusinessListResult.Failed(OwnerAccountService.Unreachable);
        }
    }

    public async Task<OwnerActionResult> CreateAsync(BusinessSettings settings, CancellationToken cancellationToken = default)
    {
        var body = ToApi(settings);
        var result = await OwnerAccountService.RunAsync(http, logger, "create-business",
            () => ApiClients.Create(http).Owner.Businesses.PostAsync(body, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return KernelFieldNames(result);
    }

    public Task<OwnerActionResult> OpenAsync(Guid businessId, CancellationToken cancellationToken = default) =>
        OwnerAccountService.RunAsync(http, logger, "open-business",
            () => ApiClients.Create(http).Owner.Businesses[businessId].Open.PostAsync(cancellationToken: cancellationToken));

    public async Task<CurrentBusinessResult> GetCurrentAsync(Guid pageBusiness, CancellationToken cancellationToken = default)
    {
        if (http.BaseAddress is null)
        {
            return new CurrentBusinessResult.Failed(OwnerAccountService.Unreachable);
        }

        try
        {
            var business = await ApiClients.Create(http).Business
                .GetAsync(c => c.Headers.Add(BusinessHeader, pageBusiness.ToString()), cancellationToken)
                .ConfigureAwait(false);
            return business is { Id: { } id }
                ? new CurrentBusinessResult.Loaded(id, FromApi(business))
                : new CurrentBusinessResult.Failed("The business could not be loaded.");
        }
        catch (ProblemDetails problem) when (problem.Type == OwnerAccountService.BusinessChangedType)
        {
            return new CurrentBusinessResult.BusinessChanged();
        }
        catch (ProblemDetails problem)
        {
            return new CurrentBusinessResult.Failed(problem.Status == 401
                ? "No business is open, or your session has ended."
                : problem.Title ?? "The business could not be loaded.");
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            LogFailed(logger, "get-current", ex);
            return new CurrentBusinessResult.Failed(OwnerAccountService.Unreachable);
        }
    }

    public async Task<OwnerActionResult> UpdateCurrentAsync(Guid pageBusiness, BusinessSettings settings, CancellationToken cancellationToken = default)
    {
        var body = ToApi(settings);
        var result = await OwnerAccountService.RunAsync(http, logger, "update-business",
            () => ApiClients.Create(http).Business.PutAsync(body, c => c.Headers.Add(BusinessHeader, pageBusiness.ToString()), cancellationToken))
            .ConfigureAwait(false);
        return KernelFieldNames(result);
    }

    private static ApiBusinessSettings ToApi(BusinessSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new ApiBusinessSettings
        {
            Name = settings.Name,
            CountryCode = Enum.Parse<BusinessSettings_countryCode>(settings.CountryCode),
            CurrencyCode = settings.CurrencyCode,
            PricingMode = settings.PricingMode == PricingMode.TaxInclusive
                ? BusinessSettings_pricingMode.Tax_inclusive
                : BusinessSettings_pricingMode.Tax_exclusive,
            FinancialYearStartMonth = settings.FinancialYearStartMonth,
            TimeZone = settings.TimeZone,
            CashRoundingStepMinor = (long)(settings.CashRoundingStep * MinorPerUnit),
        };
    }

    private static BusinessSettings FromApi(Business business) => new(
        business.Name ?? string.Empty,
        business.CountryCode?.ToString() ?? string.Empty,
        business.CurrencyCode ?? string.Empty,
        business.PricingMode == Business_pricingMode.Tax_inclusive ? PricingMode.TaxInclusive : PricingMode.TaxExclusive,
        business.FinancialYearStartMonth ?? 1,
        business.TimeZone ?? string.Empty,
        (business.CashRoundingStepMinor ?? 0) / MinorPerUnit);

    /// <summary>The API names the rounding field by its wire form; the form uses the kernel's name.</summary>
    private static OwnerActionResult KernelFieldNames(OwnerActionResult result) =>
        result is OwnerActionResult.FieldErrors fields
            ? new OwnerActionResult.FieldErrors(fields.Errors.ToDictionary(
                e => e.Key == "cashRoundingStepMinor" ? "cashRoundingStep" : e.Key, e => e.Value, StringComparer.Ordinal))
            : result;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Business {Operation} request failed")]
    private static partial void LogFailed(ILogger logger, string operation, Exception exception);
}
