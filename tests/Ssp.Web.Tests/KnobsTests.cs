using Bunit;
using Microsoft.AspNetCore.Components;
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
    public async Task ManyFastChangesGiveOneChange()
    {
        // NOTE: The debounce is far longer than any burst, so a slow runner cannot split the burst in two.
        var debounce = TimeSpan.FromSeconds(3);
        var changes = new List<string>();
        var knobs = Render<Knobs>(p => p
            .Add(c => c.Netlist, Circuit("fixtures", "pot-lowpass.cir"))
            .Add(c => c.Debounce, debounce)
            .Add(c => c.NetlistChanged, (string n) => { lock (changes) changes.Add(n); }));

        foreach (var pos in new[] { "0.1", "0.2", "0.3", "0.4", "0.6", "0.7", "0.8", "0.9", "1" })
        {
            // NOTE: Find and Input in one dispatch, so a render cannot remove the handler between them.
            await knobs.InvokeAsync(() => knobs.Find(".knobs input[type=range]").Input(pos));
        }
        lock (changes) Assert.Empty(changes);

        // NOTE: The callback does not render the component, so WaitForAssertion would not check again. Poll instead.
        Assert.True(SpinWait.SpinUntil(() => { lock (changes) return changes.Count == 1; }, debounce * 4));
        // NOTE: Wait past another debounce window to show that no late change follows.
        await Task.Delay(debounce);
        lock (changes) Assert.Single(changes);
        Assert.Contains("* ssp:knob RV1 log 1", changes[0]);
    }

    // NOTE: Other tests block pool threads. The pool adds a thread about every half second then, so the debounce timer fires late.
    [Fact]
    public void ChangeRunsWhilePoolThreadsAreBlocked()
    {
        var (page, _) = Run(Circuit("fixtures", "pot-lowpass.cir"));
        var before = OutMagnitude(page);
        var gate = new ManualResetEventSlim();
        for (var i = 0; i < Environment.ProcessorCount * 4; i++) { Task.Run(() => gate.Wait()); }
        Task.Run(async () => { await Task.Delay(8000); gate.Set(); });
        page.Find(".knobs input[type=range]").Input("0.9");
        page.WaitForAssertion(() => Assert.NotEqual(before, OutMagnitude(page)), TimeSpan.FromSeconds(5));
    }
}
