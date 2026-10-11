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
    public HelloPosDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("HELLOPOS_DESIGN_CONNECTION")
            ?? "Host=localhost;Database=hellopos_design";
        var options = new DbContextOptionsBuilder<HelloPosDbContext>().UseHelloPosDatabase(connectionString).Options;
        return new HelloPosDbContext(options, NoCurrentBusiness.Instance);
    }
}
