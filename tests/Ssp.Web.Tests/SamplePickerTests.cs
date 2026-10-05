using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Core;
using Ssp.Core.Audio;
using Ssp.Web.Components;
using Ssp.Web.Hosting;
using Ssp.Web.Projects;

namespace Ssp.Web.Tests;

public class SamplePickerTests : BunitContext
{
    sealed class CountingHost : ISimulationHost
    {
        public List<double[]> Inputs { get; } = [];

        public Task<RunResult> Run(string netlist, RunOptions options) => throw new NotSupportedException();

        public Task<double[]> Render(string netlist, double[] input, int sampleRate, int oversample)
        {
            Inputs.Add(input);
            return Task.FromResult(input.Select(x => x * 0.5).ToArray());
        }

        public Task<SweepResult> Sweep(string netlist, string reference, IReadOnlyList<double> values, RunOptions options) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> Versions() => throw new NotSupportedException();
    }

    readonly CountingHost host = new();
    readonly MemoryStore storage = new();
    readonly SampleChoice choice;

    public SamplePickerTests()
    {
        Services.AddSingleton<ISimulationHost>(host);
        Services.AddSingleton(TimeProvider.System);
        JSInterop.Mode = JSRuntimeMode.Loose;
        choice = new SampleChoice(storage);
    }

    // The Clip tab and the Virtual amp side by side, as the editor shows them, with one shared choice.
    IRenderedComponent<CascadingValue<SampleChoice>> Panels(RenderCache? cache = null) =>
        Render<CascadingValue<SampleChoice>>(p => p
            .Add(c => c.Value, choice)
            .AddChildContent<ClipPlayer>(c => c.Add(x => x.Netlist, "V1 in 0 1\n.END\n").Add(x => x.Cache, cache))
            .AddChildContent<VirtualAmp>(c => c.Add(x => x.Cache, cache)));

    static byte[] WavBytes(int rate, double seconds, int channels = 1)
    {
        var frames = (int)(seconds * rate);
        var data = new WavData(rate, [.. Enumerable.Range(0, channels).Select(_ => Enumerable.Range(0, frames).Select(i => Math.Sin(i * 0.05) * 0.1).ToArray())]);
        using var stream = new MemoryStream();
        Wav.Write(stream, data, 16);
        return stream.ToArray();
    }

    static void Upload(IRenderedComponent<CascadingValue<SampleChoice>> panels, byte[] bytes, string name)
    {
        var input = panels.FindComponent<InputFile>();
        panels.InvokeAsync(() => input.UploadFiles(InputFileContent.CreateFromBinary(bytes, name))).GetAwaiter().GetResult();
    }

    static void PickOption(IRenderedComponent<CascadingValue<SampleChoice>> panels, string selector, string value) =>
        panels.InvokeAsync(() => panels.Find(selector).Change(value)).GetAwaiter().GetResult();

    [Fact]
    public void TheChoiceInTheClipTabShowsInTheVirtualAmp()
    {
        var panels = Panels();
        var clip = ".clip-player .sample-select";
        var amp = ".virtual-amp > .sample-picker .sample-select";

        PickOption(panels, clip, "sweep");

        Assert.Equal("sweep", panels.Find(amp).GetAttribute("value"));
        Assert.Equal("Sample: Sweep 20 Hz-20 kHz", panels.Find(".virtual-amp .clip-source").TextContent);
        Assert.Equal("Sample: Sweep 20 Hz-20 kHz", panels.Find(".virtual-amp .amp-sample").TextContent);
        Assert.Equal("Sample: Sweep 20 Hz-20 kHz", panels.Find(".clip-player > .clip-source").TextContent);
    }

