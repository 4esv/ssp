using System.Globalization;
using SpiceSharp;
using SpiceSharp.Components;
using SpiceSharp.Simulations;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using PartsApi = Ssp.Core.Parts.Parts;

namespace Ssp.Core.Tests;

public class VactrolPartTests
{
    const double StepAt = 1e-3;

    static string Models(params string[] files) =>
        string.Join('\n', files.Select(f => File.ReadAllText(Path.Combine(RepoPaths.Root, "models", f))));

    // Steps the LED drive from 0 to about 10 mA at StepAt. Returns the LDR conductance over time.
    static List<(double T, double G)> Step(double tau)
    {
        var netlist = string.Join('\n',
            "vactrol test",
            Models("led.lib", "vactrol.cir"),
            "Vdrv d 0 PULSE(0 5 " + StepAt.ToString("G", CultureInfo.InvariantCulture) + " 1u 1u 1 2)",
            "Rs d la 305",
            "X1 la 0 m 0 vactrol tau=" + tau.ToString("G", CultureInfo.InvariantCulture),
            "Vm m 0 DC 1",
            ".END");

        var loaded = NetlistLoader.Load(netlist);
        Assert.Empty(loaded.Diagnostics);

        var tran = new Transient("tran", tau / 200, StepAt + 6 * tau);
        var im = new RealCurrentExport(tran, "Vm");
        var samples = new List<(double, double)>();
        foreach (var _ in tran.Run(loaded.Circuit, Transient.ExportTransient))
        {
            samples.Add((tran.Time, -im.Value));
        }
        return samples;
    }

    static double At(List<(double T, double G)> s, double t) =>
        s.OrderBy(p => Math.Abs(p.T - t)).First().G;

    [Theory]
    [InlineData(10e-3)]
    [InlineData(50e-3)]
    public void ConductanceFollowsTheLedStepWithTheStatedTimeConstant(double tau)
    {
        var s = Step(tau);
        var g0 = At(s, StepAt / 2);
        var g1 = At(s, StepAt + 6 * tau);
        Assert.True(g1 > 100 * g0, $"g0={g0} g1={g1}: the drive did not move the LDR");

        foreach (var k in new[] { 1.0, 2.0, 3.0 })
        {
            var expected = g0 + (g1 - g0) * (1 - Math.Exp(-k));
            // g1 is read at 6 tau, so it sits 0.25% below the final value. The bound allows that.
            var measured = At(s, StepAt + k * tau);
            Assert.InRange(measured, expected - 0.02 * (g1 - g0), expected + 0.02 * (g1 - g0));
        }
    }

    [Fact]
    public void TheTimeConstantIsASubcircuitParameterWithARow()
    {
        Assert.Contains("params:", Models("vactrol.cir"));
        Assert.Matches(@"params:.*\btau=", Models("vactrol.cir"));
        var row = PartsApi.Load(Path.Combine(RepoPaths.Root, "models", "parts.toml")).Get("VACTROL")!;
        Assert.Equal("vactrol", row.Kind);
        Assert.Equal("vactrol", row.Model);
        Assert.False(string.IsNullOrWhiteSpace(row.Provenance));
    }
}
