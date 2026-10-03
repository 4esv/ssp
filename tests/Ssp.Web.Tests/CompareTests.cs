using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Core;
using Ssp.Web.Components;
using Ssp.Web.Hosting;

namespace Ssp.Web.Tests;

public class CompareTests : BunitContext
{
    static string Fixture(string name) => File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", name));

    readonly SpyHost host = new();

    IRenderedComponent<Compare> Compare(string part, string values)
    {
        Services.AddSingleton<ISimulationHost>(host);
        var page = Render<Compare>(p => p.Add(c => c.Netlist, Fixture("rc-lowpass.cir")));
        page.Find("select.part").Change(part);
        page.Find("input.values").Input(values);
        page.Find("button.compare").Click();
        return page;
    }

    [Fact]
    public void ThreeValuesGiveThreePaths()
    {
        var page = Compare("R1", "1k, 4k7, 10k");

        Assert.Equal(3, page.FindAll(".compare .magnitude path.series").Count);
    }

    [Fact]
    public void LegendNamesEachValue()
    {
        var page = Compare("R1", "1k, 4k7, 10k");

        var legend = page.FindAll(".compare .magnitude .legend text").Select(t => t.TextContent).ToList();
        Assert.Equal(["R1 = 1k", "R1 = 4k7", "R1 = 10k"], legend);
    }

    [Fact]
    public void InvalidValueShowsDiagnostic()
    {
        var page = Compare("R1", "1k, banana");

        var item = Assert.Single(page.FindAll(".compare ul.diagnostics li"));
        Assert.Contains("error", item.ClassList);
        Assert.Contains("banana", item.TextContent);
        Assert.Empty(page.FindAll(".compare svg.line-chart"));
        Assert.Equal(0, host.Sweeps);
    }

    [Fact]
    public void UsesHostSweep()
    {
        Compare("C1", "47n, 100n");

        Assert.Equal(1, host.Sweeps);
        Assert.Equal(0, host.Runs);
    }

    [Fact]
    public void PartListHasEachPassivePart()
    {
        Services.AddSingleton<ISimulationHost>(host);
        var page = Render<Compare>(p => p.Add(c => c.Netlist, Fixture("rc-lowpass.cir")));

        Assert.Equal(["C1", "R1"], page.FindAll("select.part option").Select(o => o.TextContent));
    }

    sealed class SpyHost : ISimulationHost
    {
        readonly InProcessSimulationHost inner = new();

        public int Runs { get; private set; }
        public int Sweeps { get; private set; }

        public Task<RunResult> Run(string netlist, RunOptions options)
        {
            Runs++;
            return inner.Run(netlist, options);
        }

        public Task<double[]> Render(string netlist, double[] input, int sampleRate, int oversample) =>
            inner.Render(netlist, input, sampleRate, oversample);

        public Task<SweepResult> Sweep(string netlist, string reference, IReadOnlyList<double> values, RunOptions options)
        {
            Sweeps++;
            return inner.Sweep(netlist, reference, values, options);
        }

        public Task<IReadOnlyList<string>> Versions() => inner.Versions();
    }
}
