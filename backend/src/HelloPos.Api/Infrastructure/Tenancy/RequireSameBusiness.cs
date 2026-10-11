namespace HelloPos.Api.Infrastructure.Tenancy;

public static class RequireSameBusinessExtensions
{
    public const string HeaderName = "X-HelloPos-Business";
    public const string BusinessChangedType = "urn:hellopos:problem:business-changed";

    /// <summary>
    /// Stale-tab guard (feature 005, research R7). A page sends <c>X-HelloPos-Business</c> with the
    /// business it was opened for; when the session has since opened another business (in another
    /// tab), the request is refused with <c>409</c> instead of acting for the wrong business. The header
    /// never selects the business: that comes only from the session (FR-013).
    /// </summary>
    public static TBuilder RequireSameBusiness<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(static async (context, next) =>
        {
            var header = context.HttpContext.Request.Headers[HeaderName].ToString();
            if (header.Length == 0)
            {
                return await next(context).ConfigureAwait(false);
            }

            var current = context.HttpContext.RequestServices.GetRequiredService<ICurrentBusiness>().BusinessId;
            return Guid.TryParse(header, out var page) && page == current
                ? await next(context).ConfigureAwait(false)
                : TypedResults.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    type: BusinessChangedType,
                    title: "Another business is open in this session now. Reload the page.");
        });
}
