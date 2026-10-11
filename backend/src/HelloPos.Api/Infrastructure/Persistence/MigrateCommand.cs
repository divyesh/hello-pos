namespace HelloPos.Api.Infrastructure.Persistence;

/// <summary>
/// <c>dotnet HelloPos.Api.dll migrate</c>: bootstrap roles and apply migrations, then exit (research R3).
/// Never starts the web host. Needs <c>ConnectionStrings:Admin</c>, <c>ConnectionStrings:Migrator</c>
/// and <c>ConnectionStrings:HelloPos</c> (to know the app login it grants rights to).
/// </summary>
public static partial class MigrateCommand
{
    public const string Name = "migrate";

    public static bool IsRequested(string[] args) => args is [Name, ..];

    public static async Task<int> RunAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddHelloPosLogging();
        await using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MigrateCommand));

        var configuration = host.Services.GetRequiredService<IConfiguration>();
        var admin = configuration.GetConnectionString("Admin");
        var migrator = configuration.GetConnectionString("Migrator");
        var app = configuration.GetConnectionString(Database.DatabaseSetup.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(admin) || string.IsNullOrWhiteSpace(migrator) || string.IsNullOrWhiteSpace(app))
        {
            LogMissingSettings(logger);
            return 2;
        }

        try
        {
            var runner = new Migrator(host.Services.GetRequiredService<ILogger<Migrator>>());
            var applied = await runner.RunAsync(new MigratorSettings(admin, migrator, app), CancellationToken.None)
                .ConfigureAwait(false);
            LogDone(logger, applied.Count);
            return 0;
        }
        catch (MigrationFailedException ex)
        {
            LogFailed(logger, ex);
            return 1;
        }
        catch (Npgsql.NpgsqlException ex)
        {
            LogFailed(logger, ex);
            return 1;
        }
    }

    [LoggerMessage(Level = LogLevel.Error,
        Message = "migrate needs ConnectionStrings:Admin, ConnectionStrings:Migrator and ConnectionStrings:HelloPos")]
    private static partial void LogMissingSettings(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migration finished: {Count} migration(s) applied")]
    private static partial void LogDone(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Migration failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
