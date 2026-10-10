using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;

namespace HelloPos.Api.Tests.Contract;

/// <summary>
/// The hand-written contract in <c>contracts/openapi.yaml</c> (Constitution, Principle V), loaded once
/// and exposed as JSON Schemas for each operation's responses (research R5).
/// </summary>
public static class OpenApiContract
{
    private static readonly Lazy<JsonObject> Document = new(Load);

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string ContractPath => Path.Combine(RepositoryRoot, "contracts", "openapi.yaml");

    /// <summary>JSON Schema for the response body of <paramref name="operationId"/> at <paramref name="statusCode"/>.</summary>
    public static JsonSchema ResponseSchema(string operationId, string statusCode, string mediaType = "application/json")
    {
        var operation = FindOperation(operationId);
        var schema = operation["responses"]?[statusCode]?["content"]?[mediaType]?["schema"]
            ?? throw new InvalidOperationException(
                $"Contract has no '{mediaType}' response {statusCode} for operation '{operationId}'.");

        // Wrap the response schema with the contract's component schemas so $refs resolve.
        var root = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$defs"] = Document.Value["components"]?["schemas"]?.DeepClone() ?? new JsonObject(),
            ["allOf"] = new JsonArray(schema.DeepClone()),
        };
        var text = root.ToJsonString().Replace("#/components/schemas/", "#/$defs/", StringComparison.Ordinal);
        return JsonSchema.FromText(text);
    }

    /// <summary>Validates <paramref name="json"/>; returns the list of violations (empty when valid).</summary>
    public static IReadOnlyList<string> Validate(JsonSchema schema, string json)
    {
        using var instance = JsonDocument.Parse(json);
        var result = schema.Evaluate(instance.RootElement, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = true,
        });
        if (result.IsValid)
        {
            return [];
        }

        return (result.Details ?? [])
            .Where(d => d.Errors is { Count: > 0 })
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}"))
            .ToList();
    }

    private static JsonObject FindOperation(string operationId)
    {
        foreach (var (_, pathItem) in Document.Value["paths"]!.AsObject())
        {
            foreach (var (_, operation) in pathItem!.AsObject())
            {
                if (operation is JsonObject op && (string?)op["operationId"] == operationId)
                {
                    return op;
                }
            }
        }

        throw new InvalidOperationException($"Contract has no operation '{operationId}'.");
    }

    private static JsonObject Load()
    {
        var settings = new OpenApiReaderSettings();
        settings.AddYamlReader();

        using var stream = new MemoryStream(File.ReadAllBytes(ContractPath));
        var result = OpenApiDocument.Load(stream, "yaml", settings);
        if (result.Document is null || result.Diagnostic?.Errors.Count > 0)
        {
            var errors = string.Join("; ", result.Diagnostic?.Errors.Select(e => e.ToString()) ?? []);
            throw new InvalidOperationException($"{ContractPath} is not a valid OpenAPI document: {errors}");
        }

        // OpenAPI 3.1 output uses plain JSON Schema 2020-12 for schemas, which JsonSchema.Net evaluates.
        using var writer = new StringWriter();
        result.Document.SerializeAsV31(new OpenApiJsonWriter(writer));
        return JsonNode.Parse(writer.ToString())!.AsObject();
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
