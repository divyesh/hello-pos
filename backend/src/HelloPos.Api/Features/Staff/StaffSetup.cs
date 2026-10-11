using Microsoft.AspNetCore.Identity;

namespace HelloPos.Api.Features.Staff;

public static class StaffSetup
{
    /// <summary>Staff services (feature 006): PIN hashing and credentials.</summary>
    public static IServiceCollection AddStaff(this IServiceCollection services)
    {
        services.AddSingleton<IPasswordHasher<StaffMember>, PasswordHasher<StaffMember>>();
        services.AddScoped<StaffCredentials>();
        return services;
    }
}
