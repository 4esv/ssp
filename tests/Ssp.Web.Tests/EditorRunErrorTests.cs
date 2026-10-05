using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Core;
using Ssp.Web.Hosting;
using Ssp.Web.Pages;

namespace Ssp.Web.Tests;

public class EditorRunErrorTests : BunitContext
{
    readonly ManualTimeProvider time = new();
    readonly ThrowingHost host = new();

    public EditorRunErrorTests()
    {
        Services.AddSingleton<TimeProvider>(time);
        Services.AddSingleton<ISimulationHost>(host);
    }

    static string Fixture(string name) => File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", name));

    sealed class ThrowingHost : ISimulationHost
    {
        readonly InProcessSimulationHost inner = new();
        public bool Throw;

        public Task<RunResult> Run(string netlist, RunOptions options) =>
            Throw ? throw new InvalidOperationException("The host broke.") : inner.Run(netlist, options);

        public Task<double[]> Render(string netlist, double[] input, int sampleRate, int oversample) => inner.Render(netlist, input, sampleRate, oversample);
        public Task<SweepResult> Sweep(string netlist, string reference, IReadOnlyList<double> values, RunOptions options) => inner.Sweep(netlist, reference, values, options);
        public Task<IReadOnlyList<string>> Versions() => inner.Versions();
    }

    IRenderedComponent<Editor> GoodRun()
    {
        var page = Render<Editor>();
        page.Find("textarea").Input(Fixture("divider-basic.cir"));
        time.Advance(TimeSpan.FromMilliseconds(100));
        page.Find("button.run").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".voltages tbody tr")));
        return page;
    }

    [Fact]
    public void RunThatThrowsShowsTheFailureAndKeepsTheLastResultStale()
    {
        var page = GoodRun();
        var good = page.Find(".voltages").TextContent;
        host.Throw = true;

        page.Find("button.run").Click();

        page.WaitForAssertion(() => Assert.Contains("The run failed: The host broke.", page.Find(".run-failed").TextContent));
        Assert.Equal(good, page.Find(".voltages").TextContent);
        Assert.Single(page.FindAll(".live-state.stale"));
        Assert.Empty(page.FindAll("button.run[disabled]"));
    }

    [Fact]
    public void LiveRunThatThrowsShowsTheFailureAndKeepsTheLastResultStale()
    {
        var page = GoodRun();
        var good = page.Find(".voltages").TextContent;
        host.Throw = true;

        page.Find("textarea").Input("* edit\n" + Fixture("divider-basic.cir"));
        time.Advance(TimeSpan.FromMilliseconds(250));

        page.WaitForAssertion(() => Assert.Contains("The run failed: The host broke.", page.Find(".run-failed").TextContent));
        Assert.Equal(good, page.Find(".voltages").TextContent);
        Assert.Single(page.FindAll(".live-state.stale"));
    }
}
