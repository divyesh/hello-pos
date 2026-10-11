using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace HelloPos.Api.Tests.Tenancy.Samples;

/// <summary>
/// Creates the sample tables in a migrated test database (research R10): the DDL comes from the
/// difference between the production model and <see cref="SampleDbContext"/>'s model, so it is produced
/// by the same <c>ConfigureBusinessOwned()</c> helper real slices use; row-level security is added with
/// the same SQL real migrations use. Runs as the migrator (owner), like a real migration.
/// </summary>
public static class SampleSchema
{
    public static readonly string[] Tables = ["sample_categories", "sample_products"];

    public static async Task CreateAsync(string migratorConnectionString)
    {
        await using var production = new HelloPosDbContext(
            new DbContextOptionsBuilder<HelloPosDbContext>().UseHelloPosDatabase(migratorConnectionString).Options,
            NoCurrentBusiness.Instance);
        await using var sample = SampleDbContext.Create(migratorConnectionString, null);

        var differ = sample.GetService<IMigrationsModelDiffer>();
        var operations = differ.GetDifferences(
            production.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
            sample.GetService<IDesignTimeModel>().Model.GetRelationalModel());
        var commands = sample.GetService<IMigrationsSqlGenerator>().Generate(operations, sample.Model);

        await using var connection = new NpgsqlConnection(ConnectionStrings.Normalize(migratorConnectionString));
        await connection.OpenAsync();
        foreach (var sql in commands.Select(c => c.CommandText)
                     .Concat(Tables.Select(t => RowSecurityMigrationExtensions.EnableSql(t))))
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
