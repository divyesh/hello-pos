using HelloPos.Api.Features.Health.GetHealth;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Database;

// Composition only: services, middleware and feature mapping. Features live in Features/.
var builder = WebApplication.CreateBuilder(args);

builder.AddHelloPosLogging();
builder.Services.AddHelloPosProblemDetails();
builder.Services.AddHelloPosCors(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHelloPosDatabase(builder.Configuration);

var app = builder.Build();

app.UseHelloPosRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();

app.MapGetHealth();

app.Run();

/// <summary>Entry point, public so integration tests can host the API.</summary>
public partial class Program;
