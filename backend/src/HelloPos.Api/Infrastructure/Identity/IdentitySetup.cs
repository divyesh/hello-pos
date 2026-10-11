using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using HelloPos.Api.Infrastructure.Database;
using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;

namespace HelloPos.Api.Infrastructure.Identity;

/// <summary>
/// Owner accounts and the web session (feature 005, research R1–R5): ASP.NET Core Identity over
/// <see cref="OwnerIdentityDbContext"/>, an HttpOnly same-site cookie, lockout, rate limits and token
/// lifespans. Session secrets never reach page scripts (Constitution VIII).
/// </summary>
public static class IdentitySetup
{
    public const string OwnerAuthRateLimit = "owner-auth";
    public const string CookieName = "hellopos_session";
    public const string SessionStartedClaim = "session_started";

    public static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(8);
    public static readonly TimeSpan AbsoluteTimeout = TimeSpan.FromDays(7);

    public static IServiceCollection AddOwnerIdentity(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddIdentityCore<OwnerAccount>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.User.AllowedUserNameCharacters = string.Empty; // user name = email, validated by the slice
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Tokens.EmailConfirmationTokenProvider = EmailConfirmationTokenProvider.ProviderName;
                options.Tokens.PasswordResetTokenProvider = PasswordResetTokenProvider.ProviderName;
            })
            .AddEntityFrameworkStores<OwnerIdentityDbContext>()
            .AddSignInManager<OwnerSignInManager>()
            .AddClaimsPrincipalFactory<OwnerClaimsPrincipalFactory>()
            .AddPasswordValidator<CommonPasswordValidator>()
            .AddTokenProvider<EmailConfirmationTokenProvider>(EmailConfirmationTokenProvider.ProviderName)
            .AddTokenProvider<PasswordResetTokenProvider>(PasswordResetTokenProvider.ProviderName);

        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<TimeProvider>((o, clock) => o.TimeProvider = clock);

        var dataProtection = services.AddDataProtection().SetApplicationName("hello-pos");
        if (!string.IsNullOrWhiteSpace(configuration.GetConnectionString(DatabaseSetup.ConnectionStringName)))
        {
            dataProtection.PersistKeysToDbContext<OwnerIdentityDbContext>();
        }

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme, options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = IdleTimeout;
                options.SlidingExpiration = true;
                options.Events = new CookieAuthenticationEvents
                {
                    OnValidatePrincipal = ValidateSessionAsync,
                    OnRedirectToLogin = context => StatusOnly(context, StatusCodes.Status401Unauthorized),
                    OnRedirectToAccessDenied = context => StatusOnly(context, StatusCodes.Status403Forbidden),
                };
            });
        services.AddAuthorization();

        var permitPerMinute = configuration.GetValue("RateLimits:OwnerAuthPerMinute", 10);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(OwnerAuthRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
            options.OnRejected = async (context, cancellationToken) =>
            {
                var problems = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problems.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails =
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many attempts. Wait a minute and try again.",
                    },
                }).ConfigureAwait(false);
            };
        });

        return services;
    }

    /// <summary>Stamps when a session starts, so it can be ended after <see cref="AbsoluteTimeout"/>.</summary>
    public static Claim SessionStarted(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return new Claim(SessionStartedClaim,
            clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Runs on every request with a session cookie: ends the session after <see cref="AbsoluteTimeout"/>,
    /// and when the account's security stamp has changed (password reset), so a reset ends every other
    /// session at once (FR-004a). One indexed lookup per request (research R5).
    /// </summary>
    private static async Task ValidateSessionAsync(CookieValidatePrincipalContext context)
    {
        var services = context.HttpContext.RequestServices;
        var clock = services.GetRequiredService<TimeProvider>();
        var principal = context.Principal;
        var started = principal?.FindFirst(SessionStartedClaim)?.Value;
        var valid = long.TryParse(started, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            && clock.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(seconds) <= AbsoluteTimeout;

        if (valid)
        {
            var users = services.GetRequiredService<UserManager<OwnerAccount>>();
            var account = principal!.FindFirst(ClaimsCurrentOwner.ClaimType)?.Value is { } id
                ? await users.FindByIdAsync(id).ConfigureAwait(false)
                : null;
            var stamp = principal.FindFirst(users.Options.ClaimsIdentity.SecurityStampClaimType)?.Value;
            valid = account is not null
                && stamp is not null
                && string.Equals(stamp, await users.GetSecurityStampAsync(account).ConfigureAwait(false), StringComparison.Ordinal);
        }

        if (!valid)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme).ConfigureAwait(false);
        }
    }

    private static Task StatusOnly(RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        context.Response.StatusCode = status;
        return Task.CompletedTask;
    }
}
