using HelloPos.Api.Infrastructure.Persistence;

namespace HelloPos.Api.Tests.Tenancy.Samples;

/// <summary>For assertions through the migrator (owner) connection, which sees every business.</summary>
internal static class TestQueryExtensions
{
    public static IQueryable<T> AcrossAllBusinessesForTests<T>(this IQueryable<T> query)
        where T : class => query.AcrossAllBusinesses();
}
