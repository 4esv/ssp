using SpiceSharp.Simulations;
using SpiceSharpBehavioral.Parsers;
using SpiceSharpBehavioral.Parsers.Nodes;
using Ssp.Core.Analysis;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;

namespace Ssp.Core.Tests;

/// <summary>
/// The op-amp macro-model costs less for each solver step (#223), and it keeps the fit values of the provenance header.
/// The cost is the node count of the expressions of the behavioral sources and of their derivatives, because the engine evaluates both in each Load.
/// </summary>
public class OpAmpCostTests
{
    const double Rail = 15.0;

    public static TheoryData<string> Names => new() { "TL072", "LM308", "JRC4558" };

    static OpAmpModel Set(string name) => name switch
    {
        "TL072" => OpAmpModel.Tl072,
        "LM308" => OpAmpModel.Lm308,
        "JRC4558" => OpAmpModel.Jrc4558,
        _ => throw new ArgumentException(name),
    };

    static string ModelText(string name) => File.ReadAllText(Path.Combine(RepoPaths.Root, "models", $"opamp-{name.ToLowerInvariant()}.cir"));

    // The clamp of the model before #223: a soft max inside a soft min. Each V() and each sqrt is written again for each term.
    static string NestedClamp(string value, string low, string high)
    {
        const string corner = "0.000001";
        var raised = $"(0.5*({value}+{low}+sqrt(({value}-({low}))*({value}-({low}))+{corner})))";
        return $"(0.5*({raised}+{high}-sqrt(({raised}-({high}))*({raised}-({high}))+{corner})))";
    }

    // The behavioral sources of the model before #223: Bgm, Bclamp, Bo and Bout. The numbers do not change the cost.
    static string[] OldExpressions(string text)
    {
        var drop = text.Split("rail drop ")[1].Split(' ')[0];
        return
        [
            Expressions(text)[0],
            $"0.001*(V(x)-{NestedClamp("V(x)", "V(vee)", "V(vcc)")})",
            "V(x)",
            $"100*(V(out)-{NestedClamp("V(out)", $"V(vee)+{drop}", $"V(vcc)-{drop}")})",
        ];
    }

    // The expressions of all behavioral sources, in the order of the file. Bgm is the first.
    static string[] Expressions(string text) => text.Split('\n')
        .Where(l => l.StartsWith("B", StringComparison.Ordinal) && l.Contains("={", StringComparison.Ordinal))
        .Select(l => l[(l.IndexOf("={", StringComparison.Ordinal) + 2)..l.LastIndexOf('}')])
        .ToArray();

    static int Count(Node node) => node switch
    {
        BinaryOperatorNode b => 1 + Count(b.Left) + Count(b.Right),
        UnaryOperatorNode u => 1 + Count(u.Argument),
        TernaryOperatorNode t => 1 + Count(t.Condition) + Count(t.IfTrue) + Count(t.IfFalse),
        FunctionNode f => 1 + f.Arguments.Sum(Count),
        _ => 1,
    };

    static IEnumerable<VariableNode> Variables(Node node) => node switch
    {
        BinaryOperatorNode b => Variables(b.Left).Concat(Variables(b.Right)),
        UnaryOperatorNode u => Variables(u.Argument),
        TernaryOperatorNode t => Variables(t.Condition).Concat(Variables(t.IfTrue)).Concat(Variables(t.IfFalse)),
        FunctionNode f => f.Arguments.SelectMany(Variables),
        VariableNode v => [v],
        _ => [],
    };

    // Nodes of the expression and of its derivative to each node voltage.
    static int Cost(string expression)
    {
        var node = Parser.Parse(Lexer.FromString(expression));
        var derivatives = new Derivatives { Variables = new HashSet<VariableNode>(Variables(node)) }.Derive(node);
        return Count(node) + derivatives.Values.Sum(Count);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void TheClampsCostAtLeast60PercentLessThanTheNestedClamps(string name)
    {
        var text = ModelText(name);
        var now = Expressions(text);
        var before = OldExpressions(text);
        Assert.Equal(5, now.Length);

        var costNow = now.Sum(Cost);
        var costBefore = before.Sum(Cost);

        // Measured for each set: 1209 nodes before, 447 now (63 % less). See docs/benchmarks.md.
        Assert.True(costNow < 0.4 * costBefore, $"{name}: {costNow} nodes now, {costBefore} before.");
    }

    static LoadedCircuit OpenLoop(string name, string source)
    {
        var netlist = ModelText(name) + $"""

            V1 in 0 {source}
            Vcc vcc 0 DC 15
            Vee vee 0 DC -15
            X1 in 0 out vcc vee {name}
            .END
            """;
        var loaded = NetlistLoader.Load(netlist);
        Assert.Empty(loaded.Diagnostics);
        return loaded;
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void DcGainAndGbwAreWithin1PercentOfTheHeader(string name)
    {
        var model = Set(name);

        var result = Analyses.FrequencyResponse(OpenLoop(name, "DC 0 AC 1"), new Ssp.Core.Analysis.DecadeSweep(0.01, 1e6, 10));

        double At(double f) => result.MagnitudeDb["out"][result.Frequencies.ToList().FindIndex(x => Math.Abs(x / f - 1) < 1e-6)];
        var gain = Math.Pow(10, At(0.01) / 20);
        var gbw = Math.Pow(10, At(1e6) / 20) * 1e6;
        Assert.InRange(gain, 0.99 * model.Gain, 1.01 * model.Gain);
        Assert.InRange(gbw, 0.99 * model.Gbw, 1.01 * model.Gbw);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void RailDropIsWithin1PercentOfTheHeader(string name)
    {
        var model = Set(name);

        var high = Analyses.OperatingPoint(OpenLoop(name, "DC 1"));
        var low = Analyses.OperatingPoint(OpenLoop(name, "DC -1"));

        Assert.InRange(Rail - high.NodeVoltages["out"], 0.99 * model.RailDrop, 1.01 * model.RailDrop);
        Assert.InRange(low.NodeVoltages["out"] + Rail, 0.99 * model.RailDrop, 1.01 * model.RailDrop);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void SlewRateIsWithin1PercentOfTheHeader(string name)
    {
        var model = Set(name);
        var loaded = OpenLoop(name, "PULSE(-10 10 1u 1n 1n 1 2)");
        var rise = 10.0 / model.SlewRate;
        var tran = new Transient("tran", rise / 200, 1e-6 + (4 * rise), rise / 200);
        var export = new RealVoltageExport(tran, "out");
        var samples = new List<(double T, double V)>();
        foreach (var _ in tran.Run(loaded.Circuit, Transient.ExportTransient))
        {
            samples.Add((tran.Time, export.Value));
        }

        // Open loop with no load: from -5 V to 5 V the stage current is at its limit, and the output rises at the slew rate.
        double Cross(double level)
        {
            var k = samples.FindIndex(s => s.T > 1e-6 && s.V >= level);
            var (a, b) = (samples[k - 1], samples[k]);
            return a.T + ((level - a.V) * (b.T - a.T) / (b.V - a.V));
        }
        var slew = 10.0 / (Cross(5.0) - Cross(-5.0));
        Assert.InRange(slew, 0.99 * model.SlewRate, 1.01 * model.SlewRate);
    }
}
