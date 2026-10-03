using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Web.Hosting;
using Ssp.Web.Pages;
using Ssp.Web.Sharing;

namespace Ssp.Web.Tests;

public class EditorPageTests : BunitContext
{
    static string Fixture(string name) => File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", name));

    IRenderedComponent<Editor> Run(string netlist)
    {
        Services.AddSingleton<ISimulationHost, InProcessSimulationHost>();
        var page = Render<Editor>();
        page.Find("textarea").Input(netlist);
        page.Find("button").Click();
        return page;
    }

    [Fact]
    public void VoltageTableShowsEachNode()
    {
        var page = Run(Fixture("divider-basic.cir"));

        var rows = page.FindAll("table.voltages tbody tr")
            .Select(r => r.QuerySelectorAll("td").Select(td => td.TextContent.Trim()).ToArray())
            .ToList();
        // NOTE: Vout = 1 V * 2k / (1k + 2k). Node 0 is ground and is not in the table.
        Assert.Equal(
            [["in", "1 V"], ["out", "0.6667 V"]],
            rows);
    }

    [Fact]
    public void DiagnosticsShowWithSeverity()
    {
        var page = Run(Fixture("diag-dangling-pin.cir"));

        var item = Assert.Single(page.FindAll("ul.diagnostics li"));
        Assert.Contains("warning", item.ClassList);
        Assert.StartsWith("Warning", item.TextContent.Trim());
        Assert.Contains("R3", item.TextContent);
        Assert.NotEmpty(page.FindAll("table.voltages tbody tr"));
    }

    IRenderedComponent<Editor> Open(string hash)
    {
        Services.AddSingleton<ISimulationHost, InProcessSimulationHost>();
        Services.GetRequiredService<BunitNavigationManager>().NavigateTo("editor#" + hash);
        return Render<Editor>();
    }

    [Fact]
    public void HashLoadsNetlistIntoEditor()
    {
        var netlist = Fixture("divider-basic.cir");

        var page = Open(ShareCodec.Encode(netlist));

        // NOTE: A textarea value has LF line endings. A Windows checkout gives CRLF fixtures.
        Assert.Equal(netlist.Replace("\r\n", "\n"), page.Find("textarea").GetAttribute("value"));
        Assert.Empty(page.FindAll("ul.diagnostics li"));
    }

    [Fact]
    public void BadHashShowsDiagnosticAndEmptyEditor()
    {
        var page = Open("not-a-netlist");

        Assert.Equal("", page.Find("textarea").GetAttribute("value"));
        var item = Assert.Single(page.FindAll("ul.diagnostics li"));
        Assert.Contains("error", item.ClassList);
        Assert.Contains("share link", item.TextContent);
    }

    [Fact]
    public void ShareLinkCarriesNetlist()
    {
        var netlist = Fixture("divider-basic.cir");
        Services.AddSingleton<ISimulationHost, InProcessSimulationHost>();
        var page = Render<Editor>();

        page.Find("textarea").Input(netlist);

        var href = page.Find("a.share").GetAttribute("href")!;
        Assert.Equal(netlist, ShareCodec.Decode(href[(href.IndexOf('#') + 1)..]));
    }

    [Fact]
    public void BodePlotShowsDbAndPhase()
    {
        var page = Run(Fixture("rc-lowpass.cir"));

        var labels = page.FindAll(".bode svg.line-chart text.axis-label.y").Select(t => t.TextContent).ToList();
        Assert.Equal(["Magnitude (dB)", "Phase (°)"], labels);
        Assert.NotEmpty(page.FindAll(".bode svg.line-chart path.series"));
    }

    [Fact]
    public void ImpedancePlotShowsInputAndOutput()
    {
        var page = Run(Fixture("rc-lowpass.cir"));

        var chart = page.Find(".impedance svg.line-chart");
        Assert.Equal("Impedance (Ω)", chart.QuerySelector("text.axis-label.y")!.TextContent);
        Assert.Equal(["input", "output"], chart.QuerySelectorAll("path.series title").Select(t => t.TextContent));
    }

    [Fact]
    public void ErrorShowsNoTable()
    {
        var page = Run(Fixture("diag-no-ground.cir"));

        var item = Assert.Single(page.FindAll("ul.diagnostics li"));
        Assert.Contains("error", item.ClassList);
        Assert.StartsWith("Error", item.TextContent.Trim());
        Assert.Empty(page.FindAll("table.voltages"));
        Assert.Empty(page.FindAll("svg.line-chart"));
    }
}
