using Ssp.Core.Audio;

namespace Ssp.Core.Tests;

public class ConvolutionTests
{
    static readonly double[] Ir = [0.5, -0.25, 0.125, 0.0625, -0.03125];

    [Fact]
    public void UnitImpulseGivesTheIr()
    {
        var output = Convolution.Convolve([1.0], Ir);

        Assert.Equal(Ir, output);
    }

    [Fact]
    public void OutputLengthIsSamplesPlusIrMinusOne()
    {
        var output = Convolution.Convolve(new double[10], Ir);

        Assert.Equal(10 + Ir.Length - 1, output.Length);
    }

    [Fact]
    public void DelayedScaledImpulseGivesDelayedScaledIr()
    {
        var output = Convolution.Convolve([0.0, 0.0, 2.0, 0.0], Ir);

        var expected = new double[4 + Ir.Length - 1];
        for (var i = 0; i < Ir.Length; i++) expected[2 + i] = 2.0 * Ir[i];
        Assert.Equal(expected, output);
    }

    [Fact]
    public void EmptyInputGivesEmptyOutput()
    {
        Assert.Empty(Convolution.Convolve([], Ir));
        Assert.Empty(Convolution.Convolve([1.0], []));
    }
}
