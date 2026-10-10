using HelloPos.Api.Infrastructure;

// Composition only: services, middleware and feature mapping. Features live in Features/.
var builder = WebApplication.CreateBuilder(args);

builder.AddHelloPosLogging();
builder.Services.AddHelloPosProblemDetails();

var app = builder.Build();

app.UseHelloPosRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();

app.Run();

/// <summary>Entry point, public so integration tests can host the API.</summary>
public partial class Program;
