namespace HelloPos.Api.Infrastructure.Tenancy;

public static class RequireCurrentBusinessExtensions
{
    /// <summary>
    /// Every endpoint (or group) that touches business-owned data declares this: without a current
    /// business the request is answered <c>401</c> before any data access (spec FR-008).
    /// </summary>
    public static TBuilder RequireCurrentBusiness<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(static async (context, next) =>
            context.HttpContext.RequestServices.GetRequiredService<ICurrentBusiness>().BusinessId is null
                ? TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized)
                : await next(context).ConfigureAwait(false));
}
