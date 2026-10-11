using System.Security.Claims;
using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace HelloPos.Api.Infrastructure.Identity;

/// <summary>Adds claim <c>owner_id</c> (read by <see cref="ClaimsCurrentOwner"/>) to the owner's session.</summary>
public sealed class OwnerClaimsPrincipalFactory(UserManager<OwnerAccount> userManager, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<OwnerAccount>(userManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(OwnerAccount user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var identity = await base.GenerateClaimsAsync(user).ConfigureAwait(false);
        identity.AddClaim(new Claim(ClaimsCurrentOwner.ClaimType, user.Id.ToString()));
        return identity;
    }
}
