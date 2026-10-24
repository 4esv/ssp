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

    // NOTE: #409: the diagnostic carries the line, so the message does not say it again.
    [Fact]
    public void AParseErrorNamesItsLineOnce()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ssp-409-{Guid.NewGuid():N}.cir");
        File.WriteAllText(path, "* t\nV1 in 0 1\nR1 in out 1k\n.END\nstray text\n");
        try
        {
            var (exitCode, output) = Run("run", path);

            Assert.Equal(1, exitCode);
            var line = Assert.Single(output.Split('\n'), l => l.Contains(".END", StringComparison.Ordinal));
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(line, "line 5", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        }
        finally
        {
            File.Delete(path);
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
        Assert.Contains("Node out is 3 dB below its peak at 336 Hz.", changed);
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

    // NOTE: The TL072 model is a subcircuit. Its nodes (X1.lo, X1.o, X1.x) and parts (X1.Rid, ...) are internal.
    [Fact]
    public void RunTextReportLeavesOutSubcircuitInternals()
    {
        var netlist = File.ReadAllText(Path.Combine(RepoPaths.Root, "models", "opamp-tl072.cir")) + "\n"
            + File.ReadAllText(RepoPaths.Fixture("opamp-buffer.cir")).Replace("OPAMP", "TL072");
        var path = Path.Combine(Path.GetTempPath(), $"ssp-subckt-{Guid.NewGuid():N}.cir");
        File.WriteAllText(path, netlist);
        try
        {
            var (exitCode, text) = Run("run", path);
            var (_, json) = Run("run", path, "--json");

            Assert.Equal(0, exitCode);
            Assert.DoesNotContain("X1.", text);
            Assert.Contains("out ", text);
            Assert.Contains("\"X1.lo\"", json);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
