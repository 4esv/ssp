using System.Text.Json.Nodes;

namespace Ssp.Cli.Tests;

public class SweepCommandTests
{
    static (int ExitCode, string Output) Run(params string[] args)
    {
        using var output = new StringWriter();
        var exitCode = Program.Run(args, output);
        return (exitCode, output.ToString());
    }

    static JsonArray Series(string json) => JsonNode.Parse(json)!["series"]!.AsArray();

    [Fact]
    public void ThreeValuesGiveThreeSeries()
    {
        var (exitCode, output) = Run("sweep", RepoPaths.Fixture("rc-lowpass.cir"), "--set", "R1=4k7,10k,22k", "--json");

        Assert.Equal(0, exitCode);
        Assert.Equal("R1", JsonNode.Parse(output)!["part"]!.GetValue<string>());
        Assert.Equal(3, Series(output).Count);
    }

    [Fact]
    public void FiftyValuesGiveFiftySeries()
    {
        var values = string.Join(",", Enumerable.Range(1, 50).Select(i => $"{i}k"));

        var (exitCode, output) = Run("sweep", RepoPaths.Fixture("rc-lowpass.cir"), "--set", $"R1={values}", "--json");

        Assert.Equal(0, exitCode);
        Assert.Equal(50, Series(output).Count);
    }

    [Fact]
    public void EachSeriesNamesTheValueItUsed()
    {
        var (_, output) = Run("sweep", RepoPaths.Fixture("rc-lowpass.cir"), "--set", "R1=4k7,10k,22k", "--json");

        var series = Series(output);
        Assert.Equal(["4k7", "10k", "22k"], series.Select(s => s!["value"]!.GetValue<string>()));
        Assert.Equal([4700.0, 10_000.0, 22_000.0], series.Select(s => s!["number"]!.GetValue<double>()));
    }

    [Fact]
    public void EachSeriesHasItsOwnFrequencyResponse()
    {
        var (_, output) = Run("sweep", RepoPaths.Fixture("rc-lowpass.cir"), "--set", "R1=4k7,22k", "--json");

        var responses = Series(output)
            .Select(s => s!["result"]!["frequencyResponse"]!["magnitudeDb"]!["out"]!.ToJsonString())
            .ToList();
        Assert.NotEqual(responses[0], responses[1]);
    }

    [Fact]
    public void TextOutputNamesEachValue()
    {
        var (exitCode, output) = Run("sweep", RepoPaths.Fixture("rc-lowpass.cir"), "--set", "R1=4k7,22k");

        Assert.Equal(0, exitCode);
        Assert.Contains("R1 = 4k7", output);
        Assert.Contains("R1 = 22k", output);
        Assert.Contains("Node out falls 3 dB below its 10.0 Hz level at 336 Hz.", output);
    }

    [Fact]
    public void BadValueExitsOne()
    {
        var (exitCode, _) = Run("sweep", RepoPaths.Fixture("rc-lowpass.cir"), "--set", "R1=4k7,abc", "--json");

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void UnknownPartExitsOne()
    {
        var (exitCode, _) = Run("sweep", RepoPaths.Fixture("rc-lowpass.cir"), "--set", "R9=1k,2k", "--json");

        Assert.Equal(1, exitCode);
    }
}
