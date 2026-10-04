using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Core;
using Ssp.Web.Components;
using Ssp.Web.Hosting;

namespace Ssp.Web.Tests;

public class ClipPlayerRenderTests : BunitContext
{
    sealed class FakeHost : ISimulationHost
    {
        public List<int> Oversamples { get; } = [];

        /// <summary>The first oversample at which a render works. A smaller oversample throws a timestep failure.</summary>
        public int WorksFrom { get; set; } = 1;

        public Func<double, double> Shape { get; set; } = x => x * 0.5;

        public Task<RunResult> Run(string netlist, RunOptions options) => throw new NotSupportedException();

        public Task<double[]> Render(string netlist, double[] input, int sampleRate, int oversample)
        {
            Oversamples.Add(oversample);
            if (oversample < WorksFrom)
            {
                throw new InvalidOperationException("The timestep 0.00000e+000s is too small at t=1.81020e-001s");
            }

            return Task.FromResult(input.Select(Shape).ToArray());
        }

        public Task<SweepResult> Sweep(string netlist, string reference, IReadOnlyList<double> values, RunOptions options) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> Versions() => throw new NotSupportedException();
    }

    readonly FakeHost host = new();

    public ClipPlayerRenderTests()
    {
        Services.AddSingleton<ISimulationHost>(host);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    string StatusAfterTry()
    {
        var player = Render<ClipPlayer>();
        player.InvokeAsync(() => player.Instance.Try("V1 in 0 1\n.END\n")).GetAwaiter().GetResult();
        return player.Find("p.clip-status").TextContent;
    }

    [Fact]
    public void TimestepFailureAtOneRetriesAtTwo()
    {
        host.WorksFrom = 2;

        var status = StatusAfterTry();

        Assert.Equal([1, 2], host.Oversamples);
        Assert.Contains("Rendered at 2x oversample.", status);
    }

    [Fact]
    public void FailureAtEightShowsTheMessage()
    {
        host.WorksFrom = 16;

        var status = StatusAfterTry();

        Assert.Equal([1, 2, 4, 8], host.Oversamples);
        Assert.Contains("Render failed", status);
        Assert.Contains("timestep", status);
    }

    [Fact]
    public void ZerosShowTheOutputIsSilent()
    {
        host.Shape = _ => 0;

        var status = StatusAfterTry();

        Assert.Contains("The output is silent.", status);
        Assert.DoesNotContain("Playing", status);
        Assert.DoesNotContain(JSInterop.Invocations, i => i.Identifier == "play");
    }

    [Fact]
    public void NormalRenderShowsPeakInDbfs()
    {
        var status = StatusAfterTry();

        Assert.Matches(@"Output peak -?\d+(\.\d)? dBFS, RMS -?\d+ dBFS\.", status);
        Assert.Equal([1], host.Oversamples);
    }
}
