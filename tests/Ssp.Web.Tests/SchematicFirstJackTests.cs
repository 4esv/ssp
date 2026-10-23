using Bunit;
using Ssp.Web.Components;
using Ssp.Web.Schematic;

namespace Ssp.Web.Tests;

public class SchematicFirstJackTests : BunitContext
{
    [Theory]
    [InlineData("jack-in", "* ssp:input ")]
    [InlineData("jack-out", "* ssp:output ")]
    public void A_jack_on_an_empty_circuit_places_a_part_and_the_directive(string kind, string directive)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var seen = new List<SchematicChange>();
        var editor = Render<SchematicEditor>(p => p
            .Add(c => c.Netlist, "")
            .Add(c => c.Changed, (SchematicChange c) => seen.Add(c)));
        Assert.Contains("Tap a part in the palette to start", editor.Find("p.schematic-empty").TextContent);

        editor.Find($"button.palette-item[data-kind=\"{kind}\"]").Click();

        var change = Assert.Single(seen);
        Assert.Contains(directive, change.Netlist);
        Assert.Single(change.Layout.Parts);
        Assert.DoesNotContain("Tap a pin first", editor.Markup);
    }
}
