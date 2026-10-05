using Bunit;
using Microsoft.JSInterop;
using Ssp.Web.Components;
using Ssp.Web.Schematic;

namespace Ssp.Web.Tests;

public class SymbolTests : BunitContext
{
    public static TheoryData<string> EditorKinds()
    {
        var data = new TheoryData<string>();
        foreach (var kind in SchematicEdits.Kinds.Keys) data.Add(kind);
        return data;
    }

    [Theory]
    [MemberData(nameof(EditorKinds))]
    public void EveryEditorKindHasItsOwnSymbol(string kind)
    {
        var symbol = Symbols.Find(kind);

        Assert.NotNull(symbol);
        Assert.Equal(kind, symbol.Kind);
        Assert.NotEmpty(symbol.Pins);
    }

    [Fact]
    public void NoTwoKindsShareADrawing()
    {
        var shared = Symbols.Kinds.GroupBy(k => Symbols.For(k).Svg).Where(g => g.Count() > 1).Select(g => string.Join(" = ", g)).ToList();

        Assert.Empty(shared);
    }

    [Fact]
    public void AJackIsNotAResistorAndABatteryIsNotASource()
    {
        Assert.NotEqual(Symbols.For("resistor").Svg, Symbols.For("jack-in").Svg);
        Assert.NotEqual(Symbols.For("resistor").Svg, Symbols.For("jack-out").Svg);
        Assert.NotEqual(Symbols.For("source").Svg, Symbols.For("battery").Svg);
        Assert.Contains(">IN<", Symbols.For("jack-in").Svg);
        Assert.Contains(">OUT<", Symbols.For("jack-out").Svg);
    }

    [Theory]
    [MemberData(nameof(EditorKinds))]
    public void ThePaletteIconIsTheSymbol(string kind)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var editor = Render<SchematicEditor>(p => p.Add(c => c.Netlist, "* t\nR1 a 0 1k\n.END\n"));

        var icon = editor.Find($".palette-item[data-kind=\"{kind}\"] .palette-symbol").InnerHtml;

        icon.MarkupMatches(Symbols.Icon(kind));
        if (!SchematicEdits.IsSwitch(kind)) Assert.Equal(Symbols.For(kind).Svg, Symbols.Icon(kind));
    }

    [Fact]
    public void ACanvasJackIsDrawnWithItsSymbol()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var editor = Render<SchematicEditor>(p => p.Add(c => c.Netlist, "* t\n* ssp:input a\n* ssp:output b\nR1 a b 1k\n.END\n"));

        Assert.Empty(editor.FindAll(".jack-ring"));
        Assert.Contains("IN", editor.Find("g[data-jack=\"in\"]").InnerHtml);
        Assert.Contains("OUT", editor.Find("g[data-jack=\"out\"]").InnerHtml);
    }

    [Theory]
    [InlineData("1 DC 9", "battery")]
    [InlineData("DC 0 AC 1 SINE(0 1 1k)", "source")]
    public void ACanvasSourceKeepsTheKindItWasPlacedAs(string spec, string kind)
    {
        var circuit = Ssp.Core.Netlist.NetlistLoader.Load($"* t\nV9 a 0 {spec}\nR1 a 0 1k\n.END\n");
        var elements = SchematicRenderer.Elements(circuit, null);

        Assert.Equal(kind, elements.Single(e => e.Reference == "V9").Kind);
    }
}
