using HelloPos.Web;
using HelloPos.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();

// The API address comes from wwwroot/appsettings*.json (FR-011); these files hold no secrets.
var apiBaseUrl = builder.Configuration["Api:BaseUrl"];
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = string.IsNullOrWhiteSpace(apiBaseUrl) ? null : new Uri(apiBaseUrl),
});
builder.Services.AddScoped<IHealthService>(sp => new HealthService(
    sp.GetRequiredService<HttpClient>(),
    sp.GetRequiredService<ILogger<HealthService>>(),
    HealthService.DefaultTimeout));

await builder.Build().RunAsync().ConfigureAwait(false);
