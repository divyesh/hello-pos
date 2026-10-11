using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HelloPos.Api.Infrastructure.Persistence;

/// <summary>
/// Turns tenancy and integrity failures into problem details (spec FR-012, FR-013):
/// a write referring to another business's record (composite foreign key, <c>23503</c>) or touching
/// another business's row (row-level security, <c>42501</c>) is answered exactly like an unknown id:
/// <c>404</c>, revealing nothing. No current business is <c>401</c>; a business change is <c>400</c>.
/// </summary>
public sealed partial class PersistenceExceptionHandler(
    IProblemDetailsService problemDetails, ILogger<PersistenceExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var status = exception switch
        {
            NoCurrentBusinessException => StatusCodes.Status401Unauthorized,
            BusinessReassignmentException => StatusCodes.Status400BadRequest,
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.InsufficientPrivilege } } => StatusCodes.Status404NotFound,
            PostgresException { SqlState: PostgresErrorCodes.InsufficientPrivilege } => StatusCodes.Status404NotFound,
            _ => 0,
        };
        if (status == 0)
        {
            return false;
        }

        LogRejected(logger, status, exception.GetType().Name);
        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = { Status = status },
        }).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Request rejected with {Status} by tenancy or integrity rules ({ExceptionType})")]
    private static partial void LogRejected(ILogger logger, int status, string exceptionType);
}
