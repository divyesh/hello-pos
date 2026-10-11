using HelloPos.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Tests.Persistence;

/// <summary>The EF model and the migrations stay in step: no model change without a migration.</summary>
public sealed class ModelSnapshotTests
{
    [Fact]
    public void Model_HasNoPendingChanges()
    {
        using var context = new DesignTimeDbContextFactory().CreateDbContext([]);

        Assert.False(context.Database.HasPendingModelChanges(),
            "The EF model has changes not captured in a migration. Add one with: scripts/dotnet.ps1 ef migrations add <Name> (see docs/tenancy.md).");
    }
}
