using Ssp.Core.Calculators;

namespace Ssp.Core.Tests;

public class CalculatorsTests
{
    [Fact]
    public void RcFilterCutoff()
    {
        // fc = 1 / (2 pi R C) = 1 / (2 pi * 10e3 * 10e-9) = 1 / 6.2832e-4 = 1591.55 Hz
        Assert.Equal(1591.549, Calculators.Calculators.RcFilter(10e3, 10e-9), 0.001);
    }

    [Fact]
    public void DividerOutput()
    {
        // Vout = Vin * R2 / (R1 + R2) = 9 * 20k / (10k + 20k) = 6 V
        Assert.Equal(6.0, Calculators.Calculators.Divider(9.0, 10e3, 20e3), 1e-9);
    }

    [Fact]
    public void BjtBiasPoint()
    {
        // Vb = 9 * 10k / (20k + 10k) = 3 V
        // Ve = 3 - 0.7 = 2.3 V; Ie = 2.3 / 2.3k = 1 mA
        // Vc = 9 - 1 mA * 4.7k = 4.3 V
        var bias = Calculators.Calculators.BjtBias(9.0, 20e3, 10e3, 4.7e3, 2.3e3, 0.7);
        Assert.Equal(3.0, bias.Base, 1e-9);
        Assert.Equal(2.3, bias.Emitter, 1e-9);
        Assert.Equal(1e-3, bias.Current, 1e-12);
        Assert.Equal(4.3, bias.Collector, 1e-9);
    }

    [Fact]
    public void LedResistorValue()
    {
        // R = (Vs - Vf) / I = (9 - 2) / 10 mA = 700 ohm
        Assert.Equal(700.0, Calculators.Calculators.LedResistor(9.0, 2.0, 10e-3), 1e-9);
    }

    [Fact]
    public void NonInvertingGain()
    {
        // G = 1 + Rf / Rg = 1 + 100k / 10k = 11
        Assert.Equal(11.0, Calculators.Calculators.Gain(100e3, 10e3), 1e-9);
    }

    [Fact]
    public void SallenKeyLowPass()
    {
        // R1 = R2 = 10k, C1 = C2 = 10 nF
        // fc = 1 / (2 pi sqrt(R1 R2 C1 C2)) = 1 / (2 pi * 1e-4) = 1591.55 Hz
        // Q = sqrt(R1 R2 C1 C2) / (C2 (R1 + R2)) = 1e-4 / (10e-9 * 20k) = 0.5
        var filter = Calculators.Calculators.SallenKey(10e3, 10e3, 10e-9, 10e-9);
        Assert.Equal(1591.549, filter.CutoffHz, 0.001);
        Assert.Equal(0.5, filter.Q, 1e-9);
    }

    [Fact]
    public void ToneStackCorners()
    {
        // Low-pass: 1 / (2 pi * 39k * 10 nF) = 1 / 2.4504e-3 = 408.09 Hz
        // High-pass: 1 / (2 pi * 22k * 3.9 nF) = 1 / 5.3910e-4 = 1854.95 Hz
        var tone = Calculators.Calculators.ToneStack(39e3, 10e-9, 22e3, 3.9e-9);
        Assert.Equal(408.090, tone.LowPassHz, 0.001);
        Assert.Equal(1854.953, tone.HighPassHz, 0.001);
    }
}
