using System.Text.Json;
using System.Text.Json.Nodes;
using HelloPos.Api.Features.Staff;
using HelloPos.Api.Tests.Contract;
using Microsoft.AspNetCore.Identity;

namespace HelloPos.Api.Tests.Features.Pos;

/// <summary>
/// Feature 007 research R10: <c>contracts/pin-hash-cases.json</c> holds PINs and the hashes the API stores
/// for them. The Android app verifies PINs offline against the same hashes, so both suites check this
/// file and the two implementations cannot drift. Regenerate with <c>HELLOPOS_WRITE_PIN_VECTORS=1</c>.
/// </summary>
public sealed class PinHashVectorTests
{
    private static readonly string FilePath = Path.Combine(OpenApiContract.RepositoryRoot, "contracts", "pin-hash-cases.json");

    [Fact]
    public void EveryVector_Verifies_AndItsWrongPinsDoNot()
    {
        WriteIfRequested();
        var hasher = new PasswordHasher<StaffMember>();
        var person = new StaffMember { DisplayName = "Vector" };
        var cases = JsonNode.Parse(File.ReadAllText(FilePath))!["cases"]!.AsArray();

        Assert.True(cases.Count >= 6, "the shared file must hold several cases");
        foreach (var vector in cases)
        {
            var hash = (string)vector!["hash"]!;
            Assert.NotEqual(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(person, hash, (string)vector["pin"]!));
            foreach (var wrong in vector["wrongPins"]!.AsArray())
            {
                Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(person, hash, (string)wrong!));
            }
        }
    }

    private static void WriteIfRequested()
    {
        if (Environment.GetEnvironmentVariable("HELLOPOS_WRITE_PIN_VECTORS") != "1")
        {
            return;
        }

        var hasher = new PasswordHasher<StaffMember>();
        var person = new StaffMember { DisplayName = "Vector" };
        string[][] pins =
        [
            ["4826", "4827", "6284", "48260"],
            ["1357", "7531", "1358", "135"],
            ["90715", "90716", "9071", "907150"],
            ["593718", "593719", "59371", "000000"],
            ["0429", "429", "04290", "4029"],
            ["700001", "700000", "70001", "100007"],
        ];
        var document = new JsonObject
        {
            ["description"] = "PINs and their ASP.NET Core Identity v3 hashes (PBKDF2, header-declared PRF and iterations). " +
                "Both the API (.NET) and the Android app (Kotlin) must verify every pin and reject every wrongPins entry. " +
                "Test data only: these PINs are not used anywhere.",
            ["cases"] = new JsonArray(pins.Select(p => (JsonNode)new JsonObject
            {
                ["pin"] = p[0],
                ["hash"] = hasher.HashPassword(person, p[0]),
                ["wrongPins"] = new JsonArray(p[1..].Select(w => (JsonNode)JsonValue.Create(w)!).ToArray()),
            }).ToArray()),
        };
        File.WriteAllText(FilePath, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
    }
}
