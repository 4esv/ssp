using SpiceSharp.Components;
using Ssp.Core.Analysis;
using Ssp.Core.Diagnostics;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using DiagnosticsEngine = Ssp.Core.Diagnostics.Diagnostics;
using LayoutDoc = Ssp.Core.Layout.Layout;

namespace Ssp.Core.Tests;

public class BlocksTests
{
    static string Directory { get; } = Path.Combine(RepoPaths.Root, "circuits", "blocks");

    public static TheoryData<string> Blocks()
    {
        var data = new TheoryData<string>();
        foreach (var path in System.IO.Directory.GetFiles(Directory, "*.cir").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(path));
        }
        return data;
    }

    static LoadedCircuit Load(string name) => NetlistLoader.Load(File.ReadAllText(Path.Combine(Directory, name)));

    static LayoutDoc LayoutOf(string name) =>
        LayoutDoc.Read(Path.Combine(Directory, Path.ChangeExtension(name, null) + ".layout.toml"));

    static string Errors(IEnumerable<Diagnostic> diagnostics) =>
        string.Join("\n", diagnostics.Where(d => d.Severity == Severity.Error).Select(d => d.Message));

    [Fact]
    public void LibraryHas18Blocks() => Assert.Equal(18, Blocks().Count);

    [Theory]
    [MemberData(nameof(Blocks))]
    public void BlockLoadsAndRunsWithoutErrors(string name)
    {
        var loaded = Load(name);
        Assert.Equal("", Errors(loaded.Diagnostics));
        Assert.Equal("", Errors(Pot.Apply(loaded)));
        Assert.Equal("", Errors(DiagnosticsEngine.Run(loaded)));

        var op = SolverFailure.OperatingPoint(loaded);
        Assert.Null(op.Diagnostic?.Message);
        Assert.All(op.Value!.NodeVoltages.Values, v => Assert.True(double.IsFinite(v)));

        var ac = Analyses.FrequencyResponse(loaded, new DecadeSweep(20, 20_000, 10));
        Assert.All(ac.MagnitudeDb[loaded.Directives.Output!], db => Assert.True(double.IsFinite(db)));
    }

    [Theory]
    [MemberData(nameof(Blocks))]
    public void BlockHasTitleInputAndOutput(string name)
    {
        var directives = Load(name).Directives;
        Assert.False(string.IsNullOrWhiteSpace(directives.Title));
        Assert.False(string.IsNullOrWhiteSpace(directives.Input));
        Assert.False(string.IsNullOrWhiteSpace(directives.Output));
    }

    [Theory]
    [MemberData(nameof(Blocks))]
    public void BlockLayoutPlacesEveryPart(string name)
    {
        var loaded = Load(name);
        var layout = LayoutOf(name);
        Assert.Equal("", Errors(layout.Validate(loaded)));

        var placed = layout.Parts.Select(p => p.Reference).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // A subcircuit instance X1 loads as parts named X1.<part>. The layout places X1.
        var parts = loaded.Circuit.OfType<IComponent>().Select(c => c.Name.Split('.')[0]).Distinct();
        Assert.All(parts, part => Assert.Contains(part, placed));
    }
}
