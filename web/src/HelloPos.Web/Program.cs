using HelloPos.Web;
using HelloPos.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();

// The API address comes from wwwroot/appsettings*.json (FR-011); these files hold no secrets.
// Requests carry the HttpOnly session cookie and the CSRF header (feature 005).
var apiBaseUrl = builder.Configuration["Api:BaseUrl"];
builder.Services.AddScoped(_ => new HttpClient(new ApiHttpHandler { InnerHandler = new HttpClientHandler() })
{
    BaseAddress = string.IsNullOrWhiteSpace(apiBaseUrl) ? null : new Uri(apiBaseUrl),
});
builder.Services.AddScoped<IHealthService>(sp => new HealthService(
    sp.GetRequiredService<HttpClient>(),
    sp.GetRequiredService<ILogger<HealthService>>(),
    HealthService.DefaultTimeout));

builder.Services.AddScoped<IOwnerAccountService, OwnerAccountService>();
builder.Services.AddScoped<IBusinessService, BusinessService>();
builder.Services.AddScoped<OpenBusinessState>();
builder.Services.AddScoped<IStaffService, StaffService>();
builder.Services.AddScoped<OwnerAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<OwnerAuthenticationStateProvider>());
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();

await builder.Build().RunAsync().ConfigureAwait(false);
