using SpiceSharp.Components;
using Ssp.Core.Analysis;
using Ssp.Core.Netlist;

namespace Ssp.Core.Tests;

public class RenderTests
{
    private const int Fs = 44_100;
    private const double Amplitude = 0.3;
    private const double Frequency = 440.0;

    // Measured with Render on clipper-bjt-si.cir, 0.3 V 440 Hz, last 100 ms of 200 ms:
    // 0.638671 V at oversample 1 and 0.638657 V at oversample 4.
    private const double MeasuredPeak = 0.6387;

    // Oversample 1 and 4 differ by 0.002 %. Doubling the diode IS moves the clamp by Vt ln 2 = 18 mV (2.8 %),
    // so 1 % keeps the step size out of the result and still catches a model change.
    private const double Tolerance = 0.01;

    // Oversample 1 and 4 differ by at most 39 mV, on the fast clipping edges. Output that is off in time by
    // a factor of the oversample differs by 1.26 V, so 0.1 V separates the two.
    private const double MaxSampleDifference = 0.1;

    // The coupling capacitors charge for about 50 ms, so the peak is read from the second half.
    private const int Samples = Fs / 5;

    private static double[] Sine()
    {
        var input = new double[Samples];
        for (var i = 0; i < input.Length; i++)
        {
            input[i] = Amplitude * Math.Sin(2.0 * Math.PI * Frequency * i / Fs);
        }

        return input;
    }

    private static double[] Render(int oversample) =>
        Analyses.Render(NetlistLoader.Load(Fixtures.Read("clipper-bjt-si.cir")), Sine(), Fs, oversample);

    private static double[] Settled(double[] output) => output[(output.Length / 2)..];

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void OutputLengthEqualsInputLength(int oversample)
    {
        Assert.Equal(Samples, Render(oversample).Length);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void OutputPeakMatchesMeasuredValue(int oversample)
    {
        var peak = Settled(Render(oversample)).Max(Math.Abs);

        Assert.InRange(peak, MeasuredPeak * (1 - Tolerance), MeasuredPeak * (1 + Tolerance));
    }

    [Fact]
    public void OversampledOutputLinesUpWithTheInputSamples()
    {
        var plain = Render(1);
        var oversampled = Render(4);

        var worst = plain.Zip(oversampled, (a, b) => Math.Abs(a - b)).Max();

        Assert.True(worst < MaxSampleDifference, $"The largest difference is {worst} V.");
    }

    // Cross-check: a Si junction conducts at 0.5 V to 0.7 V from microamps to milliamps, so the pair clamps near +-0.5 V.
    // Here the stage pushes about 2 mA into the diodes, so the clamp sits at the top of that range.
    [Fact]
    public void SiliconDiodePairClampsNearHalfAVoltOnBothPolarities()
    {
        var settled = Settled(Render(1));

        Assert.InRange(settled.Max(), 0.4, 0.7);
        Assert.InRange(-settled.Min(), 0.4, 0.7);
    }

    [Fact]
    public void RenderLeavesTheInputSourceUnchanged()
    {
        var circuit = NetlistLoader.Load(Fixtures.Read("clipper-bjt-si.cir"));
        var source = (VoltageSource)circuit.Circuit["V1"];
        var count = circuit.Circuit.Count;

        Analyses.Render(circuit, Sine(), Fs, 1);

        Assert.Null(source.Parameters.Waveform);
        Assert.Equal(count, circuit.Circuit.Count);
    }
}
