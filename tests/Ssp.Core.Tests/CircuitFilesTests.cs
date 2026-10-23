using Ssp.Core.Netlist;
using LayoutDoc = Ssp.Core.Layout.Layout;

namespace Ssp.Core.Tests;

/// <summary>Every netlist and layout in circuits/ loads without an error diagnostic (#324).</summary>
public class CircuitFilesTests
{
    static readonly string Circuits = Path.Combine(RepoPaths.Root, "circuits");

    // These fixtures name an op-amp model that the test supplies. Alone, they give a missing-model error by design.
    static readonly HashSet<string> NeedsModel = ["bias-opamp-near-rail.cir", "opamp-buffer.cir", "diag-opamp-no-bias.cir"];

    [Fact]
    public void EveryNetlistLoadsWithoutAnError()
    {
        var files = Directory.GetFiles(Circuits, "*.cir", SearchOption.AllDirectories)
            .Where(f => !NeedsModel.Contains(Path.GetFileName(f))).ToArray();
        Assert.NotEmpty(files);
        var errors = files.SelectMany(f => NetlistLoader.Load(File.ReadAllText(f)).Diagnostics
            .Where(d => d.Severity == Severity.Error).Select(d => $"{Path.GetRelativePath(Circuits, f)}: {d.Message}"));
        Assert.Empty(errors);
    }

    [Fact]
    public void EveryLayoutReadsAndValidatesWithoutAnError()
    {
        var files = Directory.GetFiles(Circuits, "*.layout.toml", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        var errors = new List<string>();
        foreach (var f in files)
        {
            var netlist = Path.Combine(Path.GetDirectoryName(f)!, Path.GetFileName(f)[..^".layout.toml".Length] + ".cir");
            Assert.True(File.Exists(netlist), $"{f} has no netlist.");
            var circuit = NetlistLoader.Load(File.ReadAllText(netlist));
            errors.AddRange(LayoutDoc.Read(f).Validate(circuit).Where(d => d.Severity == Severity.Error)
                .Select(d => $"{Path.GetRelativePath(Circuits, f)}: {d.Message}"));
        }
        Assert.Empty(errors);
    }
}
