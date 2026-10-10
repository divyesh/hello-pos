namespace HelloPos.Kernel.Tests;

/// <summary>FR-012: the shared kernel depends on no web or UI framework and on no HelloPos app.</summary>
public sealed class KernelDependencyTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "Microsoft.AspNetCore",
        "Microsoft.JSInterop",
        "MudBlazor",
        "HelloPos.Api",
        "HelloPos.Web",
    ];

    [Fact]
    public void Kernel_ReferencesNoFrameworkOrAppAssemblies()
    {
        var references = typeof(KernelAssembly).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

        var forbidden = references
            .Where(name => ForbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        Assert.True(forbidden.Count == 0, $"HelloPos.Kernel must not reference: {string.Join(", ", forbidden)}");
    }
}
