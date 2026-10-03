using SpiceSharp;
using SpiceSharp.Components;
using SpiceSharp.Simulations;

namespace Ssp.Core.Tests;

public class EngineSmokeTests
{
    [Fact]
    public void OperatingPointOfVoltageDividerIsTwoThirdsVolt()
    {
        var circuit = new Circuit(
            new VoltageSource("V1", "in", "0", 1.0),
            new Resistor("R1", "in", "out", 1e3),
            new Resistor("R2", "out", "0", 2e3));
        var op = new OP("op");
        var vout = new RealVoltageExport(op, "out");

        var result = double.NaN;
        foreach (var _ in op.Run(circuit))
        {
            result = vout.Value;
        }

        Assert.Equal(2.0 / 3.0, result, 1e-9);
    }
}
