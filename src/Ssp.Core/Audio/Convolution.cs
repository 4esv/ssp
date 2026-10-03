namespace Ssp.Core.Audio;

/// <summary>Convolves audio with an impulse response, for example a guitar cabinet. No file access.</summary>
public static class Convolution
{
    /// <summary>
    /// Returns the full linear convolution of <paramref name="samples"/> with <paramref name="ir"/>.
    /// The output has <c>samples.Length + ir.Length - 1</c> samples, or none when either input is empty.
    /// </summary>
    public static double[] Convolve(double[] samples, double[] ir)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(ir);
        if (samples.Length == 0 || ir.Length == 0) return [];

        var output = new double[samples.Length + ir.Length - 1];
        for (var i = 0; i < samples.Length; i++)
        {
            var x = samples[i];
            if (x == 0) continue;
            for (var k = 0; k < ir.Length; k++) output[i + k] += x * ir[k];
        }
        return output;
    }
}
