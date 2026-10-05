using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Core;
using Ssp.Web.Components;
using Ssp.Web.Hosting;

namespace Ssp.Web.Tests;

public class ClipPlayerCacheTests : BunitContext
{
    sealed class CountingHost : ISimulationHost
    {
        public List<string> Renders { get; } = [];

        public Task<RunResult> Run(string netlist, RunOptions options) => throw new NotSupportedException();

        public Task<double[]> Render(string netlist, double[] input, int sampleRate, int oversample)
        {
            Renders.Add(netlist);
            return Task.FromResult(input.Select(x => x * 0.5).ToArray());
        }

        public Task<SweepResult> Sweep(string netlist, string reference, IReadOnlyList<double> values, RunOptions options) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> Versions() => throw new NotSupportedException();
    }

    const string OutOfDate = "Out of date: the circuit changed. Press Play to render it.";

    readonly CountingHost host = new();
    readonly ManualTimeProvider clock = new();

    public ClipPlayerCacheTests()
    {
        Services.AddSingleton<ISimulationHost>(host);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    static string Circuit(int n) => $"V1 in 0 {n}\n.END\n";

    IRenderedComponent<ClipPlayer> Player(string netlist, RenderCache? cache = null) =>
        Render<ClipPlayer>(p => p.Add(c => c.Clock, clock).Add(c => c.Netlist, netlist).Add(c => c.Cache, cache));

    static void Press(IRenderedComponent<ClipPlayer> player, string selector) =>
        player.InvokeAsync(() => player.Find(selector).Click()).GetAwaiter().GetResult();

    static void SetNetlist(IRenderedComponent<ClipPlayer> player, string netlist) =>
        player.Render(p => p.Add(c => c.Netlist, netlist));

    [Fact]
    public void PlayTwiceWithTheSameKeyRendersOnce()
    {
        var player = Player(Circuit(1));

        Press(player, "button.clip-play");
        Press(player, "button.clip-play");

        Assert.Single(host.Renders);
        Assert.Equal(2, JSInterop.Invocations.Count(i => i.Identifier == "play"));
        Assert.Empty(player.FindAll("p.clip-stale"));
    }

    [Fact]
    public void AKnobChangeMarksItOutOfDateAndTheNextPlayRenders()
    {
        var player = Player(Circuit(1));
        Press(player, "button.clip-play");

        SetNetlist(player, Circuit(2));

        Assert.Equal(OutOfDate, player.Find("p.clip-stale").TextContent);
        Assert.Single(host.Renders);

        Press(player, "button.clip-play");

        Assert.Equal([Circuit(1), Circuit(2)], host.Renders);
        Assert.Empty(player.FindAll("p.clip-stale"));
    }

    [Fact]
    public void PlayOldPlaysTheOldResultWithoutRendering()
    {
        var player = Player(Circuit(1));
        Press(player, "button.clip-play");
        SetNetlist(player, Circuit(2));
        var plays = JSInterop.Invocations.Count(i => i.Identifier == "play");

        Press(player, "button.clip-play-old");

        Assert.Single(host.Renders);
        Assert.Equal(plays + 1, JSInterop.Invocations.Count(i => i.Identifier == "play"));
        Assert.NotEmpty(player.FindAll("p.clip-stale"));
    }

    [Fact]
    public void ThePlayerDoesNotRenderByItselfWhenTheCircuitChanges()
    {
        var player = Player(Circuit(1));
        Press(player, "button.clip-play");

        SetNetlist(player, Circuit(2));
        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Single(host.Renders);
    }

    [Fact]
    public void RenderAgainRendersEvenWithTheSameKey()
    {
        var player = Player(Circuit(1));
        Press(player, "button.clip-play");

        Press(player, "button.clip-render-again");

        Assert.Equal([Circuit(1), Circuit(1)], host.Renders);
    }

    [Fact]
    public void TheSixthDistinctKeyDropsTheOldest()
    {
        var player = Player(Circuit(0));
        for (var n = 0; n < 6; n++)
        {
            SetNetlist(player, Circuit(n));
            Press(player, "button.clip-play");
        }
        Assert.Equal(6, host.Renders.Count);

        SetNetlist(player, Circuit(1));
        Press(player, "button.clip-play");
        Assert.Equal(6, host.Renders.Count);

        SetNetlist(player, Circuit(0));
        Press(player, "button.clip-play");
        Assert.Equal(7, host.Renders.Count);
    }

    [Fact]
    public void TheCacheHoldsAtMostFiftyMegabytes()
    {
        var cache = new RenderCache();
        var floats = 4 * 1024 * 1024;
        for (var n = 0; n < 4; n++)
        {
            cache.Add(new RenderKey(Circuit(n), "s", 1, "cab"), new RenderedClip(new float[floats], 44100, "x"));
        }

        Assert.Equal(3, cache.Count);
        Assert.True(cache.Bytes <= RenderCache.MaxBytes);
        Assert.False(cache.TryGet(new RenderKey(Circuit(0), "s", 1, "cab"), out _));
    }

    [Fact]
    public void DownloadUsesTheCachedBuffer()
    {
        var player = Player(Circuit(1));
        Press(player, "button.clip-play");
        Press(player, "button.clip-play");

        Press(player, "button.clip-download");

        Assert.Single(host.Renders);
        Assert.Single(JSInterop.Invocations, i => i.Identifier == "download");
    }

    [Fact]
    public void ASecondPlayerWithTheSameKeyAndTheSameCacheDoesNotRender()
    {
        var cache = new RenderCache();
        var first = Player(Circuit(1), cache);
        Press(first, "button.clip-play");

        var second = Player(Circuit(1), cache);
        Press(second, "button.clip-play");

        Assert.Single(host.Renders);
    }

    [Fact]
    public void ANewPlayerOnTheSameCacheShowsTheLatestResultAndItsDownloadWorks()
    {
        var cache = new RenderCache();
        Press(Player(Circuit(1), cache), "button.clip-play");

        var after = Player(Circuit(1), cache);

        Assert.False(after.Find("button.clip-download").HasAttribute("disabled"));
        Assert.Empty(after.FindAll("p.clip-stale"));
    }
}
