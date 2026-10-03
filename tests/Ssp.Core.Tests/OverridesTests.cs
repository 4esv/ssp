using SpiceSharp.Components;
using Ssp.Core.Netlist;

namespace Ssp.Core.Tests;

public class OverridesTests
{
    [Theory]
    [InlineData("R1=1t", 1e12)]
    [InlineData("R1=2g", 2e9)]
    [InlineData("R1=3meg", 3e6)]
    [InlineData("R1=3MEG", 3e6)]
    [InlineData("R1=4k", 4e3)]
    [InlineData("R1=5m", 5e-3)]
    [InlineData("R1=6u", 6e-6)]
    [InlineData("R1=7n", 7e-9)]
    [InlineData("R1=8p", 8e-12)]
    [InlineData("R1=9f", 9e-15)]
    [InlineData("R1=10", 10)]
    [InlineData("R1=1.5k", 1500)]
    [InlineData("R1=4k7", 4700)]
    [InlineData("R1=2meg2", 2.2e6)]
    [InlineData("R1 = 1e3", 1000)]
    public void ParsesValue(string text, double expected)
    {
        var parsed = Overrides.Parse(text);

        Assert.Equal("R1", parsed.Reference);
        Assert.Equal(expected, parsed.Value, expected * 1e-12);
    }

    [Theory]
    [InlineData("")]
    [InlineData("R1")]
    [InlineData("=1k")]
    [InlineData("R1=")]
    [InlineData("R1=abc")]
    [InlineData("R1=4x7")]
    public void BadTextThrowsFormatException(string text)
    {
        Assert.Throws<FormatException>(() => Overrides.Parse(text));
    }

    [Fact]
    public void AppliesResistorValue()
    {
        var loaded = NetlistLoader.Load(Fixtures.Read("rc-lowpass.cir"));

        var diagnostics = Overrides.Apply(loaded, new[] { Overrides.Parse("R1=4k7") });

        Assert.Empty(diagnostics);
        var r1 = Assert.IsType<Resistor>(loaded.Circuit["R1"]);
        Assert.Equal(4700, r1.Parameters.Resistance, 1e-9);
    }

    [Fact]
    public void AppliesCapacitorValue()
    {
        var loaded = NetlistLoader.Load(Fixtures.Read("rc-lowpass.cir"));

        var diagnostics = Overrides.Apply(loaded, new[] { Overrides.Parse("C1=47n") });

        Assert.Empty(diagnostics);
        var c1 = Assert.IsType<Capacitor>(loaded.Circuit["C1"]);
        Assert.Equal(47e-9, c1.Parameters.Capacitance, 1e-18);
    }

    [Fact]
    public void UnknownReferenceGivesOneDiagnosticNamingItAndDoesNotThrow()
    {
        var loaded = NetlistLoader.Load(Fixtures.Read("rc-lowpass.cir"));

        var diagnostics = Overrides.Apply(loaded, new[] { Overrides.Parse("R9=1k") });

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("R9", diagnostic.Message);
    }

    [Fact]
    public void ApplyDoesNotChangeTheNetlistText()
    {
        var text = Fixtures.Read("rc-lowpass.cir");
        var copy = string.Copy(text);
        var loaded = NetlistLoader.Load(text);

        Overrides.Apply(loaded, new[] { Overrides.Parse("R1=4k7") });

        Assert.Equal(copy, text);
        Assert.Equal(copy, Fixtures.Read("rc-lowpass.cir"));
    }
}
