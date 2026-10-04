using SpiceSharp.Components;
using Ssp.Core.Analysis;
using Ssp.Core.Chains;
using Ssp.Core.Diagnostics;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using DiagnosticsEngine = Ssp.Core.Diagnostics.Diagnostics;

namespace Ssp.Core.Tests;

public class ChainTests
{
    static readonly DecadeSweep Sweep = new(100, 10_000, 10);

    // The buffer between the gain stage and the tone stage keeps the tone stack off the gain stage output.
    static readonly IReadOnlyList<ChainStage> Stages =
    [
        Stage("input", "buffer-opamp-tl072.cir"),
        Stage("drive", "gain-variable-tl072.cir"),
        Stage("buffer", "buffer-opamp-tl072.cir"),
        Stage("tone", "tone-baxandall-passive.cir"),
    ];

    static ChainStage Stage(string name, string block) =>
        new(name, File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "blocks", block)));

    static string Errors(IEnumerable<Diagnostic> diagnostics) =>
        string.Join("\n", diagnostics.Where(d => d.Severity == Severity.Error).Select(d => d.Message));

    static LoadedCircuit LoadWithKnobs(string netlist)
    {
        var loaded = NetlistLoader.Load(netlist);
        Assert.Equal("", Errors(loaded.Diagnostics));
        Assert.Equal("", Errors(Pot.Apply(loaded)));
        return loaded;
    }

    static double GainAt1k(LoadedCircuit loaded)
    {
        var ac = Analyses.FrequencyResponse(loaded, Sweep);
        var index = Enumerable.Range(0, ac.Frequencies.Count).MinBy(i => Math.Abs(ac.Frequencies[i] - 1000));
        Assert.Equal(1000, ac.Frequencies[index], 1e-6);
        return ac.MagnitudeDb[loaded.Directives.Output!][index];
    }

    [Fact]
    public void ChainGainIsTheSumOfTheStageGains()
    {
        var sum = Stages.Sum(s => GainAt1k(LoadWithKnobs(s.Netlist)));
        var chain = GainAt1k(LoadWithKnobs(Chain.Compose(Stages)));

        Assert.InRange(chain, sum - 0.5, sum + 0.5);
    }

    [Fact]
    public void ChainLoadsWithoutErrors()
    {
        var loaded = LoadWithKnobs(Chain.Compose(Stages));
        Assert.Equal("", Errors(DiagnosticsEngine.Run(loaded)));
        Assert.Null(SolverFailure.OperatingPoint(loaded).Diagnostic?.Message);
    }

    [Fact]
    public void ChainKeepsEveryPartAndNodeOfEachStage()
    {
        var chain = NetlistLoader.Load(Chain.Compose(Stages));
        var stages = Stages.Select(s => NetlistLoader.Load(s.Netlist)).ToList();
        var supplies = new[] { "vcc", "vee" };

        // Each stage loses its input source. The first input source and one source for each supply node stay.
        var parts = stages.Sum(s => s.Circuit.OfType<IComponent>().Count(c => c is not VoltageSource)) + 1 + supplies.Length;
        Assert.Equal(parts, chain.Circuit.OfType<IComponent>().Count());
        Assert.Equal(parts, chain.Circuit.Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // Each stage keeps its own nodes. The input and output nodes join, the supply nodes and ground are shared.
        var own = stages.Sum(s => s.NodeNames.Except([s.Directives.Input!, s.Directives.Output!, "0", .. supplies]).Count());
        var nodes = own + Stages.Count + 1 + supplies.Length + 1;
        Assert.Equal(nodes, chain.NodeNames.Count);
        Assert.Equal(nodes, chain.NodeNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EachKnobHasTheStageName()
    {
        var netlist = Chain.Compose(Stages);
        var knobs = NetlistLoader.Load(netlist).Directives.Knobs.Select(k => k.Part);
        Assert.Equal(["drive.RV1", "tone.RV1", "tone.RV2"], knobs);

        // Knob at 0.5: 5.9 dB. Knob at 1: 40.1 dB. See gain-variable-tl072.cir.
        var half = GainAt1k(LoadWithKnobs(netlist));
        var full = GainAt1k(LoadWithKnobs(netlist.Replace("ssp:knob drive.RV1 linear 0.5", "ssp:knob drive.RV1 linear 1")));
        Assert.InRange(full - half, 33, 35);
    }

    [Fact]
    public void RunnerRunsAChain()
    {
        var result = Runner.Run(Chain.Compose(Stages), new RunOptions(Sweep));

        Assert.Equal("", Errors(result.Diagnostics));
        Assert.NotNull(result.OperatingPoint);
        Assert.NotNull(result.FrequencyResponse);
    }

    [Fact]
    public void StageNamesMustBeUnique() =>
        Assert.Throws<ArgumentException>(() => Chain.Compose([Stages[0], Stages[0]]));

    [Fact]
    public void ASourceBehindASeriesResistorOnTheInputIsTheInputSource()
    {
        const string Drawn = "* drawn\n* ssp:input in\n* ssp:output out\nR1 in n2 1m\nV1 n2 0 DC 0 AC 1\nR2 in out 10k\nR3 out 0 10k\n.END\n";
        var netlist = Chain.Compose([new ChainStage("pedal", Drawn), Stage("buffer", "buffer-opamp-tl072.cir")]);

        Assert.DoesNotContain("n2", netlist);
        Assert.DoesNotContain("1m", netlist);
        var loaded = NetlistLoader.Load(netlist);
        Assert.Equal("", Errors(loaded.Diagnostics));
        Assert.DoesNotContain(loaded.Circuit.OfType<VoltageSource>(), v => v.Nodes.Contains("Xpedal.in") && v.Nodes.Contains("Xpedal.n2"));
    }

    [Fact]
    public void ASourceBehindAResistorOver10MegohmIsLeftAlone()
    {
        const string Drawn = "* drawn\n* ssp:input in\n* ssp:output out\nR1 in n2 20meg\nV1 n2 0 DC 0 AC 1\nR2 in out 10k\nR3 out 0 10k\n.END\n";
        Assert.Contains("n2", Chain.Compose([new ChainStage("pedal", Drawn)]));
    }
}
