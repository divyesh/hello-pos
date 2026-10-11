namespace HelloPos.Api.Infrastructure.Tenancy;

/// <summary>
/// The business the current request or job acts for. Comes only from the authenticated identity,
/// never from request bodies, routes, query strings or headers (spec FR-006).
/// </summary>
public interface ICurrentBusiness
{
    Guid? BusinessId { get; }
}

/// <summary>
/// Reads the <c>business_id</c> claim of the authenticated user. Until login exists (roadmap #7)
/// nothing authenticates requests, so this is always <c>null</c> in production (FR-007).
/// </summary>
public sealed class ClaimsCurrentBusiness(IHttpContextAccessor httpContextAccessor) : ICurrentBusiness
{
    public const string ClaimType = "business_id";

    public Guid? BusinessId
    {
        get
        {
            var user = httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            return Guid.TryParse(user.FindFirst(ClaimType)?.Value, out var id) ? id : null;
        }
    }
}

/// <summary>No current business: migrations, design-time tooling and work started without one.</summary>
public sealed class NoCurrentBusiness : ICurrentBusiness
{
    public static readonly NoCurrentBusiness Instance = new();

    public Guid? BusinessId => null;
}
