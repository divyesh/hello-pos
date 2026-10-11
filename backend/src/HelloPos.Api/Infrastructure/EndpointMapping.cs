namespace HelloPos.Api.Infrastructure;

/// <summary>A feature slice that maps its own endpoints; every registered mapper is mapped at startup.</summary>
public interface IEndpointMapper
{
    void Map(IEndpointRouteBuilder app);
}

public static class EndpointMapping
{
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
