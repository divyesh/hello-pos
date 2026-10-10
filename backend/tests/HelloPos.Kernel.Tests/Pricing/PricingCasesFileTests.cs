using System.Text.Json;
using Json.Schema;

namespace HelloPos.Kernel.Tests.Pricing;

/// <summary>
/// FR-014 / SC-005: the shared pricing test cases (Constitution, Principle I) are read by this suite and
/// validated against their schema. The Kotlin suite runs the same checks on the same files.
/// </summary>
public sealed class PricingCasesFileTests
{
    private static readonly string ContractsDir = Path.Combine(FindRepositoryRoot(), "contracts");
    private static readonly string CasesPath = Path.Combine(ContractsDir, "pricing-cases.json");
    private static readonly string SchemaPath = Path.Combine(ContractsDir, "pricing-cases.schema.json");

    // Loaded once: JsonSchema.Net registers schemas globally by $id and refuses to register one twice.
    private static readonly Lazy<JsonSchema> Schema = new(() => JsonSchema.FromText(File.ReadAllText(SchemaPath)));

    [Fact]
    public void PricingCasesFile_ExistsAndMatchesSchema()
    {
        Assert.True(File.Exists(CasesPath), $"Missing shared pricing cases: {CasesPath}");

        var violations = Validate(File.ReadAllText(CasesPath));

        Assert.True(violations.Count == 0,
            $"{CasesPath} does not match {SchemaPath}:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void MalformedCase_IsRejected()
    {
        const string malformed = """{ "version": 1, "cases": [ { "id": "Bad Id" } ] }""";

        Assert.NotEmpty(Validate(malformed));
    }

    private static List<string> Validate(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = Schema.Value.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });

        return result.IsValid
            ? []
            : (result.Details ?? [])
                .Where(d => d.Errors is { Count: > 0 })
                .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}"))
                .ToList();
    }

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "global.json")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (the folder holding global.json) not found.");
    }
}
