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
    static string MaskTimings(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        var timings = root["timingsMs"]!.AsObject();
        foreach (var key in timings.Select(p => p.Key).ToList())
        {
            timings[key] = 0;
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
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
        Golden.Assert("run-rc-lowpass.json", MaskTimings(output));
    }

    [Fact]
    public void RunExitsOneWhenADiagnosticIsAnError()
    {
        var (exitCode, output) = Run("run", RepoPaths.Fixture("diag-no-ground.cir"));

        Assert.Equal(1, exitCode);
        Assert.Contains("error", output, StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("Node out falls 3 dB below its 10.0 Hz level at 339 Hz.", changed);
    }
}
