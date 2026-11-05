using Ssp.Core;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Web.Components;
using Ssp.Web.Library;
using Ssp.Web.Schematic;
using LayoutDoc = Ssp.Core.Layout.Layout;

namespace Ssp.Web.Tests;

/// <summary>
/// #446: after a run, a node voltage on the schematic does not cover a ref label, a value label or a part, and a
/// voltage under 1 µV reads 0 V, not solver noise in femtovolts.
/// </summary>
public class RunVoltageLabelTests
{
    static readonly PartsTable Table = Ssp.Core.Parts.Parts.Load(Path.Combine(RepoPaths.Root, "models", "parts.toml"));

    public static TheoryData<string> Pedals() => new("overdrive-808.cir", "clean-boost.cir", "distortion-rat.cir", "fuzz-face-si.cir", "fuzz-muff.cir");

    // The labels as the editor draws them after a run: the library layout, the parts table and the first pin of each net.
    static (SchematicDrawing Drawing, IReadOnlyList<VoltageLabel> Labels, IReadOnlyDictionary<string, double> Volts) Labelled(string file)
    {
        var entry = CircuitLibrary.All.Single(c => c.FileName == file);
        var circuit = NetlistLoader.Load(entry.Netlist);
        var parts = PartMap.Resolve(circuit, Table);
        var layout = LayoutDoc.Parse(entry.Layout!);
        var drawing = SchematicRenderer.Draw(circuit, layout, parts);
        var volts = Runner.Run(entry.Netlist, new RunOptions()).OperatingPoint!.NodeVoltages;
        var anchors = SchematicRenderer.OverlayPins(circuit, layout, parts).Select(p => (p.Net, p.At));
        return (drawing, SchematicRenderer.VoltageLabels(drawing, anchors, volts), volts);
    }

    [Theory]
    [MemberData(nameof(Pedals))]
    public void No_voltage_label_sits_on_a_label_or_a_part(string file)
    {
        var (drawing, labels, _) = Labelled(file);

        Assert.NotEmpty(labels);
        foreach (var label in labels)
        {
            foreach (var text in drawing.Text) Assert.False(label.Box.Overlaps(text.Box), $"{file}: {label.Text} at {label.Node} sits on '{text.Text}'.");
            foreach (var part in drawing.Parts) Assert.False(label.Box.Overlaps(part.Box), $"{file}: {label.Text} at {label.Node} sits on {part.Reference}.");
            foreach (var mark in drawing.Marks) Assert.False(label.Box.Overlaps(mark), $"{file}: {label.Text} at {label.Node} sits on a ground or supply symbol.");
        }
        for (var i = 0; i < labels.Count; i++)
        {
            for (var j = i + 1; j < labels.Count; j++) Assert.False(labels[i].Box.Overlaps(labels[j].Box), $"{file}: {labels[i].Text} sits on {labels[j].Text}.");
        }
    }

    [Theory]
    [MemberData(nameof(Pedals))]
    public void A_node_under_1_uV_shows_0_V(string file)
    {
        var (_, labels, volts) = Labelled(file);

        foreach (var label in labels.Where(l => Math.Abs(volts[l.Node]) < 1e-6)) Assert.Equal("0 V", label.Text);
        Assert.DoesNotContain(labels, l => l.Text.EndsWith("fV", StringComparison.Ordinal) || l.Text.EndsWith("pV", StringComparison.Ordinal) || l.Text.EndsWith("nV", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-8e-15, "0 V")]
    [InlineData(4.9e-7, "0 V")]
    [InlineData(-9.99e-7, "0 V")]
    [InlineData(0, "0 V")]
    [InlineData(2e-6, "2 µV")]
    [InlineData(-0.784, "-784 mV")]
    [InlineData(4.5, "4.5 V")]
    public void Node_voltages_under_1_uV_are_0_V(double value, string text) => Assert.Equal(text, Eng.NodeVolts(value));

    [Fact]
    public void Other_quantities_keep_their_small_values() => Assert.Equal("-8 fV", Eng.Format(-8e-15, "V"));
}
