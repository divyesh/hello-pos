using HelloPos.Api.Tests.Contract;

namespace HelloPos.Api.Tests.Persistence;

/// <summary>FR-009: the business scope can only be skipped through the named, reviewable opt-out.</summary>
public sealed class FilterBypassSourceTests
{
    private const string Allowed = "QueryableExtensions.cs";

    [Fact]
    public void IgnoreQueryFilters_IsCalledOnlyInsideAcrossAllBusinesses()
    {
        var source = Path.Combine(OpenApiContract.RepositoryRoot, "backend", "src");

        var offenders = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.EndsWith(Allowed, StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("IgnoreQueryFilters(", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(OpenApiContract.RepositoryRoot, f))
            .ToList();

        Assert.True(offenders.Count == 0,
            "IgnoreQueryFilters bypasses the business scope. Use AcrossAllBusinesses() (needs review approval) in: "
            + string.Join(", ", offenders));
    }
}
