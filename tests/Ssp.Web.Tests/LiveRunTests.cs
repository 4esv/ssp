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
}
