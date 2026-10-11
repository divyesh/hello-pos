namespace HelloPos.Api.Infrastructure.Identity;

public static class RequireClientHeaderExtensions
{
    public const string HeaderName = "X-HelloPos-Client";
    public const string WebClient = "web";

    /// <summary>
    /// CSRF guard for cookie-authenticated, state-changing endpoints (research R3): the request must
    /// carry <c>X-HelloPos-Client: web</c>. A cross-site form cannot set it, and a cross-site script
    /// cannot send it without a CORS preflight the API refuses for unknown origins.
    /// </summary>
    public static TBuilder RequireClientHeader<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(static async (context, next) =>
            string.Equals(context.HttpContext.Request.Headers[HeaderName], WebClient, StringComparison.Ordinal)
                ? await next(context).ConfigureAwait(false)
                : TypedResults.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: $"Header {HeaderName} is required."));
}
