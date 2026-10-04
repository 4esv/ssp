using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Web.Components;
using Ssp.Web.Schematic;

namespace Ssp.Web.Tests;

public class SchematicValueTests : BunitContext
{
    public SchematicValueTests() => Services.AddSingleton(TimeProvider.System);

    [Theory]
    [InlineData("47k", 47e3)]
    [InlineData("4u7", 4.7e-6)]
    [InlineData("10n", 10e-9)]
    [InlineData("1M", 1e6)]
    [InlineData("1meg", 1e6)]
    [InlineData("2m2", 2.2e-3)]
    [InlineData("4k7", 4.7e3)]
    [InlineData("100", 100)]
    [InlineData("0.1u", 0.1e-6)]
    [InlineData(" 47 kΩ ", 47e3)]
    [InlineData("10nF", 10e-9)]
    [InlineData("1e3", 1e3)]
    public void ParsesAValue(string text, double expected)
    {
        Assert.True(SchematicEdits.TryParseValue(text, out var value, out var error), error);
        Assert.Equal(expected, value, expected * 1e-9);
    }

    [Theory]
    [InlineData("", "Type a value")]
    [InlineData("   ", "Type a value")]
    [InlineData("abc", "is not a value")]
    [InlineData("47x", "is not a value")]
    [InlineData("k47", "is not a value")]
    [InlineData("4.7.1k", "is not a value")]
    [InlineData("0", "above zero")]
    [InlineData("-5k", "above zero")]
    public void RefusesBadTextAndSaysWhy(string text, string reason)
    {
        Assert.False(SchematicEdits.TryParseValue(text, out _, out var error));
        Assert.Contains(reason, error);
    }

    [Theory]
    [InlineData(47e3, "47k")]
    [InlineData(4.7e-6, "4.7u")]
    [InlineData(10e-9, "10n")]
    [InlineData(1e6, "1meg")]
    [InlineData(100, "100")]
    [InlineData(2.2e-3, "2.2m")]
    [InlineData(820e3, "820k")]
    public void FormatsForTheNetlist(double value, string expected) => Assert.Equal(expected, SchematicEdits.FormatValue(value));

    [Theory]
    [InlineData(1e3, 1, 1.2e3)]
    [InlineData(1e3, -1, 820)]
    [InlineData(8.2e3, 1, 10e3)]
    [InlineData(10e3, -1, 8.2e3)]
    [InlineData(4.7e3, 1, 5.6e3)]
    [InlineData(4.7e3, -1, 3.9e3)]
    [InlineData(5e3, 1, 5.6e3)]
    [InlineData(5e3, -1, 4.7e3)]
    [InlineData(10e-9, -1, 8.2e-9)]
    [InlineData(8.2e-9, 1, 10e-9)]
    [InlineData(1, -1, 0.82)]
    public void StepsThroughE12(double from, int direction, double expected) =>
        Assert.Equal(expected, SchematicEdits.StepE12(from, direction), expected * 1e-9);

    const string Divider = "* divider\nV1 in 0 1\nR1 in a 10k\nR2 a 0 10k\n.END\n";

    readonly List<SchematicChange> changes = [];

    IRenderedComponent<SchematicEditor> Select(string reference)
    {
        var editor = Render<SchematicEditor>(p => p
            .Add(c => c.Netlist, Divider)
            .Add(c => c.Changed, (SchematicChange change) => changes.Add(change)));
        editor.Find($"rect.part[data-ref=\"{reference}\"]").Click();
        return editor;
    }

    [Fact]
    public void TypingAValueRewritesTheLine()
    {
        var editor = Select("R1");
        var box = editor.Find("input.value-edit");
        Assert.Equal("10k", box.GetAttribute("value"));
        box.Input("4u7");
        editor.Find("input.value-edit").KeyDown("Enter");
        Assert.Contains("R1 in a 4.7u", changes.Last().Netlist);
        Assert.Contains("R2 a 0 10k", changes.Last().Netlist);
    }

    [Fact]
    public void UpAndDownStepE12()
    {
        var editor = Select("R1");
        editor.Find("input.value-edit").KeyDown("ArrowUp");
        Assert.Contains("R1 in a 12k", changes.Last().Netlist);
        editor.Find("input.value-edit").KeyDown("ArrowDown");
        editor.Find("input.value-edit").KeyDown("ArrowDown");
        Assert.Contains("R1 in a 8.2k", changes.Last().Netlist);
    }

    [Fact]
    public void BadInputSaysWhyAndChangesNothing()
    {
        var editor = Select("R1");
        editor.Find("input.value-edit").Input("47x");
        editor.Find("input.value-edit").KeyDown("Enter");
        Assert.Contains("is not a value", editor.Find(".value-error").TextContent);
        Assert.Empty(changes);
    }
}
