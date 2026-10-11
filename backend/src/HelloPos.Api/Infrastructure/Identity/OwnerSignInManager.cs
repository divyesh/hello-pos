using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace HelloPos.Api.Infrastructure.Identity;

/// <summary>
/// Owners have only the application cookie: no external-login or two-factor cookies (yet). Identity's
/// default sign-out also signs out of those schemes, which are not registered and would throw; this
/// signs out of the application cookie only. (Session revalidation lives in <see cref="IdentitySetup"/>.)
/// </summary>
public sealed class OwnerSignInManager(
    UserManager<OwnerAccount> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<OwnerAccount> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<OwnerAccount>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<OwnerAccount> confirmation)
    : SignInManager<OwnerAccount>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override Task SignOutAsync() => Context.SignOutAsync(IdentityConstants.ApplicationScheme);
}
