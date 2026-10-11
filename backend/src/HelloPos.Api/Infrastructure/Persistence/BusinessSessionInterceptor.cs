using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace HelloPos.Api.Infrastructure.Persistence;

/// <summary>
/// Tells PostgreSQL which business the session acts for, so row-level security applies to every
/// statement, including raw SQL (spec FR-014, research R6). Set when EF opens a connection, reset when
/// it closes it; Npgsql's pool additionally runs <c>DISCARD ALL</c> when a connection is returned, so
/// a business can never survive into another request (SC-008). No business means an empty setting,
/// which the policies treat as "no rows".
/// </summary>
public sealed class BusinessSessionInterceptor : DbConnectionInterceptor
{
    public static readonly BusinessSessionInterceptor Instance = new();

    private const string SetSql = $"SELECT set_config('{RowSecurityMigrationExtensions.SessionSetting}', @business, false)";
    private const string ResetSql = $"RESET {RowSecurityMigrationExtensions.SessionSetting}";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        using var command = SetCommand(connection, eventData);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        await using var command = SetCommand(connection, eventData);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public override InterceptionResult ConnectionClosing(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State == System.Data.ConnectionState.Open)
        {
            using var command = new NpgsqlCommand(ResetSql, (NpgsqlConnection)connection);
            command.ExecuteNonQuery();
        }

        return result;
    }

    public override async ValueTask<InterceptionResult> ConnectionClosingAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State == System.Data.ConnectionState.Open)
        {
            await using var command = new NpgsqlCommand(ResetSql, (NpgsqlConnection)connection);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        return result;
    }

    private static NpgsqlCommand SetCommand(DbConnection connection, ConnectionEndEventData eventData)
    {
        var business = (eventData.Context as HelloPosDbContext)?.CurrentBusinessId;
        var command = new NpgsqlCommand(SetSql, (NpgsqlConnection)connection);
        command.Parameters.AddWithValue("business", business?.ToString() ?? string.Empty);
        return command;
    }
}
