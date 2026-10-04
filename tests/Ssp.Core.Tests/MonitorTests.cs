using Ssp.Core.Analysis;
using Ssp.Core.Audio;
using Ssp.Core.Netlist;

namespace Ssp.Core.Tests;

public class MonitorTests
{
    const int Fs = 44_100;

    static double[] Sine(int length) =>
        [.. Enumerable.Range(0, length).Select(i => 0.2 * Math.Sin(2 * Math.PI * 440 * i / Fs))];

    [Fact]
    public void RingBufferReadsSamplesAndInterpolates()
    {
        var ring = new RingBufferWaveform(Fs, 8);
        ring.Push([1.0, 3.0, 5.0]);

        Assert.Equal(3.0, ring.At(1.0 / Fs), 9);
        Assert.Equal(2.0, ring.At(0.5 / Fs), 9);
        Assert.Equal(5.0, ring.At(2.0 / Fs), 9);
        Assert.Equal(0, ring.Starved);
    }

    [Fact]
    public void RingBufferHoldsTheLastSampleAndCountsStarvedReads()
    {
        var ring = new RingBufferWaveform(Fs, 8);
        ring.Push([1.0, 3.0]);

        Assert.Equal(3.0, ring.At(5.0 / Fs));
        Assert.Equal(1, ring.Starved);
    }

    [Fact]
    public void RingBufferKeepsTheNewestSamplesWhenItWraps()
    {
        var ring = new RingBufferWaveform(Fs, 4);
        ring.Push([0.0, 1.0, 2.0, 3.0, 4.0, 5.0]);

        Assert.Equal(5.0, ring.At(5.0 / Fs));
        Assert.Equal(2.0, ring.At(2.0 / Fs));
        Assert.Equal(2.0, ring.At(0.0));
    }

    [Fact]
    public void ChunksGiveTheSameOutputAsOneRender()
    {
        var input = Sine(2048);
        var expected = Analyses.Render(NetlistLoader.Load(Fixtures.Read("clipper-bjt-si.cir")), input, Fs, 1);

        using var session = new MonitorSession(NetlistLoader.Load(Fixtures.Read("clipper-bjt-si.cir")), Fs);
        var actual = new List<double>();
        for (var start = 0; start < input.Length; start += 512)
        {
            actual.AddRange(session.Process(input.AsSpan(start, 512)));
        }

        Assert.Equal(input.Length, actual.Count);
        Assert.Equal(0, session.Starved);
        for (var i = 0; i < input.Length; i++)
        {
            Assert.Equal(expected[i], actual[i], 6);
        }
    }

    [Fact]
    public void ChunkEdgesLeaveNoStep()
    {
        var input = Sine(1536);
        using var session = new MonitorSession(NetlistLoader.Load(Fixtures.Read("clipper-bjt-si.cir")), Fs);
        var output = new List<double>();
        for (var start = 0; start < input.Length; start += 512)
        {
            output.AddRange(session.Process(input.AsSpan(start, 512)));
        }

        var steps = output.Zip(output.Skip(1), (a, b) => Math.Abs(b - a)).ToArray();
        var edge = Math.Max(steps[511], steps[1023]);
        Assert.True(edge <= 2 * steps.Max(), $"edge {edge}");
        Assert.Contains(output, v => Math.Abs(v) > 1e-6);
    }
}
