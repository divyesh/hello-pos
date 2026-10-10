namespace HelloPos.Api.Infrastructure;

/// <summary>Browser origins allowed to call the API (the web back office), from <c>Cors:AllowedOrigins</c>.</summary>
public static class Cors
{
    public static IServiceCollection AddHelloPosCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        return services.AddCors(options => options.AddDefaultPolicy(policy =>
        {
            if (origins.Length > 0)
            {
                policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
            }
        }));
    }
}
