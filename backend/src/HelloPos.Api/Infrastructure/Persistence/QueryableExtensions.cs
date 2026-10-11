using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Infrastructure.Persistence;

public static class QueryableExtensions
{
    /// <summary>
    /// The only way to skip the business scope in application code (spec FR-009). Every use needs
    /// explicit review approval. It removes only the EF filter: row-level security still confines the
    /// app login to the current business, so real cross-business reads also need a platform database
    /// role that does not exist yet (research R6).
    /// </summary>
    public static IQueryable<T> AcrossAllBusinesses<T>(this IQueryable<T> query)
        where T : class => query.IgnoreQueryFilters([HelloPosDbContext.BusinessScopeFilter]);
}
