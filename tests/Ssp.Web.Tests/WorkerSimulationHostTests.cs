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
}
