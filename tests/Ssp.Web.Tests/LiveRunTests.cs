using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Core;
using Ssp.Web.Hosting;
using Ssp.Web.Pages;

namespace Ssp.Web.Tests;

public class LiveRunTests : BunitContext
{
    readonly ManualTimeProvider time = new();
    readonly GatedHost host = new();

    public LiveRunTests()
    {
        Services.AddSingleton<TimeProvider>(time);
        Services.AddSingleton<ISimulationHost>(host);
    }

    static string Fixture(string name) => File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", name));

    sealed class GatedHost : ISimulationHost
    {
        readonly InProcessSimulationHost inner = new();
        public int Runs;
        public TaskCompletionSource? Gate;

        public async Task<RunResult> Run(string netlist, RunOptions options)
        {
            Interlocked.Increment(ref Runs);
            var gate = Gate;
            if (gate is not null) await gate.Task;
            return await inner.Run(netlist, options);
        }

        public Task<double[]> Render(string netlist, double[] input, int sampleRate, int oversample) => inner.Render(netlist, input, sampleRate, oversample);
        public Task<SweepResult> Sweep(string netlist, string reference, IReadOnlyList<double> values, RunOptions options) => inner.Sweep(netlist, reference, values, options);
        public Task<IReadOnlyList<string>> Versions() => inner.Versions();
    }

    // NOTE: Each live run waits on its own gate. Run is the main-thread path, so a live run must not use it.
    sealed class SlowHost : ISimulationHost
    {
        readonly InProcessSimulationHost inner = new();
        public readonly List<(string Netlist, CancellationToken Token, TaskCompletionSource Gate)> Live = [];
        public int Finished;

        public Task<RunResult> Run(string netlist, RunOptions options) => throw new InvalidOperationException("The live run used the main-thread Run.");

        async Task<RunResult> ISimulationHost.Live(string netlist, CancellationToken token)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (Live) Live.Add((netlist, token, gate));
            await gate.Task;
            try
            {
                return await inner.Live(netlist, CancellationToken.None);
            }
            finally
            {
                Interlocked.Increment(ref Finished);
            }
        }

        public Task<double[]> Render(string netlist, double[] input, int sampleRate, int oversample) => inner.Render(netlist, input, sampleRate, oversample);
        public Task<SweepResult> Sweep(string netlist, string reference, IReadOnlyList<double> values, RunOptions options) => inner.Sweep(netlist, reference, values, options);
        public Task<IReadOnlyList<string>> Versions() => inner.Versions();
    }

    IRenderedComponent<Editor> Page(string netlist)
    {
        var page = Render<Editor>();
        page.Find("textarea").Input(netlist);
        return page;
    }

    static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);

    [Fact]
    public void OneRunForABurstOfEdits()
    {
        var page = Page(Fixture("divider-basic.cir"));
        page.Find("textarea").Input("* a\n" + Fixture("divider-basic.cir"));
        time.Advance(TimeSpan.FromMilliseconds(200));
        page.Find("textarea").Input("* ab\n" + Fixture("divider-basic.cir"));
        time.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Equal(0, host.Runs);

        time.Advance(Debounce);

        page.WaitForAssertion(() => Assert.Equal(1, host.Runs));
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".voltages tbody tr")));
        Assert.Equal(["1 V", "667 mV"], page.FindAll(".schematic-pins .net-voltage").Select(l => l.TextContent));
    }

    [Fact]
    public void StaleWhileSolving()
    {
        var page = Page(Fixture("divider-basic.cir"));
        Assert.Single(page.FindAll(".live-state.stale"));
        host.Gate = new TaskCompletionSource();

        time.Advance(Debounce);
        page.WaitForAssertion(() => Assert.Equal(1, host.Runs));
        Assert.Single(page.FindAll(".live-state.stale"));

        host.Gate.SetResult();
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".live-state.stale")));
        Assert.NotEmpty(page.FindAll(".voltages tbody tr"));
    }

    [Fact]
    public void FailedSolveKeepsLastGoodResultStale()
    {
        var page = Page(Fixture("divider-basic.cir"));
        time.Advance(Debounce);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".voltages tbody tr")));
        var good = page.Find(".voltages").TextContent;

        page.Find("textarea").Input("R1 in\n");
        time.Advance(Debounce);

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".live-state.stale")));
        Assert.Equal(good, page.Find(".voltages").TextContent);
        Assert.Single(page.FindAll(".live-failed"));
    }

    [Fact]
    public void SlowLiveRunDoesNotBlockTheRender()
    {
        var slow = new SlowHost();
        Services.AddSingleton<ISimulationHost>(slow);
        var page = Page(Fixture("divider-basic.cir"));
        time.Advance(Debounce);
        page.WaitForAssertion(() => Assert.Single(slow.Live));

        page.Find("textarea").Input("* still typing\n" + Fixture("divider-basic.cir"));

        Assert.StartsWith("* still typing", page.Find("textarea").GetAttribute("value"));
        Assert.Single(page.FindAll(".live-state.stale"));
        Assert.Empty(page.FindAll(".voltages tbody tr"));
    }

    [Fact]
    public void NewerEditCancelsTheOlderRun()
    {
        var slow = new SlowHost();
        Services.AddSingleton<ISimulationHost>(slow);
        var page = Page(Fixture("divider-basic.cir"));
        time.Advance(Debounce);
        page.WaitForAssertion(() => Assert.Single(slow.Live));

        page.Find("textarea").Input(Fixture("divider-basic.cir").Replace("R2 out 0 2k", "R2 out 0 1k"));
        time.Advance(Debounce);
        page.WaitForAssertion(() => Assert.Equal(2, slow.Live.Count));
        Assert.True(slow.Live[0].Token.IsCancellationRequested);
        Assert.False(slow.Live[1].Token.IsCancellationRequested);

        slow.Live[1].Gate.SetResult();
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".live-state.stale")));
        slow.Live[0].Gate.SetResult();
        page.WaitForAssertion(() => Assert.Equal(2, slow.Finished));
        // NOTE: The older result goes to the renderer after the host returns it. This wait gives it the time to show.
        Thread.Sleep(100);

        page.WaitForAssertion(() => Assert.Equal(["1 V", "500 mV"], page.FindAll(".schematic-pins .net-voltage").Select(l => l.TextContent)));
        Assert.Empty(page.FindAll(".live-state.stale"));
    }
}
