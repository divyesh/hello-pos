using System.Diagnostics;

namespace HelloPos.Api.Infrastructure;

/// <summary>
/// RFC 7807 errors whose <c>traceId</c> is the request's W3C trace id, the same value carried by
/// every log event for that request (Constitution, Principle IX; contract <c>ProblemDetails</c>).
/// </summary>
public static class ProblemDetailsSetup
{
    public static IServiceCollection AddHelloPosProblemDetails(this IServiceCollection services) =>
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["traceId"] =
                Activity.Current?.TraceId.ToHexString() ?? context.HttpContext.TraceIdentifier);
}
