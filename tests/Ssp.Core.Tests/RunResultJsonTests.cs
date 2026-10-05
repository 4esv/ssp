using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Ssp.Core.Tests;

public class RunResultJsonTests
{
    static readonly string SchemaPath = Path.Combine(RepoPaths.Root, "docs", "schema", "run-result.schema.json");

    // NOTE: One row for each released schema version. A change to the schema needs a new version and a new row.
    static readonly Dictionary<string, string> SchemaHashes = new(StringComparer.Ordinal)
    {
        ["1.0.0"] = "458ff1327e82faaded67fdd498f37060ebc7173a79fb4a86ac3633a8cc5208ee",
        ["1.1.0"] = "cd582eeaa373063d8cd99b6a9d3d65999a074fb1abdacd5fdcfe4281d136c17f",
    };

    // NOTE: JsonSchema.Net registers the schema by $id in a global registry, so build it once.
    static readonly Lazy<JsonSchema> Schema = new(() => JsonSchema.FromText(SchemaText()));

    static string SchemaText() => File.ReadAllText(SchemaPath).Replace("\r\n", "\n");

    static EvaluationResults Validate(string json)
    {
        using var instance = JsonDocument.Parse(json);
        return Schema.Value.Evaluate(instance.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
    }

    static string Describe(EvaluationResults results) =>
        string.Join("\n", (results.Details ?? []).Where(d => d.Errors is not null)
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Key}: {e.Value}")));

    /// <summary>Returns null if the schema text matches the hash for its version, else the reason it does not.</summary>
    static string? CheckLock(string schemaText, IReadOnlyDictionary<string, string> hashes)
    {
        var version = JsonNode.Parse(schemaText)!["version"]!.GetValue<string>();
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(schemaText)));
        if (!hashes.TryGetValue(version, out var expected))
        {
            return $"Schema version {version} has no hash. Add [\"{version}\"] = \"{hash}\".";
        }

        return expected == hash ? null : $"The schema changed but its version is still {version}. Change the version and add [\"<new version>\"] = \"{hash}\".";
    }

    [Fact]
    public void RcLowpassValidatesAgainstTheSchema()
    {
        var json = Runner.Run(Fixtures.Read("rc-lowpass.cir"), new RunOptions()).ToJson();

        var results = Validate(json);

        Assert.True(results.IsValid, Describe(results));
    }

    [Fact]
    public void StoppedRunValidatesAgainstTheSchema()
    {
        var json = Runner.Run(Fixtures.Read("diag-no-ground.cir"), new RunOptions()).ToJson();

        var results = Validate(json);

        Assert.True(results.IsValid, Describe(results));
    }

    [Fact]
    public void RcLowpassJsonHasTheResultValues()
    {
        var json = JsonNode.Parse(Runner.Run(Fixtures.Read("rc-lowpass.cir"), new RunOptions()).ToJson())!;

        Assert.Equal(1.0, json["operatingPoint"]!["nodeVoltages"]!["in"]!.GetValue<double>(), 6);
        Assert.Equal(41, json["frequencyResponse"]!["frequencies"]!.AsArray().Count);
        Assert.Equal(41, json["frequencyResponse"]!["magnitudeDb"]!["out"]!.AsArray().Count);
        Assert.Equal(41, json["impedance"]!["input"]!.AsArray().Count);
        Assert.NotNull(json["impedance"]!["input"]![0]!["re"]);
        Assert.NotNull(json["impedance"]!["input"]![0]!["im"]);
    }

    [Fact]
    public void JsonWithAMissingSectionDoesNotValidate()
    {
        var json = JsonNode.Parse(Runner.Run(Fixtures.Read("rc-lowpass.cir"), new RunOptions()).ToJson())!.AsObject();
        json.Remove("impedance");

        Assert.False(Validate(json.ToJsonString()).IsValid);
    }

    [Fact]
    public void SchemaHasAnIdAndAVersion()
    {
        var schema = JsonNode.Parse(SchemaText())!;
        var version = schema["version"]!.GetValue<string>();

        Assert.Equal(RunResult.SchemaId, schema["$id"]!.GetValue<string>());
        Assert.EndsWith(version, RunResult.SchemaId);
        Assert.Equal(RunResult.SchemaVersion, version);
    }

    [Fact]
    public void JsonNamesTheSchemaVersion()
    {
        var json = JsonNode.Parse(Runner.Run(Fixtures.Read("rc-lowpass.cir"), new RunOptions()).ToJson())!;

        Assert.Equal(RunResult.SchemaVersion, json["schemaVersion"]!.GetValue<string>());
    }

    [Fact]
    public void SchemaMatchesTheHashForItsVersion()
    {
        Assert.Null(CheckLock(SchemaText(), SchemaHashes));
    }

    [Fact]
    public void SchemaChangeWithoutAVersionChangeFails()
    {
        var original = SchemaText();
        var at = original.IndexOf("\"additionalProperties\": false", StringComparison.Ordinal);
        Assert.True(at >= 0);
        var changed = original.Remove(at, "\"additionalProperties\": false".Length).Insert(at, "\"additionalProperties\": true");

        var reason = CheckLock(changed, SchemaHashes);

        Assert.NotNull(reason);
        Assert.Contains("version is still", reason);
    }
}
