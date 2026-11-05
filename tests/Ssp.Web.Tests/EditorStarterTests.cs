using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Web.Hosting;
using Ssp.Web.Pages;
using Ssp.Web.Sharing;

namespace Ssp.Web.Tests;

public class EditorStarterTests : BunitContext
{
    public EditorStarterTests() => Services.AddSingleton<TimeProvider>(new ManualTimeProvider());

    static string Fixture(string name) => File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", name));

    IRenderedComponent<Editor> Open(string? hash = null)
    {
        Services.AddSingleton<ISimulationHost, InProcessSimulationHost>();
        if (hash is not null)
        {
            Services.GetRequiredService<BunitNavigationManager>().NavigateTo("editor#" + hash);
        }
        return Render<Editor>();
    }

    [Fact]
    public void OpensWithTheStarterWhenThereIsNoSharedCircuit()
    {
        var page = Open();

        var box = page.Find("textarea").GetAttribute("value") ?? page.Find("textarea").TextContent;
        Assert.NotEmpty(box.Trim());
        Assert.Equal(Fixture("rc-lowpass.cir").ReplaceLineEndings("\n"), box.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void SharedCircuitWinsOverTheStarter()
    {
        var shared = Fixture("divider-basic.cir");

        var page = Open(ShareCodec.Encode(shared));

        var box = page.Find("textarea").GetAttribute("value") ?? page.Find("textarea").TextContent;
        Assert.Equal(shared.ReplaceLineEndings("\n"), box.ReplaceLineEndings("\n"));
    }

    // #417: the gallery opens a library circuit by its netlist. The editor draws it from the hand layout of the library, not
    // from a fresh placement.
    [Fact]
    public void ALibraryCircuitOpensWithItsHandLayout()
    {
        var circuit = Ssp.Web.Library.CircuitLibrary.All.First(c => c.Layout is not null);
        var hand = Ssp.Core.Layout.Layout.Parse(circuit.Layout!);

        var page = Open(ShareCodec.Encode(circuit.Netlist));

        var drawn = page.FindAll("svg polyline[data-net]").Select(p => p.GetAttribute("points")).ToHashSet();
        foreach (var wire in hand.Wires)
        {
            var points = string.Join(' ', wire.Points.Select(p => FormattableString.Invariant($"{p.X},{p.Y}")));
            Assert.Contains(points, drawn);
        }
    }

    [Fact]
    public void RunOnTheStarterGivesAResult()
    {
        var page = Open();

        page.Find("button.run").Click();

        Assert.NotEmpty(page.FindAll("table.voltages tbody tr"));
    }
}
