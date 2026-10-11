using HelloPos.Api.Infrastructure.Persistence;
using Npgsql;

namespace HelloPos.Api.Tests.Persistence;

/// <summary>
/// The automated schema convention check (spec FR-020, research R11), run against a migrated
/// database's catalog. Every violation names the table (and column) and the rule. Later features'
/// tests can call it too.
/// </summary>
public static class SchemaConventions
{
    public const string TenantRoot = "businesses";

    private static readonly string[] Ignored = [HelloPosDbContext.MigrationsHistoryTable];

    /// <summary>Returns every violation in <paramref name="schema"/> (empty when the schema follows the rules).</summary>
    public static async Task<IReadOnlyList<string>> CheckAsync(string connectionString, string schema = HelloPosDbContext.Schema)
    {
        await using var connection = new NpgsqlConnection(ConnectionStrings.Normalize(connectionString));
        await connection.OpenAsync();
        var violations = new List<string>();

        var tables = (await QueryAsync(connection, schema,
                "select table_name from information_schema.tables where table_schema = @schema and table_type = 'BASE TABLE'",
                r => r.GetString(0)))
            .Where(t => !Ignored.Contains(t)).ToHashSet();
        var businessOwned = tables.Where(t => t != TenantRoot).ToHashSet();

        var columns = await QueryAsync(connection, schema, """
            select table_name, column_name, data_type, is_nullable = 'YES', numeric_precision, numeric_scale
            from information_schema.columns where table_schema = @schema
            """,
            r => (Table: r.GetString(0), Column: r.GetString(1), Type: r.GetString(2), Nullable: r.GetBoolean(3),
                  Precision: r.IsDBNull(4) ? (int?)null : r.GetInt32(4), Scale: r.IsDBNull(5) ? (int?)null : r.GetInt32(5)));

        var firstKeyColumn = (await QueryAsync(connection, schema, """
            select c.relname, a.attname
            from pg_index i
              join pg_class c on c.oid = i.indrelid
              join pg_namespace n on n.oid = c.relnamespace
              join pg_attribute a on a.attrelid = c.oid and a.attnum = i.indkey[0]
            where i.indisprimary and n.nspname = @schema
            """, r => (Table: r.GetString(0), Column: r.GetString(1))))
            .ToDictionary(x => x.Table, x => x.Column);

        var rowSecurity = (await QueryAsync(connection, schema, """
            select c.relname, c.relrowsecurity from pg_class c join pg_namespace n on n.oid = c.relnamespace
            where n.nspname = @schema and c.relkind = 'r'
            """, r => (Table: r.GetString(0), Enabled: r.GetBoolean(1))))
            .ToDictionary(x => x.Table, x => x.Enabled);

        var policies = (await QueryAsync(connection, schema,
                $"select tablename from pg_policies where schemaname = @schema and policyname = '{RowSecurityMigrationExtensions.PolicyName}'",
                r => r.GetString(0)))
            .ToHashSet();

        var foreignKeys = await QueryAsync(connection, schema, """
            select con.conname, src.relname, tgt.relname,
                   array(select a.attname from unnest(con.conkey) k join pg_attribute a on a.attrelid = con.conrelid and a.attnum = k)
            from pg_constraint con
              join pg_class src on src.oid = con.conrelid
              join pg_class tgt on tgt.oid = con.confrelid
              join pg_namespace n on n.oid = src.relnamespace
            where con.contype = 'f' and n.nspname = @schema
            """, r => (Name: r.GetString(0), From: r.GetString(1), To: r.GetString(2), Columns: (string[])r.GetValue(3)));

        foreach (var table in businessOwned.Order())
        {
            var businessId = columns.FirstOrDefault(c => c.Table == table && c.Column == "business_id");
            if (businessId.Column is null || businessId.Type != "uuid" || businessId.Nullable)
            {
                violations.Add($"{schema}.{table}: needs a non-null business_id uuid column [business-id]");
            }

            if (!firstKeyColumn.TryGetValue(table, out var first) || first != "business_id")
            {
                violations.Add($"{schema}.{table}: primary key must start with business_id [composite-key]");
            }
        }

        foreach (var fk in foreignKeys.Where(fk => fk.To != TenantRoot && businessOwned.Contains(fk.To) && !fk.Columns.Contains("business_id")))
        {
            violations.Add($"{schema}.{fk.From}: foreign key {fk.Name} to {fk.To} must include business_id [composite-link]");
        }

        foreach (var table in tables.Order())
        {
            if (!rowSecurity.GetValueOrDefault(table) || !policies.Contains(table))
            {
                violations.Add($"{schema}.{table}: row-level security with policy {RowSecurityMigrationExtensions.PolicyName} is required [row-security]");
            }

            var id = columns.FirstOrDefault(c => c.Table == table && c.Column == "id");
            if (id.Column is null || id.Type != "uuid")
            {
                violations.Add($"{schema}.{table}: needs an id uuid column (UUIDv7) [uuid-id]");
            }
        }

        foreach (var column in columns.Where(c => tables.Contains(c.Table)))
        {
            var where = $"{schema}.{column.Table}.{column.Column}";
            switch (column.Type)
            {
                case "timestamp without time zone":
                    violations.Add($"{where}: timestamps must be timestamptz [utc-time]");
                    break;
                case "real" or "double precision":
                    violations.Add($"{where}: floating point is not allowed; money is numeric(19,4) [exact-money]");
                    break;
                case "numeric" when column.Precision is null || column.Scale is null:
                    violations.Add($"{where}: numeric needs explicit precision and scale [exact-money]");
                    break;
            }
        }

        return violations;
    }

    private static async Task<List<T>> QueryAsync<T>(NpgsqlConnection connection, string schema, string sql, Func<NpgsqlDataReader, T> read)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schema", schema);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync())
        {
            rows.Add(read(reader));
        }

        return rows;
    }
}
