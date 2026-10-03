using Ssp.Core.Audio;

namespace Ssp.Core.Tests;

public class WavTests
{
    const int Rate = 48000;

    static WavData Sine(int bitDepth, int channels)
    {
        var scale = Math.Pow(2, bitDepth - 1);
        var data = new double[channels][];
        for (var c = 0; c < channels; c++)
        {
            data[c] = new double[Rate / 100];
            for (var i = 0; i < data[c].Length; i++)
            {
                var amp = c == 0 ? 0.8 : 0.4;
                data[c][i] = Math.Round(amp * Math.Sin(2 * Math.PI * 1000 * i / Rate) * scale) / scale;
            }
        }
        return new WavData(Rate, data);
    }

    [Theory]
    [InlineData(16, 1)]
    [InlineData(16, 2)]
    [InlineData(24, 1)]
    [InlineData(24, 2)]
    public void RoundTripIsBitExact(int bitDepth, int channels)
    {
        var input = Sine(bitDepth, channels);
        using var stream = new MemoryStream();
        Wav.Write(stream, input, bitDepth);
        stream.Position = 0;
        var output = Wav.Read(stream);

        Assert.Equal(Rate, output.SampleRate);
        Assert.Equal(channels, output.Channels.Length);
        for (var c = 0; c < channels; c++)
            Assert.Equal(input.Channels[c], output.Channels[c]);
    }

    static byte[] Header(short tag, short channels, short bits, byte[]? extra = null)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var blockAlign = (short)(channels * bits / 8);
        w.Write("RIFF"u8); w.Write(36 + 4); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write(tag); w.Write(channels);
        w.Write(Rate); w.Write(Rate * blockAlign); w.Write(blockAlign); w.Write(bits);
        w.Write("data"u8); w.Write(4); w.Write(new byte[4]);
        return ms.ToArray();
    }

    [Fact]
    public void Rejects8BitPcm()
    {
        var ex = Assert.Throws<NotSupportedException>(() => Wav.Read(new MemoryStream(Header(1, 1, 8))));
        Assert.Contains("8-bit PCM", ex.Message);
    }

    [Fact]
    public void RejectsFloat()
    {
        var ex = Assert.Throws<NotSupportedException>(() => Wav.Read(new MemoryStream(Header(3, 1, 32))));
        Assert.Contains("IEEE float", ex.Message);
    }

    [Fact]
    public void WriteRejectsUnsupportedBitDepth()
    {
        var ex = Assert.Throws<NotSupportedException>(() => Wav.Write(new MemoryStream(), Sine(16, 1), 8));
        Assert.Contains("8-bit", ex.Message);
    }
}
