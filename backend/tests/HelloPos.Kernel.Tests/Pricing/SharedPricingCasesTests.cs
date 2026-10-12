using System.Globalization;
using System.Text.Json.Nodes;
using HelloPos.Kernel.Pricing;

namespace HelloPos.Kernel.Tests.Pricing;

/// <summary>
/// Runs every case of <c>contracts/pricing-cases.json</c> through the .NET engine (Constitution I, VI;
/// feature #10 FR-004). The Kotlin suite runs the same file; both must pass every case.
/// </summary>
public sealed class SharedPricingCasesTests
{
    private static readonly Lazy<JsonArray> Cases = new(() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(PricingCasesFileTests.ContractsDir, "pricing-cases.json")))!["cases"]!.AsArray());

    public static TheoryData<string> CaseIds()
    {
        var data = new TheoryData<string>();
        foreach (var item in Cases.Value)
        {
            data.Add((string)item!["id"]!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void SharedCase(string id)
    {
        var testCase = Cases.Value.Single(c => (string)c!["id"]! == id)!;
        var result = PricingEngine.Price(ToInput(testCase["input"]!));

        if (testCase["expectedError"] is { } expectedError)
        {
            var refused = Assert.IsType<PricingResult.Refused>(result);
            var code = (string)expectedError["code"]!;
            var lineId = (string?)expectedError["lineId"];
            Assert.True(
                refused.Problems.Any(p => p.Code == code && (lineId is null || p.LineId == lineId)),
                $"{id}: expected refusal {code}{(lineId is null ? "" : $" on line {lineId}")}, got {string.Join(", ", refused.Problems.Select(p => $"{p.Code}/{p.LineId}/{p.Field}"))}");
            return;
        }

        if (result is PricingResult.Refused rejected)
        {
            Assert.Fail($"{id}: refused: {string.Join(", ", rejected.Problems.Select(p => $"{p.Code}/{p.LineId}/{p.Field}"))}");
        }

        var actual = ToJson(((PricingResult.Priced)result).Sale);
        var expected = testCase["expected"]!.AsObject();
        var differences = Differences(id, expected, actual).ToList();
        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
    }

    [Fact]
    public void EveryCaseInTheFile_IsRun_AndIdsAreUnique()
    {
        var ids = Cases.Value.Select(c => (string)c!["id"]!).ToList();
        Assert.Equal(ids.Count, CaseIds().Count);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    internal static SaleInput ToInput(JsonNode input) => new(
        (string)input["currency"]!,
        (string)input["pricing"]! == "taxInclusive" ? PricingMode.TaxInclusive : PricingMode.TaxExclusive,
        input["lines"]!.AsArray().Select(l => ToLine(l!)).ToList())
    {
        CashRoundingStep = (long?)input["cashRoundingStep"] ?? 0,
        DepositsTaxable = (bool?)input["depositsTaxable"] ?? false,
        OrderDiscount = ToDiscount(input["orderDiscount"]),
        Tip = input["tip"] is { } tip
            ? new Tip((long?)tip["amount"], tip["percentOfSubtotal"] is { } p ? Dec((string)p!) : null)
            : null,
        Payments = (input["payments"]?.AsArray() ?? []).Select(p => new PaymentInput(
            (string)p!["method"]! switch { "cash" => PaymentMethod.Cash, "card" => PaymentMethod.Card, _ => PaymentMethod.Other },
            (long?)p["amount"],
            (long?)p["tendered"])).ToList(),
    };

    private static LineInput ToLine(JsonNode line) => new(
        (string)line["id"]!,
        (long)line["unitPrice"]!,
        Dec((string)line["quantity"]!),
        ToProfile(line["taxProfile"]!))
    {
        OriginalUnitPrice = (long?)line["originalUnitPrice"],
        OpenPrice = (bool?)line["openPrice"] ?? false,
        Unit = (string?)line["unit"] ?? "piece",
        MinQuantity = line["minQuantity"] is { } min ? Dec((string)min!) : 1m,
        Step = line["step"] is { } step ? Dec((string)step!) : 1m,
        ItemDiscount = ToDiscount(line["itemDiscount"]),
        DepositPerUnit = (long?)line["depositPerUnit"] ?? 0,
    };

    private static TaxProfile ToProfile(JsonNode profile) => new(
        (string)profile["name"]!,
        (string)profile["kind"]! switch { "zeroRated" => TaxKind.ZeroRated, "exempt" => TaxKind.Exempt, _ => TaxKind.Taxable },
        profile["components"]!.AsArray().Select(c => new TaxComponent((string)c!["label"]!, Dec((string)c["ratePercent"]!))).ToList());

    private static Discount? ToDiscount(JsonNode? discount) => discount is null
        ? null
        : (string)discount["kind"]! == "percent"
            ? new Discount(DiscountKind.Percent, Dec((string)discount["value"]!))
            : new Discount(DiscountKind.Fixed, (long)discount["value"]!);

    private static decimal Dec(string value) => decimal.Parse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    private static string Rate(decimal rate) => rate.ToString("0.############", CultureInfo.InvariantCulture);

    internal static JsonObject ToJson(PricedSale sale)
    {
        var json = new JsonObject
        {
            ["lines"] = new JsonArray(sale.Lines.Select(l => (JsonNode)new JsonObject
            {
                ["id"] = l.Id,
                ["lineAmount"] = l.LineAmount,
                ["itemDiscount"] = l.ItemDiscount,
                ["orderDiscountShare"] = l.OrderDiscountShare,
                ["taxable"] = l.Taxable,
                ["deposit"] = l.Deposit,
            }).ToArray()),
            ["taxes"] = new JsonArray(sale.Taxes.Select(t =>
            {
                var row = new JsonObject { ["label"] = t.Label, ["ratePercent"] = Rate(t.RatePercent) };
                if (t.Profile is not null)
                {
                    row["profile"] = t.Profile;
                }

                row["base"] = t.Base;
                row["amount"] = t.Amount;
                return (JsonNode)row;
            }).ToArray()),
            ["baseByKind"] = new JsonObject
            {
                ["taxable"] = sale.BaseByKind.Taxable,
                ["zeroRated"] = sale.BaseByKind.ZeroRated,
                ["exempt"] = sale.BaseByKind.Exempt,
            },
            ["subtotal"] = sale.Subtotal,
            ["taxTotal"] = sale.TaxTotal,
            ["tip"] = sale.Tip,
            ["total"] = sale.Total,
        };
        if (sale.CashDue is { } cashDue)
        {
            json["cashDue"] = cashDue;
        }

        json["payments"] = new JsonArray(sale.Payments.Select(p => (JsonNode)new JsonObject
        {
            ["method"] = p.Method.ToString().ToLowerInvariant(),
            ["applied"] = p.Applied,
            ["rounding"] = p.Rounding,
            ["change"] = p.Change,
        }).ToArray());
        json["cashRounding"] = sale.CashRounding;
        json["change"] = sale.Change;
        json["remaining"] = sale.Remaining;
        return json;
    }

    /// <summary>Every field that differs, named by its path; rates compare as numbers ("13" = "13.0").</summary>
    private static IEnumerable<string> Differences(string id, JsonObject expected, JsonObject actual)
    {
        Normalize(expected);
        foreach (var key in expected.Select(p => p.Key).Union(actual.Select(p => p.Key)))
        {
            if (!JsonNode.DeepEquals(expected[key], actual[key]))
            {
                yield return $"{id}: {key} expected {expected[key]?.ToJsonString() ?? "(absent)"} but was {actual[key]?.ToJsonString() ?? "(absent)"}";
            }
        }
    }

    private static void Normalize(JsonObject expected)
    {
        foreach (var row in expected["taxes"]?.AsArray() ?? [])
        {
            row!["ratePercent"] = Rate(Dec((string)row["ratePercent"]!));
        }
    }
}
