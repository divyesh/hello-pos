namespace HelloPos.Api.Infrastructure.Tenancy;

/// <summary>
/// The signed-in owner account (feature 005). Lets the database show an owner the businesses they own
/// before one is opened (research R6). Comes only from the authenticated session.
/// </summary>
public interface ICurrentOwner
{
    Guid? OwnerId { get; }
}

/// <summary>Reads the <c>owner_id</c> claim of the authenticated user.</summary>
public sealed class ClaimsCurrentOwner(IHttpContextAccessor httpContextAccessor) : ICurrentOwner
{
    public const string ClaimType = "owner_id";

    public Guid? OwnerId
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

/// <summary>No signed-in owner: migrations, tooling, staff and device sessions.</summary>
public sealed class NoCurrentOwner : ICurrentOwner
{
    public static readonly NoCurrentOwner Instance = new();

    public Guid? OwnerId => null;
}
