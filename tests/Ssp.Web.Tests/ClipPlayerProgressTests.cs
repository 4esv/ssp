using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Core;
using Ssp.Web.Components;
using Ssp.Web.Hosting;

namespace Ssp.Web.Tests;

public class ClipPlayerProgressTests : BunitContext
{
    sealed class FakeClock : TimeProvider
    {
        public long Ticks { get; set; }

        public override long GetTimestamp() => Ticks;

        public override long TimestampFrequency => 1000;
    }

    sealed class FakeHost : ISimulationHost
    {
        readonly TaskCompletionSource<double[]> done = new();

        public Action<double>? Report { get; private set; }

        public int Cancels { get; private set; }

        public Task<RunResult> Run(string netlist, RunOptions options) => throw new NotSupportedException();

        public Task<double[]> Render(string netlist, double[] input, int sampleRate, int oversample) => done.Task;

        public Task<double[]> Render(string netlist, double[] input, int sampleRate, int oversample, Action<double>? onProgress)
        {
            Report = onProgress;
            return done.Task;
        }

        public void CancelRender()
        {
            Cancels++;
            done.TrySetException(new OperationCanceledException());
        }

        public Task<SweepResult> Sweep(string netlist, string reference, IReadOnlyList<double> values, RunOptions options) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> Versions() => throw new NotSupportedException();
    }

    readonly FakeHost host = new();
    readonly FakeClock clock = new();

    public ClipPlayerProgressTests()
    {
        Services.AddSingleton<ISimulationHost>(host);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    IRenderedComponent<ClipPlayer> StartTry(out Task running)
    {
        var player = Render<ClipPlayer>(p => p.Add(c => c.Clock, clock).Add(c => c.Netlist, "V1 in 0 1\n.END\n"));
        running = player.InvokeAsync(() => player.Instance.Try("V1 in 0 1\n.END\n"));
        return player;
    }

    [Fact]
    public void ProgressUpdatesTheStatus()
    {
        var player = StartTry(out _);

        player.InvokeAsync(() => host.Report!(0.8));

        Assert.Matches(@"Rendering 0\.8 of \d+\.\d s at 1x oversample\.", player.Find("p.clip-status").TextContent);
        Assert.NotNull(player.Find("button.clip-cancel"));
    }

    [Fact]
    public async Task CancelCallsTheHostAndShowsCancelled()
    {
        var player = StartTry(out var running);

        player.Find("button.clip-cancel").Click();
        await running;

        Assert.Equal(1, host.Cancels);
        Assert.Equal("Cancelled.", player.Find("p.clip-status").TextContent);
        Assert.Empty(player.FindAll("button.clip-cancel"));
    }

    [Fact]
    public void SlowRenderAsksAfterSixtySeconds()
    {
        var player = StartTry(out _);

        clock.Ticks = 59_000;
        player.InvokeAsync(() => host.Report!(1.0));
        Assert.DoesNotContain("This is slow.", player.Find("p.clip-status").TextContent);

        clock.Ticks = 63_000;
        player.InvokeAsync(() => host.Report!(1.1));
        var status = player.Find("p.clip-status").TextContent;
        Assert.Contains("This is slow. Cancel, or lower the clip length.", status);
        Assert.Contains("63 s", status);
    }
}
