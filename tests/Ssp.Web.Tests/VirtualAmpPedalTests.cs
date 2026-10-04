using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Core;
using Ssp.Core.Netlist;
using Ssp.Web.Components;
using Ssp.Web.Hosting;

namespace Ssp.Web.Tests;

public class VirtualAmpPedalTests : BunitContext
{
    sealed class FakeHost : ISimulationHost
    {
        public List<string> Renders { get; } = [];

        public Task<RunResult> Run(string netlist, RunOptions options) => throw new NotSupportedException();

        public Task<double[]> Render(string netlist, double[] input, int sampleRate, int oversample)
        {
            Renders.Add(netlist);
            return Task.FromResult(new double[input.Length]);
        }

        public Task<SweepResult> Sweep(string netlist, string reference, IReadOnlyList<double> values, RunOptions options) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> Versions() => throw new NotSupportedException();
    }

    readonly FakeHost host = new();

    const string NoJacks = "V1 in 0 DC 0 AC 1\nR1 in out 1k\nR2 out 0 1k\n.END\n";

    public VirtualAmpPedalTests()
    {
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<ISimulationHost>(host);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    static string Pedal() => File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "blocks", "gain-variable-tl072.cir"));

    IRenderedComponent<VirtualAmp> Panel(string pedal) => Render<VirtualAmp>(p => p.Add(c => c.Pedal, pedal));

    [Fact]
    public void PedalWithJacksIsInTheChainFirst()
    {
        var panel = Panel(Pedal());

        var chain = panel.Find("textarea.amp-netlist").TextContent;
        Assert.Contains("Xpedal", chain);
        Assert.True(panel.Find("input.use-pedal").HasAttribute("checked"));
        Assert.StartsWith("* ssp:title Chain: pedal, gain, tone, power", chain);
    }

    [Fact]
    public void PedalWithNoJacksIsOffAndSaysWhy()
    {
        var panel = Panel(NoJacks);

        Assert.True(panel.Find("input.use-pedal").HasAttribute("disabled"));
        Assert.False(panel.Find("input.use-pedal").HasAttribute("checked"));
        Assert.Contains("Add a Jack in and a Jack out to the drawing to use it here.", panel.Markup);
        Assert.DoesNotContain("Xpedal", panel.Find("textarea.amp-netlist").TextContent);
    }

    [Fact]
    public void PedalKnobShowsWithAPrefix()
    {
        var panel = Panel(Pedal());

        Assert.Contains("pedal.RV1", panel.FindAll(".knobs .part").Select(e => e.TextContent));
    }

    [Fact]
    public void UncheckingTheBoxTakesThePedalOut()
    {
        var panel = Panel(Pedal());

        panel.Find("input.use-pedal").Change(false);

        Assert.DoesNotContain("Xpedal", panel.Find("textarea.amp-netlist").TextContent);
    }

    [Fact]
    public void EditingTheDrawingRebuildsTheChain()
    {
        var panel = Panel(NoJacks);

        panel.Render(p => p.Add(c => c.Pedal, Pedal()));

        Assert.Contains("Xpedal", panel.Find("textarea.amp-netlist").TextContent);
        Assert.True(panel.Find("input.use-pedal").HasAttribute("checked"));
    }

    [Fact]
    public void ChainThatCannotBeBuiltShowsTheMessage()
    {
        var panel = Panel(Pedal().Replace(".END", ".subckt BROKEN a b\n.END"));

        Assert.Contains("The chain cannot be built.", panel.Find(".amp-error").TextContent);
    }

    [Fact]
    public async Task TryPlaysThePedalThenTheAmp()
    {
        var panel = Panel(Pedal());

        panel.Find("button.amp-try").Click();

        await panel.WaitForAssertionAsync(() => Assert.Single(host.Renders));
        Assert.Contains("Xpedal", host.Renders[0]);
    }

    [Fact]
    public void ChainWithThePedalRunsWithNoError()
    {
        var panel = Panel(Pedal());

        var result = Runner.Run(panel.Find("textarea.amp-netlist").TextContent, new RunOptions());

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Severity.Error);
    }
}
