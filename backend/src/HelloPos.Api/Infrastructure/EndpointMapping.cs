using System.Reflection;

namespace HelloPos.Api.Infrastructure;

/// <summary>A feature slice that maps its own endpoints; every registered mapper is mapped at startup.</summary>
public interface IEndpointMapper
{
    void Map(IEndpointRouteBuilder app);
}

public static class EndpointMapping
{
    /// <summary>Registers every concrete <see cref="IEndpointMapper"/> in <paramref name="assembly"/>.</summary>
    public static IServiceCollection AddEndpointMappers(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        foreach (var type in assembly.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IEndpointMapper).IsAssignableFrom(t))
                     .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            services.AddSingleton(typeof(IEndpointMapper), type);
        }

        return services;
    }

    public static IEndpointRouteBuilder MapRegisteredEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        foreach (var mapper in app.ServiceProvider.GetServices<IEndpointMapper>())
        {
            mapper.Map(app);
        }

        return app;
    }
}
