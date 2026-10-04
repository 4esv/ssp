using Bunit;
using Ssp.Core;
using Ssp.Web.Hosting;

namespace Ssp.Web.Tests;

public class WorkerSimulationHostTests : BunitContext
{
    static string Fixture(string name) => File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", name));

    [Fact]
    public async Task RenderPostsTheNetlistAndReadsTheJson()
    {
        var module = JSInterop.SetupModule(WorkerSimulationHost.ClientModule);
        module.Setup<string>("render", _ => true).SetResult("[0.5,-0.25,null]");
        var host = new WorkerSimulationHost(JSInterop.JSRuntime);

        var samples = await host.Render("* netlist", [0.1, 0.2, 0.3], 44_100, 2);

        Assert.Equal([0.5, -0.25, double.NaN], samples);
        var call = Assert.Single(module.Invocations["render"]);
        Assert.Equal("* netlist", call.Arguments[0]);
        Assert.Equal(new[] { 0.1, 0.2, 0.3 }, call.Arguments[1]);
        Assert.Equal(44_100, call.Arguments[2]);
        Assert.Equal(2, call.Arguments[3]);
    }

    [Fact]
    public async Task VersionsReadsTheJson()
    {
        var module = JSInterop.SetupModule(WorkerSimulationHost.ClientModule);
        module.Setup<string>("versions", _ => true).SetResult("""["ssp 0.1.0","SpiceSharp 3.2.3"]""");
        var host = new WorkerSimulationHost(JSInterop.JSRuntime);

        Assert.Equal(["ssp 0.1.0", "SpiceSharp 3.2.3"], await host.Versions());
    }

    [Fact]
    public async Task RunGivesOperatingPoint()
    {
        var host = new WorkerSimulationHost(JSInterop.JSRuntime);

        var result = await host.Run(Fixture("rc-lowpass.cir"), new RunOptions());

        Assert.NotNull(result.OperatingPoint);
        Assert.Equal(1.0, result.OperatingPoint.NodeVoltages["in"], 6);
    }

    [Fact]
    public void WorkerRenderWritesOneNumberForEachSample()
    {
        var input = new double[441];
        for (var i = 0; i < input.Length; i++)
        {
            input[i] = 0.3 * Math.Sin(2.0 * Math.PI * 440 * i / 44_100);
        }

        var json = WorkerExports.RenderJson(Fixture("clipper-bjt-si.cir"), input, 44_100, 1);

        var samples = WorkerSimulationHost.ReadSamples(json);
        Assert.Equal(input.Length, samples.Length);
        Assert.All(samples, s => Assert.True(double.IsFinite(s)));
    }

    // The fixed steps of the drawn fuzz chain stop after 6 ms, and the render starts again with variable steps (#219).
    // The worker keeps that verdict for each netlist text, so the next render of the same netlist starts on variable steps.
    [Fact]
    public void WorkerRenderRemembersANetlistThatNeedsVariableSteps()
    {
        static string Block(string name) => File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "blocks", name));
        var fuzz = Ssp.Core.Chains.Chain.Compose(
        [
            new Ssp.Core.Chains.ChainStage("pedal", Fixture("fuzz-drawn.cir")),
            new Ssp.Core.Chains.ChainStage("gain", Block("gain-variable-tl072.cir")),
            new Ssp.Core.Chains.ChainStage("tone", Block("tone-baxandall-passive.cir")),
            new Ssp.Core.Chains.ChainStage("power", Block("power-9v.cir")),
        ]);
        using var stream = File.OpenRead(Path.Combine(RepoPaths.Root, "src", "Ssp.Web", "Audio", "clip.wav"));
        var input = Ssp.Core.Audio.Wav.Read(stream).Channels[0][..2205];
        var clipper = Fixture("clipper-bjt-si.cir");

        Assert.False(WorkerExports.StartsOnVariableSteps(fuzz));
        var first = WorkerExports.RenderJson(fuzz, input, 44_100, 1);
        WorkerExports.RenderJson(clipper, input, 44_100, 1);

        Assert.True(WorkerExports.StartsOnVariableSteps(fuzz));
        Assert.False(WorkerExports.StartsOnVariableSteps(clipper));
        Assert.Equal(first, WorkerExports.RenderJson(fuzz, input, 44_100, 1));
    }
}
