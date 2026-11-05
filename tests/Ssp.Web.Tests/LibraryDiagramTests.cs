using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Web.Library;
using Ssp.Web.Schematic;

namespace Ssp.Web.Tests;

/// <summary>
/// #417: each circuit of the library and of the landing page draws a readable schematic. No part sits on a part, no
/// label sits on a label or on a part, and the signal goes from the input at the left to the output at the right.
/// </summary>
/// <remarks>
/// The gallery draws a preview with the parts table, and the editor places a circuit without one, so both placements are
/// drawn here. Two placements of the same netlist can differ: the parts table gives a part a better symbol.
/// </remarks>
public class LibraryDiagramTests
{
    // NOTE: The landing page shows the library circuits, and the editor opens with this one.
    const string Starter = "starter.cir";

    public static TheoryData<string, bool> Circuits()
    {
        var data = new TheoryData<string, bool>();
        foreach (var file in CircuitLibrary.All.Select(c => c.FileName).Append(Starter)) data.Add(file, false);
        foreach (var file in CircuitLibrary.All.Select(c => c.FileName).Append(Starter)) data.Add(file, true);
        return data;
    }

    static string Netlist(string file) => file == Starter
        ? File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", "rc-lowpass.cir"))
        : CircuitLibrary.All.Single(c => c.FileName == file).Netlist;

    static readonly PartsTable Table = Ssp.Core.Parts.Parts.Load(Path.Combine(RepoPaths.Root, "models", "parts.toml"));

    static readonly Dictionary<(string, bool), SchematicDrawing> Drawn = [];

    // A circuit as the gallery draws it (with the parts table) or as the editor places it (without one).
    static SchematicDrawing Drawing(string file, bool partsTable)
    {
        lock (Drawn)
        {
            if (Drawn.TryGetValue((file, partsTable), out var known)) return known;
            var circuit = NetlistLoader.Load(Netlist(file));
            var parts = PartMap.Resolve(circuit, Table);
            var layout = partsTable ? AutoPlacer.Place(circuit, circuit.Directives, parts) : AutoPlacer.Place(circuit, circuit.Directives);
            return Drawn[(file, partsTable)] = SchematicRenderer.Draw(circuit, layout, parts);
        }
    }

    static string Name(string file, bool partsTable) => partsTable ? $"{file} with the parts table" : file;

    [Theory]
    [MemberData(nameof(Circuits))]
    public void NoPartSitsOnAnotherPart(string file, bool partsTable)
    {
        var parts = Drawing(file, partsTable).Parts;

        for (var i = 0; i < parts.Count; i++)
        {
            for (var j = i + 1; j < parts.Count; j++)
            {
                Assert.False(parts[i].Box.Overlaps(parts[j].Box), $"{Name(file, partsTable)}: {parts[i].Reference} sits on {parts[j].Reference}.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Circuits))]
    public void NoTextSitsOnAnotherTextOrOnAPart(string file, bool partsTable)
    {
        var drawing = Drawing(file, partsTable);

        for (var i = 0; i < drawing.Text.Count; i++)
        {
            for (var j = i + 1; j < drawing.Text.Count; j++)
            {
                Assert.False(drawing.Text[i].Box.Overlaps(drawing.Text[j].Box), $"{Name(file, partsTable)}: '{drawing.Text[i].Text}' sits on '{drawing.Text[j].Text}'.");
            }
            foreach (var part in drawing.Parts)
            {
                Assert.False(drawing.Text[i].Box.Overlaps(part.Box), $"{Name(file, partsTable)}: '{drawing.Text[i].Text}' sits on {part.Reference}.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Circuits))]
    public void TheInputIsAtTheLeftAndTheOutputAtTheRight(string file, bool partsTable)
    {
        var drawing = Drawing(file, partsTable);
        var directives = NetlistLoader.Load(Netlist(file)).Directives;
        var parts = drawing.Parts.ToDictionary(p => p.Reference, StringComparer.OrdinalIgnoreCase);

        // NOTE: A circuit with no ssp:input is fed by its source, on the node that is not ground.
        var source = drawing.Parts.FirstOrDefault(p => p.Kind == "source")?.Reference;
        var input = directives.Input
            ?? drawing.Pins.Where(p => string.Equals(p.Reference, source, StringComparison.OrdinalIgnoreCase) && p.Net is not ("0" or "gnd")).Select(p => p.Net).FirstOrDefault();
        if (input is not null)
        {
            var fed = drawing.Pins.Where(p => string.Equals(p.Net, input, StringComparison.OrdinalIgnoreCase)).ToList();
            var left = parts.Values.Min(p => p.Box.X0);
            Assert.True(fed.Any(p => parts[p.Reference].Box.X0 <= left + Tolerance), $"{Name(file, partsTable)}: the input is not at the left. {Describe(drawing)}");
        }

        if (directives.Output is not { } output) return;
        var leaves = drawing.Pins.Where(p => string.Equals(p.Net, output, StringComparison.OrdinalIgnoreCase)).ToList();
        var right = parts.Values.Max(p => p.Box.X1);
        Assert.True(leaves.Any(p => parts[p.Reference].Box.X1 >= right - Tolerance), $"{Name(file, partsTable)}: the output is not at the right. {Describe(drawing)}");
    }

    // NOTE: A drawn coordinate is rounded to two places, so an edge that two parts share is equal up to that.
    const double Tolerance = 0.01;

    static string Describe(SchematicDrawing drawing) =>
        string.Join(", ", drawing.Parts.OrderBy(p => p.Box.X0).Select(p => $"{p.Reference} {p.Box.X0}..{p.Box.X1}"));
}
