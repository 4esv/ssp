using System.Diagnostics;
using Ssp.Core.Netlist;
using Ssp.Web.Library;
using Ssp.Web.Schematic;
using Xunit.Abstractions;
using LayoutDoc = Ssp.Core.Layout.Layout;

namespace Ssp.Web.Tests;

// NOTE: #454. The editor lays a circuit out from nothing when an edit adds, removes or rewires a part. On a pedal-sized
// circuit that took up to 25 s in .NET and 115 s in the browser.
// NOTE: A time is wall time, so these tests run alone, after the tests that run in parallel.
[Collection(Name)]
public class AutoPlacerSpeedTests(ITestOutputHelper output)
{
    public const string Name = "Placer speed";

    // NOTE: 1 s in a local Release run. A CI runner is slower and shares its cores, and a Debug build (UPDATE_GOLDEN=1 dotnet test)
    // is slower, so there the bound is 3 s. Before #454 the pedals took 8 s to 25 s, so either bound fails on that.
    static readonly double Bound = Debug || Environment.GetEnvironmentVariable("CI") is { Length: > 0 } ? 3.0 : 1.0;

#if DEBUG
    const bool Debug = true;
#else
    const bool Debug = false;
#endif

    public static TheoryData<string> Pedals() => new(["clean-boost.cir", "overdrive-808.cir", "distortion-rat.cir", "fuzz-face-si.cir", "fuzz-muff.cir"]);

    static string Netlist(string file) => CircuitLibrary.All.Single(c => c.FileName == file).Netlist;

    // The best of three runs, so a pause of the runner does not fail the test.
    double Seconds(LoadedCircuit circuit, string name)
    {
        var warm = NetlistLoader.Load("V1 a 0 DC 1\nR1 a 0 1k\n");
        AutoPlacer.Place(warm, warm.Directives);
        var best = double.PositiveInfinity;
        for (var run = 0; run < 3; run++)
        {
            var watch = Stopwatch.StartNew();
            AutoPlacer.Place(circuit, circuit.Directives);
            best = Math.Min(best, watch.Elapsed.TotalSeconds);
            if (best > 5 * Bound) break;
        }
        output.WriteLine($"{name}: {best:F3} s");
        return best;
    }

    [Fact]
    public void A_pedal_with_one_more_resistor_is_laid_out_in_under_a_second()
    {
        var netlist = Netlist("overdrive-808.cir").Replace(".END", "R99 co 0 100k\n.END", StringComparison.Ordinal);
        var circuit = NetlistLoader.Load(netlist);
        Assert.Contains(circuit.Circuit, c => c.Name == "R99");

        Assert.True(Seconds(circuit, "overdrive-808 + R99") < Bound, $"The layout of overdrive-808 with one more resistor took {Bound} s or more.");
    }

    [Theory]
    [MemberData(nameof(Pedals))]
    public void Each_library_pedal_is_laid_out_in_under_a_second(string file)
    {
        var circuit = NetlistLoader.Load(Netlist(file));
        // NOTE: PLACER_DUMP names a folder for the layouts, to compare them before and after a change of the placer.
        if (Environment.GetEnvironmentVariable("PLACER_DUMP") is { Length: > 0 } dir)
        {
            File.WriteAllText(Path.Combine(dir, Path.ChangeExtension(file, ".layout.toml")), LayoutDoc.Format(AutoPlacer.Place(circuit, circuit.Directives)));
        }
        Assert.True(Seconds(circuit, file) < Bound, $"The layout of {file} took {Bound} s or more.");
    }
}

[CollectionDefinition(AutoPlacerSpeedTests.Name, DisableParallelization = true)]
public class AutoPlacerSpeedCollection;
