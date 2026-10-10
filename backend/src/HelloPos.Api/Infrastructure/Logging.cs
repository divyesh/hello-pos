using Serilog;
using Serilog.Formatting.Compact;

namespace HelloPos.Api.Infrastructure;

/// <summary>
/// Serilog setup (research R14). Console: compact JSON, one event per line, for the container
/// engine to collect. Seq: only when <c>Seq:ServerUrl</c> is configured. No file sinks.
/// Feature code logs through <see cref="ILogger{T}"/>, never through Serilog types.
/// </summary>
public static class Logging
{
    public const string ApplicationName = "hello-pos-api";

    public static bool IsSeqEnabled(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["Seq:ServerUrl"]);

    public static WebApplicationBuilder AddHelloPosLogging(this WebApplicationBuilder builder)
    {
        var environmentName = builder.Environment.EnvironmentName;

        builder.Services.AddSerilog((services, logger) =>
        {
            var configuration = services.GetRequiredService<IConfiguration>();

            logger
                .ReadFrom.Configuration(configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", ApplicationName)
                .Enrich.WithProperty("Environment", environmentName)
                .WriteTo.Console(new CompactJsonFormatter());

            if (IsSeqEnabled(configuration))
            {
                logger.WriteTo.Seq(configuration["Seq:ServerUrl"]!, apiKey: configuration["Seq:ApiKey"]);
            }
        });

        return builder;
    }

    /// <summary>One summary event per request: method, path, status code, elapsed time.</summary>
    public static IApplicationBuilder UseHelloPosRequestLogging(this IApplicationBuilder app) =>
        app.UseSerilogRequestLogging();
}
