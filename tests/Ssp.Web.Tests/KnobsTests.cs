using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Core;
using Ssp.Web.Components;
using Ssp.Web.Hosting;
using Ssp.Web.Pages;

namespace Ssp.Web.Tests;

public class KnobsTests : BunitContext
{
    static string Circuit(string folder, string name) => File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", folder, name));

    sealed class CountingHost : ISimulationHost
    {
        readonly InProcessSimulationHost inner = new();
        public int Runs;

        public Task<RunResult> Run(string netlist, RunOptions options)
        {
            Interlocked.Increment(ref Runs);
            return inner.Run(netlist, options);
        }

        public Task<double[]> Render(string netlist, double[] input, int sampleRate, int oversample) => inner.Render(netlist, input, sampleRate, oversample);
        public Task<SweepResult> Sweep(string netlist, string reference, IReadOnlyList<double> values, RunOptions options) => inner.Sweep(netlist, reference, values, options);
        public Task<IReadOnlyList<string>> Versions() => inner.Versions();
    }

    (IRenderedComponent<Editor> Page, CountingHost Host) Run(string netlist)
    {
        var host = new CountingHost();
        Services.AddSingleton<ISimulationHost>(host);
        var page = Render<Editor>();
        page.Find("textarea").Input(netlist);
        page.Find("button").Click();
        return (page, host);
    }

    // NOTE: The first magnitude chart has one path for each node. The input node is flat, so read the output node.
    static string? OutMagnitude(IRenderedComponent<Editor> page) =>
        page.FindAll(".bode svg.line-chart")[0].QuerySelectorAll("path.series").Single(p => p.QuerySelector("title")!.TextContent == "out").GetAttribute("d");

    [Fact]
    public void EachKnobGivesOneSlider()
    {
        var knobs = Render<Knobs>(p => p.Add(c => c.Netlist, Circuit("blocks", "tone-baxandall-passive.cir")));

        var sliders = knobs.FindAll(".knobs input[type=range]");
        Assert.Equal(2, sliders.Count);
        Assert.Equal(["RV1", "RV2"], knobs.FindAll(".knobs .part").Select(e => e.TextContent));
    }

    [Fact]
    public void SliderShowsTaperAndPosition()
    {
        var knobs = Render<Knobs>(p => p.Add(c => c.Netlist, Circuit("fixtures", "pot-lowpass.cir")));

        Assert.Equal("log", knobs.Find(".knobs .taper").TextContent);
        Assert.Equal("50%", knobs.Find(".knobs .position").TextContent);
        Assert.Equal("0.5", knobs.Find(".knobs input[type=range]").GetAttribute("value"));
    }

    [Fact]
    public void SliderChangeChangesBodeSeries()
    {
        var (page, _) = Run(Circuit("fixtures", "pot-lowpass.cir"));
        var before = OutMagnitude(page);

        page.Find(".knobs input[type=range]").Input("0.9");

        page.WaitForAssertion(() =>
        {
            Assert.NotEqual(before, OutMagnitude(page));
            Assert.Equal("90%", page.Find(".knobs .position").TextContent);
        }, TimeSpan.FromSeconds(5));
        Assert.Contains("* ssp:knob RV1 log 0.9", page.Find("textarea").GetAttribute("value"));
    }

    [Fact]
    public void ManyFastChangesGiveOneRun()
    {
        var (page, host) = Run(Circuit("fixtures", "pot-lowpass.cir"));
        Assert.Equal(1, host.Runs);

        foreach (var pos in new[] { "0.1", "0.2", "0.3", "0.4", "0.6", "0.7", "0.8", "0.9", "1" })
        {
            page.Find(".knobs input[type=range]").Input(pos);
        }
        Assert.Equal(1, host.Runs);

        page.WaitForAssertion(() => Assert.Equal(2, host.Runs), TimeSpan.FromSeconds(5));
        // NOTE: Wait past more debounce windows to show that no late run follows.
        Thread.Sleep(Knobs.DefaultDebounce * 3);
        Assert.Equal(2, host.Runs);
        Assert.Contains("* ssp:knob RV1 log 1", page.Find("textarea").GetAttribute("value"));
    }
}
