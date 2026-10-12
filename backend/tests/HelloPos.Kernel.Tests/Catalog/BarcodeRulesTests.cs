using System.Text.Json;
using System.Text.Json.Nodes;
using HelloPos.Kernel.Catalog;
using HelloPos.Kernel.Tests.Pricing;
using Json.Schema;

namespace HelloPos.Kernel.Tests.Catalog;

/// <summary>Feature #11 research R4: every case in <c>contracts/barcode-cases.json</c> (shared with the Android scanner later).</summary>
public sealed class BarcodeRulesTests
{
    private static readonly string CasesPath = Path.Combine(PricingCasesFileTests.ContractsDir, "barcode-cases.json");
    private static readonly Lazy<JsonArray> Cases = new(() => JsonNode.Parse(File.ReadAllText(CasesPath))!["cases"]!.AsArray());

    public static TheoryData<string> Inputs()
    {
        var data = new TheoryData<string>();
        foreach (var item in Cases.Value)
        {
            data.Add((string)item!["input"]!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Inputs))]
    public void SharedCase(string input)
    {
        var testCase = Cases.Value.First(c => (string)c!["input"]! == input)!;
        var (barcode, problem) = BarcodeRules.Parse(input);

        if ((bool)testCase["valid"]!)
        {
            Assert.Null(problem);
            Assert.Equal((string)testCase["code"]!, barcode!.Code);
            Assert.Equal((string)testCase["kind"]!, barcode.Kind.ToString().ToLowerInvariant());
        }
        else
        {
            Assert.Null(barcode);
            Assert.Equal((string)testCase["reason"]!, JsonNamingPolicy.CamelCase.ConvertName(problem!.Value.ToString()));
        }
    }

    [Fact]
    public void CasesFile_MatchesItsSchema_AndInputsAreUnique()
    {
        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(PricingCasesFileTests.ContractsDir, "barcode-cases.schema.json")));
        using var document = JsonDocument.Parse(File.ReadAllText(CasesPath));

        Assert.True(schema.Evaluate(document.RootElement).IsValid);
        Assert.Equal(Cases.Value.Count, Cases.Value.Select(c => (string)c!["input"]!).Distinct().Count());
    }

    [Fact]
    public void EveryProblem_HasAMessage()
    {
        foreach (var problem in Enum.GetValues<BarcodeProblem>())
        {
            Assert.False(string.IsNullOrWhiteSpace(BarcodeRules.Message(problem)));
        }
    }
}
