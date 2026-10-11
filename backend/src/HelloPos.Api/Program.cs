using HelloPos.Api.Features.Health.GetHealth;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Database;
using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Identity.Email;
using HelloPos.Api.Infrastructure.Persistence;

// `dotnet HelloPos.Api.dll migrate`: apply the schema and exit (never starts the web host).
if (MigrateCommand.IsRequested(args))
{
    return await MigrateCommand.RunAsync(args[1..]).ConfigureAwait(false);
}

// Composition only: services, middleware and feature mapping. Features live in Features/.
var builder = WebApplication.CreateBuilder(args);

builder.AddHelloPosLogging();
builder.Services.AddHelloPosProblemDetails();
builder.Services.AddHelloPosCors(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHelloPosDatabase(builder.Configuration);
builder.Services.AddOwnerIdentity(builder.Configuration, builder.Environment);
builder.Services.AddOwnerEmail(builder.Configuration);
builder.Services.AddEndpointMappers(typeof(Program).Assembly);
builder.Services.AddExceptionHandler<PersistenceExceptionHandler>();

var app = builder.Build();

app.UseHelloPosRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGetHealth();
app.MapRegisteredEndpoints();

await app.RunAsync().ConfigureAwait(false);
return 0;

/// <summary>Entry point, public so integration tests can host the API.</summary>
public partial class Program;
