using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace HelloPos.Api.Features.Owners;

/// <summary><c>POST /owner/sign-out</c> (FR-008): removes the session cookie.</summary>
public sealed class SignOut : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/owner/sign-out", HandleAsync)
            .WithName("ownerSignOut")
            .WithTags(OwnerEndpoints.Tag)
            .AllowAnonymous()
            .RequireClientHeader();

    public static async Task<NoContent> HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        await context.SignOutAsync(IdentityConstants.ApplicationScheme).ConfigureAwait(false);
        return TypedResults.NoContent();
    }
}
