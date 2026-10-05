using System.Text.RegularExpressions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Core;
using Ssp.Core.Netlist;
using Ssp.Web.Components;
using Ssp.Web.Hosting;
using Ssp.Web.Pages;

namespace Ssp.Web.Tests;

public class EditorControlsTests : BunitContext
{
    static string Fixture(string name) => File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", name));

    sealed class RejectingHost : ISimulationHost
    {
        public Task<RunResult> Run(string netlist, RunOptions options) => Task.FromResult(new RunResult(null, null, null, null,
            [new Diagnostic(Severity.Error, "The solver rejects the circuit. Check V1: the way that the part is connected is not valid.", null)],
            new Dictionary<string, double>()));

        public Task<double[]> Render(string netlist, double[] input, int sampleRate, int oversample) => throw new NotSupportedException();
        public Task<SweepResult> Sweep(string netlist, string reference, IReadOnlyList<double> values, RunOptions options) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> Versions() => throw new NotSupportedException();
    }

    public EditorControlsTests()
    {
        Services.AddSingleton<TimeProvider>(new ManualTimeProvider());
        Services.AddSingleton<ISimulationHost, InProcessSimulationHost>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public static TheoryData<double, string, string> Cases => new()
    {
        { 4.453e-11, "V", "44.5 pV" },
        { 4.5, "V", "4.5 V" },
        { 1e-8, "F", "10 nF" },
        { 0, "V", "0 V" },
        { -0.0032, "V", "-3.2 mV" },
        { 4700, "Ω", "4.7 kΩ" },
        { 2.2e6, "Ω", "2.2 MΩ" },
        { 999.96, "Hz", "1 kHz" },
        { 440, "", "440" },
        { 1e-18, "V", "0.001 fV" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void FormatterGivesPlainEngineeringNotation(double value, string unit, string expected) =>
        Assert.Equal(expected, Eng.Format(value, unit));

    [Fact]
    public void AmpPickersAreSeparateLabelledControlsInOneColumn()
    {
        var amp = Render<VirtualAmp>();

        var rows = amp.FindAll(".amp-stack > label.amp-stage");
        Assert.Equal(3, rows.Count);
        foreach (var row in rows)
        {
            Assert.Single(row.QuerySelectorAll("select"));
            Assert.NotEmpty(row.QuerySelector("span")!.TextContent);
        }
        Assert.Equal(["Gain stage", "Tone stack", "Power section"], rows.Select(r => r.QuerySelector("span")!.TextContent));
    }

    [Fact]
    public void ToolbarHasNineButtons()
    {
        var page = Render<Editor>();

        var bar = page.Find(".toolbar");
        Assert.Equal(["Run", "Undo", "Redo", "Export", "Share link", "Download SVG", "Download PDF", "Parts CSV", "Parts"], bar.QuerySelectorAll("button").Select(b => b.TextContent));
        Assert.Empty(bar.QuerySelectorAll("a"));
        Assert.True(page.Find(".toolbar .undo").HasAttribute("disabled"));
        Assert.True(page.Find(".toolbar .redo").HasAttribute("disabled"));
    }

    [Fact]
    public void KnobRowShowsNameValueAndUnit()
    {
        var amp = Render<VirtualAmp>();

        var knob = amp.Find(".knob");
        Assert.NotEmpty(knob.QuerySelector(".part")!.TextContent);
        Assert.Matches(@"^\d+%$", knob.QuerySelector(".position")!.TextContent);
    }

    [Fact]
    public void EveryPanelSaysWhatToDoFirstBeforeRun()
    {
        var page = Render<Editor>();

        foreach (var panel in page.FindAll(".dock-panel"))
        {
            Assert.True(panel.QuerySelector(".panel-hint") is not null || panel.QuerySelector("textarea, svg, .calculator-panel, .schematic-editor") is not null,
                $"Panel has no hint: {panel.GetAttribute("id") ?? panel.OuterHtml[..Math.Min(120, panel.OuterHtml.Length)]}");
        }
        Assert.Contains("Run to plot", page.Markup);
    }

    [Fact]
    public void NoPanelShowsScientificNotationAfterRun()
    {
        var page = Render<Editor>();

        page.Find(".toolbar .run").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("table.voltages td")), TimeSpan.FromSeconds(30));

        var text = string.Join(" ", page.FindAll(".dock-panel").Select(PanelText));
        Assert.DoesNotMatch(new Regex(@"\d[eE][+-]?\d"), text);
    }

    // NOTE: A netlist box holds the user's own text, such as 1E+12. It is input, not a panel value.
    static string PanelText(AngleSharp.Dom.IElement panel)
    {
        var text = panel.TextContent;
        foreach (var box in panel.QuerySelectorAll("textarea"))
        {
            text = box.TextContent.Length == 0 ? text : text.Replace(box.TextContent, "");
        }
        return text;
    }

    [Fact]
    public void SolverRejectionShowsInWordsNextToTheToolbar()
    {
        Services.AddSingleton<ISimulationHost>(new RejectingHost());
        var page = Render<Editor>();

        page.Find(".toolbar .run").Click();

        page.WaitForAssertion(() => Assert.Contains("The solver rejects the circuit", page.Find(".run-errors").TextContent), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ControlEnterRuns()
    {
        var page = Render<Editor>();

        page.Find(".editor").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", CtrlKey = true });

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("table.voltages td")), TimeSpan.FromSeconds(30));
    }
}
