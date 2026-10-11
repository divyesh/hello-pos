using Npgsql;

namespace HelloPos.Api.Infrastructure.Persistence;

/// <summary>Settings every Hello POS connection needs, whatever its source (API, probe, migrator).</summary>
public static class ConnectionStrings
{
    /// <summary>
    /// Kerberos/GSS is never used. Npgsql tries GSS encryption by default, which logs a missing
    /// <c>libgssapi_krb5</c> error in the chiseled image on every connection; turning it off removes
    /// the noise without changing behaviour (TLS settings are unaffected).
    /// </summary>
    public static string Normalize(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString) { GssEncryptionMode = GssEncryptionMode.Disable }
            .ConnectionString;
}
