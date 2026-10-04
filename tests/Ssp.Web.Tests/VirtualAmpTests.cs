using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Core;
using Ssp.Core.Chains;
using Ssp.Core.Netlist;
using Ssp.Web.Components;
using Ssp.Web.Hosting;

namespace Ssp.Web.Tests;

public class VirtualAmpTests : BunitContext
{
    sealed class FakeHost : ISimulationHost
    {
        public List<string> Renders { get; } = [];

        public Task<RunResult> Run(string netlist, RunOptions options) => throw new NotSupportedException();

        /// <summary>The time that a render takes. A render of zero time ends before the page draws again.</summary>
        public TimeSpan Delay { get; set; }

        public async Task<double[]> Render(string netlist, double[] input, int sampleRate, int oversample)
        {
            Renders.Add(netlist);
            await Task.Delay(Delay);
            return new double[input.Length];
        }

        public Task<SweepResult> Sweep(string netlist, string reference, IReadOnlyList<double> values, RunOptions options) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> Versions() => throw new NotSupportedException();
    }

    readonly FakeHost host = new();

    public VirtualAmpTests()
    {
        Services.AddSingleton<ISimulationHost>(host);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    static ChainStage Stage(string name, string block) =>
        new(name, File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "blocks", block)));

    static string Errors(string netlist) =>
        string.Join("\n", NetlistLoader.Load(netlist).Diagnostics.Where(d => d.Severity == Severity.Error).Select(d => d.Message));

    [Fact]
    public void BlockListsHaveOneGroupForEachSection()
    {
        var panel = Render<VirtualAmp>();

        Assert.Contains("gain-high-lm308.cir", panel.FindAll("select[data-stage=gain] option").Select(o => o.GetAttribute("value")));
        Assert.Contains("tone-mid-notch.cir", panel.FindAll("select[data-stage=tone] option").Select(o => o.GetAttribute("value")));
        Assert.Equal(["power-9v.cir"], panel.FindAll("select[data-stage=power] option").Select(o => o.GetAttribute("value")));
    }

    [Fact]
    public void PresetChainLoadsWithNoErrorDiagnostic()
    {
        var panel = Render<VirtualAmp>();

        Assert.Equal("", Errors(panel.Find("textarea.amp-netlist").TextContent));
    }

    [Fact]
    public void PickedStagesGiveTheComposedNetlist()
    {
        var panel = Render<VirtualAmp>();

        panel.Find("select[data-stage=gain]").Change("gain-mid-jrc4558.cir");
        panel.Find("select[data-stage=tone]").Change("tone-baxandall-passive.cir");
        panel.Find("select[data-stage=power]").Change("power-9v.cir");

        var expected = Chain.Compose(
            [Stage("gain", "gain-mid-jrc4558.cir"), Stage("tone", "tone-baxandall-passive.cir"), Stage("power", "power-9v.cir")]);
        Assert.Equal(expected.ReplaceLineEndings("\n"), panel.Find("textarea.amp-netlist").TextContent.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task TryRendersTheNetlistOnce()
    {
        var panel = Render<VirtualAmp>();
        panel.Find("select[data-stage=gain]").Change("gain-mid-jrc4558.cir");
        panel.Find("select[data-stage=tone]").Change("tone-baxandall-passive.cir");
        panel.Find("select[data-stage=power]").Change("power-9v.cir");

        panel.Find("button.amp-try").Click();

        await panel.WaitForAssertionAsync(() => Assert.Single(host.Renders));
        var expected = Chain.Compose(
            [Stage("gain", "gain-mid-jrc4558.cir"), Stage("tone", "tone-baxandall-passive.cir"), Stage("power", "power-9v.cir")]);
        Assert.Equal(expected, host.Renders[0]);
    }

    // NOTE: Try calls the clip player from the panel. The status must change without an event in the clip player (#148).
    [Fact]
    public async Task TryStatusDoesNotStayRendering()
    {
        host.Delay = TimeSpan.FromMilliseconds(100);
        var panel = Render<VirtualAmp>();

        panel.Find("button.amp-try").Click();

        await panel.WaitForAssertionAsync(() => Assert.Single(host.Renders));
        await panel.WaitForAssertionAsync(() => Assert.False(panel.Find("button.clip-play").HasAttribute("disabled")), TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("Rendering", panel.Find(".clip-status").TextContent);
    }

    [Fact]
    public void EachStageKnobGivesOneSlider()
    {
        var panel = Render<VirtualAmp>();
        panel.Find("select[data-stage=gain]").Change("gain-variable-tl072.cir");
        panel.Find("select[data-stage=tone]").Change("tone-baxandall-passive.cir");

        Assert.Equal(["gain.RV1", "tone.RV1", "tone.RV2"], panel.FindAll(".knobs .part").Select(e => e.TextContent));
    }

    [Fact]
    public async Task KnobChangeChangesTheRenderedOutput()
    {
        var panel = Render<VirtualAmp>();
        panel.Find("select[data-stage=gain]").Change("gain-variable-tl072.cir");
        panel.Find("button.amp-try").Click();
        await panel.WaitForAssertionAsync(() => Assert.Single(host.Renders));

        panel.Find(".knobs input[type=range]").Input("0.9");
        await panel.WaitForAssertionAsync(() => Assert.Contains("gain.RV1 linear 0.9", panel.Find("textarea.amp-netlist").TextContent), TimeSpan.FromSeconds(5));
        panel.Find("button.amp-try").Click();
        await panel.WaitForAssertionAsync(() => Assert.Equal(2, host.Renders.Count));

        // NOTE: The worker runs WorkerExports.RenderJson, so the test renders a short sine through it.
        var input = Enumerable.Range(0, 400).Select(i => 0.1 * Math.Sin(2 * Math.PI * 440 * i / 44_100)).ToArray();
        var before = Samples(host.Renders[0], input);
        var after = Samples(host.Renders[1], input);
        Assert.NotEqual(before, after);
        Assert.Contains(before, s => s != 0);
    }

    static double[] Samples(string netlist, double[] input) =>
        System.Text.Json.JsonSerializer.Deserialize<double[]>(WorkerExports.RenderJson(netlist, input, 44_100, 1))!;
}
