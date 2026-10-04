using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Core;
using Ssp.Web.Components;
using Ssp.Web.Hosting;

namespace Ssp.Web.Tests;

public class LiveMonitorTests : BunitContext
{
    sealed class FakeHost(int failures) : ISimulationHost, IMonitorHost
    {
        public int Calls { get; private set; }

        public int Starts { get; private set; }

        public Task MonitorStart(string netlist, int sampleRate)
        {
            Starts++;
            return Task.CompletedTask;
        }

        public Task<double[]> MonitorProcess(double[] chunk)
        {
            Calls++;
            if (Calls <= failures)
            {
                throw new InvalidOperationException("The timestep 0.00000e+000s is too small at t=8.12472e-002s.");
            }

            return Task.FromResult(chunk.Select(x => 2 * x).ToArray());
        }

        public Task MonitorStop() => Task.CompletedTask;

        public Task<RunResult> Run(string netlist, RunOptions options) => throw new NotSupportedException();

        public Task<double[]> Render(string netlist, double[] input, int sampleRate, int oversample) => throw new NotSupportedException();

        public Task<SweepResult> Sweep(string netlist, string reference, IReadOnlyList<double> values, RunOptions options) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> Versions() => throw new NotSupportedException();
    }

    IRenderedComponent<LiveMonitor> StartMonitor(FakeHost host)
    {
        Services.AddSingleton<ISimulationHost>(host);
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./js/live-monitor.js").Setup<int>("open").SetResult(44_100);
        var monitor = Render<LiveMonitor>(p => p.Add(c => c.Netlist, "V1 in 0 0\n.END\n"));
        monitor.Find("button.monitor-start").Click();
        return monitor;
    }

    [Fact]
    public async Task AFailedChunkIsSilenceAndCountsAsDroppedAndTheMonitorRuns()
    {
        var host = new FakeHost(failures: 3);
        var monitor = StartMonitor(host);

        var outputs = new List<double[]>();
        for (var i = 0; i < 5; i++)
        {
            outputs.Add(await monitor.InvokeAsync(() => monitor.Instance.Process([0.5, 0.5])));
        }

        Assert.All(outputs.Take(3), o => Assert.Equal([0.0, 0.0], o));
        Assert.All(outputs.Skip(3), o => Assert.Equal([1.0, 1.0], o));
        Assert.Equal("Dropped chunks: 3", monitor.Find("p.monitor-dropped").TextContent);
        var status = monitor.Find("p.monitor-status").TextContent;
        Assert.StartsWith("Running at 44100 Hz", status);
        Assert.Contains("Dropped 3 chunks. First error: The timestep 0.00000e+000s is too small at t=8.12472e-002s.", status);
        Assert.True(monitor.Find("button.monitor-start").HasAttribute("disabled"));

        // NOTE: A failed chunk restarts the session from the operating point.
        Assert.Equal(4, host.Starts);
    }

    [Fact]
    public async Task TwentyFailedChunksInARowStopTheMonitorAndShowTheError()
    {
        var host = new FakeHost(failures: int.MaxValue);
        var monitor = StartMonitor(host);

        for (var i = 0; i < 20; i++)
        {
            await monitor.InvokeAsync(() => monitor.Instance.Process([0.5]));
        }

        Assert.Equal(
            "The monitor stopped: The timestep 0.00000e+000s is too small at t=8.12472e-002s. Press Start to try again.",
            monitor.Find("p.monitor-status").TextContent);
        Assert.False(monitor.Find("button.monitor-start").HasAttribute("disabled"));
        Assert.True(monitor.Find("button.monitor-stop").HasAttribute("disabled"));
    }
}
