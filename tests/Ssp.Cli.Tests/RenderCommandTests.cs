using Ssp.Core.Audio;

namespace Ssp.Cli.Tests;

public sealed class RenderCommandTests : IDisposable
{
    private const int Fs = 44_100;
    private const double Amplitude = 0.3;
    private const double Frequency = 440.0;

    // The same values as RenderTests in Ssp.Core.Tests: clipper-bjt-si.cir, 0.3 V 440 Hz, last 100 ms of 200 ms.
    private const double MeasuredPeak = 0.6387;
    private const double Tolerance = 0.01;
    private const int Samples = Fs / 5;

    private readonly string _dir = Directory.CreateTempSubdirectory("ssp-render-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string In => Path.Combine(_dir, "a.wav");

    private string Out => Path.Combine(_dir, "b.wav");

    private static string BundledIr => Path.Combine(RepoPaths.Root, "models", "ir", "cab-1x12.wav");

    private static (int ExitCode, string Output, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = Program.Run(args, output, error);
        return (exitCode, output.ToString(), error.ToString());
    }

    private void WriteSine()
    {
        var input = new double[Samples];
        for (var i = 0; i < input.Length; i++)
        {
            input[i] = Amplitude * Math.Sin(2.0 * Math.PI * Frequency * i / Fs);
        }

        using var stream = File.Create(In);
        Wav.Write(stream, new WavData(Fs, [input]), 24);
    }

    private WavData ReadOut()
    {
        using var stream = File.OpenRead(Out);
        return Wav.Read(stream);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("4")]
    public void OutputLengthEqualsInputLength(string oversample)
    {
        WriteSine();

        var (exitCode, _, error) = Run(
            "render", RepoPaths.Fixture("clipper-bjt-si.cir"), "--in", In, "--out", Out, "--oversample", oversample);

        Assert.True(exitCode == 0, error);
        var result = ReadOut();
        Assert.Equal(Fs, result.SampleRate);
        Assert.Single(result.Channels);
        Assert.Equal(Samples, result.Channels[0].Length);
    }

    [Fact]
    public void OutputPeakMatchesMeasuredValue()
    {
        WriteSine();

        var (exitCode, _, error) = Run("render", RepoPaths.Fixture("clipper-bjt-si.cir"), "--in", In, "--out", Out);

        Assert.True(exitCode == 0, error);
        var output = ReadOut().Channels[0];
        var peak = output[(output.Length / 2)..].Max(Math.Abs);
        Assert.InRange(peak, MeasuredPeak * (1 - Tolerance), MeasuredPeak * (1 + Tolerance));
    }

    [Fact]
    public void MissingInputExitsOneWithAMessage()
    {
        var (exitCode, _, error) = Run("render", RepoPaths.Fixture("clipper-bjt-si.cir"), "--in", In, "--out", Out);

        Assert.Equal(1, exitCode);
        Assert.Contains("a.wav", error);
        Assert.Contains("does not exist", error);
        Assert.False(File.Exists(Out));
    }

    [Fact]
    public void ZeroOversampleExitsOne()
    {
        WriteSine();

        var (exitCode, _, error) = Run(
            "render", RepoPaths.Fixture("clipper-bjt-si.cir"), "--in", In, "--out", Out, "--oversample", "0");

        Assert.Equal(1, exitCode);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void IrConvolvesTheOutputAndKeepsTheLength()
    {
        WriteSine();
        var (plainExit, _, plainError) = Run("render", RepoPaths.Fixture("clipper-bjt-si.cir"), "--in", In, "--out", Out);
        Assert.True(plainExit == 0, plainError);
        var plain = ReadOut().Channels[0];
        WavData ir;
        using (var stream = File.OpenRead(BundledIr))
        {
            ir = Wav.Read(stream);
        }

        var (exitCode, _, error) = Run(
            "render", RepoPaths.Fixture("clipper-bjt-si.cir"), "--in", In, "--out", Out, "--ir", BundledIr);

        Assert.True(exitCode == 0, error);
        var result = ReadOut();
        Assert.Equal(Fs, result.SampleRate);
        Assert.Single(result.Channels);
        Assert.Equal(Samples, result.Channels[0].Length);
        var expected = Convolution.Convolve(plain, ir.Channels[0]);
        for (var i = 0; i < Samples; i++)
        {
            Assert.Equal(expected[i], result.Channels[0][i], 1e-5);
        }
    }

    [Fact]
    public void MissingIrExitsOneWithAMessage()
    {
        WriteSine();
        var ir = Path.Combine(_dir, "cab.wav");

        var (exitCode, _, error) = Run(
            "render", RepoPaths.Fixture("clipper-bjt-si.cir"), "--in", In, "--out", Out, "--ir", ir);

        Assert.Equal(1, exitCode);
        Assert.Contains("cab.wav", error);
        Assert.Contains("does not exist", error);
        Assert.False(File.Exists(Out));
    }

    [Fact]
    public void IrSampleRateMismatchExitsOne()
    {
        WriteSine();
        var ir = Path.Combine(_dir, "cab.wav");
        using (var stream = File.Create(ir))
        {
            Wav.Write(stream, new WavData(48_000, [[0.5, 0.25]]), 24);
        }

        var (exitCode, _, error) = Run(
            "render", RepoPaths.Fixture("clipper-bjt-si.cir"), "--in", In, "--out", Out, "--ir", ir);

        Assert.Equal(1, exitCode);
        Assert.Contains("48000", error);
        Assert.False(File.Exists(Out));
    }
}
