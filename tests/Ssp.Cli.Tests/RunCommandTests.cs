using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ssp.Cli.Tests;

public class RunCommandTests
{
    static (int ExitCode, string Output) Run(params string[] args)
    {
        using var output = new StringWriter();
        var exitCode = Program.Run(args, output);
        return (exitCode, output.ToString());
    }

    // NOTE: Timings are wall-clock values. Set them to 0 so that the golden JSON is stable.
    // The math library of each OS can change the last digit of a double. Round each number to 10 significant digits.
    internal static string Stable(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        var timings = root["timingsMs"]!.AsObject();
        foreach (var key in timings.Select(p => p.Key).ToList())
        {
            timings[key] = 0;
        }

        return Round(root)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    static JsonNode? Round(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var key in o.Select(p => p.Key).ToList())
                {
                    o[key] = Round(o[key]?.DeepClone());
                }

                return o;
            case JsonArray a:
                return new JsonArray(a.Select(item => Round(item?.DeepClone())).ToArray());
            case JsonValue v when v.GetValueKind() == JsonValueKind.Number:
                var value = double.Parse(v.ToJsonString(), CultureInfo.InvariantCulture);
                var rounded = double.Parse(value.ToString("G10", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                return JsonValue.Create(rounded == 0 ? 0 : rounded);
            default:
                return node;
        }
    }

    [Fact]
    public void RunPrintsTheReportAndExitsZero()
    {
        var (exitCode, output) = Run("run", RepoPaths.Fixture("rc-lowpass.cir"));

        Assert.Equal(0, exitCode);
        Golden.Assert("run-rc-lowpass.txt", output);
    }

    [Fact]
    public void RunWithJsonPrintsTheResultAndExitsZero()
    {
        var (exitCode, output) = Run("run", RepoPaths.Fixture("rc-lowpass.cir"), "--json");

        Assert.Equal(0, exitCode);
        Golden.Assert("run-rc-lowpass.json", Stable(output));
    }

    [Fact]
    public void RunExitsOneWhenADiagnosticIsAnError()
    {
        var (exitCode, output) = Run("run", RepoPaths.Fixture("diag-no-ground.cir"));

        Assert.Equal(1, exitCode);
        Assert.Contains("error", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SspPartDirectivesReachTheReport()
    {
        const string netlist = """
            * ssp:part D9 1N4148
            * ssp:part D1 NOPE
            V1 a 0 5
            D1 a b DGEN
            R1 b 0 1k
            .MODEL DGEN D
            .END
            """;
        var path = Path.Combine(Path.GetTempPath(), $"ssp-part-{Guid.NewGuid():N}.cir");
        File.WriteAllText(path, netlist);
        try
        {
            var (exitCode, output) = Run("run", path);

            Assert.Equal(1, exitCode);
            Assert.Contains("NOPE", output);
            Assert.Contains("D9", output);
            Assert.Contains("error", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("warning", output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RunWithJsonExitsOneWhenADiagnosticIsAnError()
    {
        var (exitCode, output) = Run("run", RepoPaths.Fixture("diag-no-ground.cir"), "--json");

        Assert.Equal(1, exitCode);
        Assert.Contains("\"severity\": \"error\"", output);
    }

    [Fact]
    public void SetChangesTheOutput()
    {
        var (_, plain) = Run("run", RepoPaths.Fixture("rc-lowpass.cir"));
        var (exitCode, changed) = Run("run", RepoPaths.Fixture("rc-lowpass.cir"), "--set", "R1=4k7");

        Assert.Equal(0, exitCode);
        Assert.NotEqual(plain, changed);
        Assert.Contains("Node out falls 3 dB below its 10.0 Hz level at 336 Hz.", changed);
    }

    [Fact]
    public void ADirectoryInPlaceOfTheNetlistPrintsOneLineNamingThePath()
    {
        var dir = Path.GetTempPath();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Program.Run(["run", dir], output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains(Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar), error.ToString());
        Assert.Single(error.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.DoesNotContain("Unhandled exception", error.ToString());
        Assert.DoesNotContain("   at ", error.ToString());
    }
}
