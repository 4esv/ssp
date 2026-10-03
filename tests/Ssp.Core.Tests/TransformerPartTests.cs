using SpiceSharp;
using SpiceSharp.Components;
using SpiceSharp.Simulations;
using Ssp.Core.Netlist;

namespace Ssp.Core.Tests;

public class TransformerPartTests
{
    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(10.0)]
    public void VoltageRatioAt1kHzMatchesTurnsRatio(double n)
    {
        var model = File.ReadAllText(Path.Combine(RepoPaths.Root, "models", "transformer.cir"));
        var netlist = string.Join('\n',
            "transformer test",
            model,
            "V1 in 0 AC 1",
            "Rs in p1 1",
            "X1 p1 0 s1 0 transformer n=" + n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Rload s1 0 100k",
            ".END");

        var loaded = NetlistLoader.Load(netlist);
        Assert.Empty(loaded.Diagnostics);

        var ac = new AC("ac", new[] { 1e3 });
        var vp = new ComplexVoltageExport(ac, "p1");
        var vs = new ComplexVoltageExport(ac, "s1");
        var ratio = double.NaN;
        foreach (var _ in ac.Run(loaded.Circuit))
        {
            ratio = vs.Value.Magnitude / vp.Value.Magnitude;
        }

        Assert.InRange(ratio, n * 0.99, n * 1.01);
    }
}