    [Fact]
    public void AnUploadShowsItsNameInBothPanels()
    {
        var panels = Panels();
        PickOption(panels, ".clip-player .sample-select", "__load");

        Upload(panels, WavBytes(48_000, 0.5, 2), "mine.wav");

        Assert.All(panels.FindAll(".clip-source"), p => Assert.Equal("Sample: mine.wav", p.TextContent));
        Assert.Equal("Sample: mine.wav", panels.Find(".amp-sample").TextContent);
        var info = panels.Find(".clip-player .sample-info").TextContent;
        Assert.Equal("mine.wav, 48000 Hz, 0.5 s, mono at 44100 Hz", info);
        Assert.Contains("mine.wav", panels.Find(".virtual-amp > .sample-picker .sample-select").InnerHtml);
        Assert.Equal(22_050, choice.Samples().Samples.Length);
    }

    [Fact]
    public void ALongFileSaysSoAndOffersTheFirstTenSeconds()
    {
        var panels = Panels();
        PickOption(panels, ".clip-player .sample-select", "__load");

        Upload(panels, WavBytes(8_000, 12), "long.wav");

        Assert.Contains("long.wav is 12.0 s. The limit is 10 s.", panels.Find(".clip-player .sample-long").TextContent);
        Assert.Equal("Bundled clip", choice.Name);

        Press(panels, ".clip-player .sample-first");

        Assert.Equal("long.wav", choice.Name);
        Assert.Equal(10 * SampleChoice.ProjectRate, choice.Samples().Samples.Length);
        Assert.Empty(panels.FindAll(".sample-long"));
    }

    [Fact]
    public async Task TheChoiceSurvivesAReload()
    {
        await choice.Select("noise");

        var reloaded = new SampleChoice(storage);
        Assert.Equal("clip", reloaded.Id);
        await reloaded.Restore();

        Assert.Equal("noise", reloaded.Id);
    }

    [Fact]
    public async Task AProjectKeepsTheChoice()
    {
        var store = new ProjectStore(storage, TimeProvider.System);
        var saved = await store.Save(ProjectStore.New("p", "V1 in 0 1\n", null) with { Sample = "chord" });

        var loaded = await store.Load(saved.Id);
        choice.Adopt(loaded!.Sample);

        Assert.Equal("chord", choice.Id);
    }

    [Fact]
    public void TheRenderKeyChangesWithTheSample()
    {
        var cache = new RenderCache();
        var panels = Panels(cache);
        Press(panels, ".clip-player button.clip-play");
        var first = cache.Latest!.Value.Key;

        PickOption(panels, ".clip-player .sample-select", "click");
        Press(panels, ".clip-player button.clip-play");
        var second = cache.Latest!.Value.Key;

        Assert.NotEqual(first, second);
        Assert.Equal(("clip", "click"), (first.SampleId, second.SampleId));
        Assert.Equal(2, host.Inputs.Count);

        PickOption(panels, ".clip-player .sample-select", "clip");
        Press(panels, ".clip-player button.clip-play");

        Assert.Equal(2, host.Inputs.Count);
    }

    static void Press(IRenderedComponent<CascadingValue<SampleChoice>> panels, string selector) =>
        panels.InvokeAsync(() => panels.Find(selector).Click()).GetAwaiter().GetResult();

    [Fact]
    public void EachBundledSampleLoadsWithItsStatedLengthAtTheProjectRate()
    {
        Assert.Equal(10, SampleChoice.Bundled.Count);
        foreach (var bundled in SampleChoice.Bundled)
        {
            var c = new SampleChoice();
            c.Adopt(bundled.Id);
            var (samples, rate) = c.Samples();
            Assert.Equal(SampleChoice.ProjectRate, rate);
            Assert.Equal(bundled.Seconds, samples.Length / (double)rate, 3);
        }
    }

    [Fact]
    public void TheSineIsAtMinus20DbFs()
    {
        var c = new SampleChoice();
        c.Adopt("sine");

        Assert.Equal(-20, 20 * Math.Log10(c.Samples().Samples.Max(Math.Abs)), 0.01);
    }
}
