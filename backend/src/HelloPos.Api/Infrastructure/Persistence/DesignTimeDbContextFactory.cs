using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HelloPos.Api.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build the model without starting the API. Creating migrations needs no
/// database; <c>HELLOPOS_DESIGN_CONNECTION</c> is only needed for commands that connect.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<HelloPosDbContext>
{
    internal static string ConnectionString =>
        Environment.GetEnvironmentVariable("HELLOPOS_DESIGN_CONNECTION") ?? "Host=localhost;Database=hellopos_design";

    public HelloPosDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HelloPosDbContext>().UseHelloPosDatabase(ConnectionString).Options;
        return new HelloPosDbContext(options, NoCurrentBusiness.Instance);
    }
}

/// <summary>Design-time factory for the owner identity context (<c>--context OwnerIdentityDbContext</c>).</summary>
public sealed class DesignTimeOwnerIdentityDbContextFactory : IDesignTimeDbContextFactory<OwnerIdentityDbContext>
{
    public OwnerIdentityDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<OwnerIdentityDbContext>()
            .UseOwnerIdentityDatabase(DesignTimeDbContextFactory.ConnectionString)
            .Options);
}
