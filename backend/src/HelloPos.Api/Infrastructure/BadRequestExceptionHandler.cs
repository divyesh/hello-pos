using Microsoft.AspNetCore.Diagnostics;

namespace HelloPos.Api.Infrastructure;

/// <summary>
/// A request the framework cannot read (malformed JSON, wrong content type) is the client's mistake:
/// answer with its status (usually <c>400</c>) as problem details, never <c>500</c>. In Development
/// minimal APIs throw <see cref="BadHttpRequestException"/> instead of answering, so this keeps every
/// environment the same.
/// </summary>
public sealed partial class BadRequestExceptionHandler(
    IProblemDetailsService problemDetails, ILogger<BadRequestExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (exception is not BadHttpRequestException bad)
        {
            return false;
        }

        LogRejected(logger, bad.StatusCode);
        httpContext.Response.StatusCode = bad.StatusCode;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = { Status = bad.StatusCode, Title = "The request could not be read." },
        }).ConfigureAwait(false);
    }

    // Never logs the body or the exception message: request bodies carry passwords and tokens.
    [LoggerMessage(Level = LogLevel.Information, Message = "Unreadable request rejected with {Status}")]
    private static partial void LogRejected(ILogger logger, int status);
}
