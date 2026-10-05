using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Core;
using Ssp.Web.Components;
using Ssp.Web.Hosting;

namespace Ssp.Web.Tests;

public class CompareTests : BunitContext
{
    static string Fixture(string name) => File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", name));

    static string Fuzz() => File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "library", "fuzz-transistor-diode.cir"));

    readonly SpyHost host = new();
    readonly ManualTimeProvider time = new();

    IRenderedComponent<Compare> Compare(string part, string values, string? netlist = null)
    {
        Services.AddSingleton<ISimulationHost>(host);
        Services.AddSingleton<TimeProvider>(time);
        var page = Render<Compare>(p => p.Add(c => c.Netlist, netlist ?? Fixture("rc-lowpass.cir")));
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
    public void ThreeEmitterValuesGiveThreeCurvesAndThreeRows()
    {
        var page = Compare("R4", "470, 1k, 2k2", Fuzz());

        var paths = page.FindAll(".compare .magnitude path.series").Select(p => p.GetAttribute("d")).ToList();
        Assert.Equal(3, paths.Distinct().Count());
        var rows = page.FindAll(".compare table.series tbody tr").Select(r => r.QuerySelector("th")!.TextContent).ToList();
        Assert.Equal(["R4 = 470", "R4 = 1k", "R4 = 2k2"], rows);
    }

    [Fact]
    public void GainIsFromTheInNodeToTheOutNode()
    {
        // NOTE: The fuzz source is AC 1 at the in node, so the out node in dB is the gain. A source of AC 2 adds 6 dB to
        // both nodes and must not change the gain.
        var one = Compare("R4", "1k", Fuzz());
        var gain = one.Find(".compare table.series tbody tr td.gain").TextContent;
        var two = Render<Compare>(p => p.Add(c => c.Netlist, Fuzz().Replace("AC 1", "AC 2")));
        two.Find("select.part").Change("R4");
        two.Find("input.values").Input("1k");
        two.Find("button.compare").Click();

        Assert.Equal(gain, two.Find(".compare table.series tbody tr td.gain").TextContent);
    }

    [Fact]
    public void FrozenSeriesStaysWhenTheCircuitChanges()
    {
        var page = Compare("R4", "470, 1k, 2k2", Fuzz());
        var before = Cells(page);
        page.Find("button.freeze").Click();

        page.Render(p => p.Add(c => c.Netlist, Fuzz().Replace("R2 n3 n10 2.2k", "R2 n3 n10 4.7k")));
        Assert.Equal(1, host.Sweeps);
        time.Advance(TimeSpan.FromMilliseconds(250));

        page.WaitForAssertion(() => Assert.Equal(2, host.Sweeps));
        var rows = page.FindAll(".compare table.series tbody tr").Select(r => r.QuerySelector("th")!.TextContent).ToList();
        Assert.Equal(["R4 = 470", "R4 = 1k", "R4 = 2k2", "R4 = 470 (frozen)", "R4 = 1k (frozen)", "R4 = 2k2 (frozen)"], rows);
        // NOTE: The axis fits the new curves too, so the frozen numbers prove the frozen curve, not its path.
        Assert.Equal(before, Cells(page, "tr.frozen"));
        Assert.Equal(3, page.FindAll(".compare .magnitude path.series.frozen").Count);
        Assert.Empty(Cells(page, "tr:not(.frozen)").Intersect(before));
    }

    static List<string> Cells(IRenderedComponent<Compare> page, string rows = "tr") =>
        page.FindAll($".compare table.series tbody {rows}").Select(r => string.Join(" | ", r.QuerySelectorAll("td").Select(d => d.TextContent))).ToList();

    [Fact]
    public void NoRunBeforeTheDebounce()
    {
        var page = Compare("R4", "1k", Fuzz());

        page.Render(p => p.Add(c => c.Netlist, Fuzz().Replace("R2 n3 n10 2.2k", "R2 n3 n10 4.7k")));
        time.Advance(TimeSpan.FromMilliseconds(249));

        Assert.Equal(1, host.Sweeps);
    }

    [Fact]
    public void ClearRemovesTheFrozenSeries()
    {
        var page = Compare("R4", "1k", Fuzz());
        page.Find("button.freeze").Click();
        page.Find("button.unfreeze").Click();

        Assert.Single(page.FindAll(".compare table.series tbody tr"));
        Assert.Empty(page.FindAll(".compare path.series.frozen"));
    }

    [Fact]
    public void PartListHasEachPassivePart()
    {
        Services.AddSingleton<ISimulationHost>(host);
        Services.AddSingleton<TimeProvider>(time);
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
