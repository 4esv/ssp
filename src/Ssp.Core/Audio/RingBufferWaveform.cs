using SpiceSharp;
using SpiceSharp.Components;
using SpiceSharp.Entities;
using SpiceSharp.ParameterSets;
using SpiceSharp.Simulations;

namespace Ssp.Core.Audio;

/// <summary>
/// A voltage source waveform that reads input samples from a ring buffer. A producer pushes the samples in chunks.
/// The engine reads the value at its simulation time. Sample <c>i</c> is at time <c>i / sampleRate</c>.
/// </summary>
/// <remarks>
/// NOTE: The value between two samples is linear, as in a piecewise-linear source. When the engine asks for a sample
/// that the producer has not pushed yet, the value is the last pushed sample and <see cref="Starved"/> counts the read.
/// When the engine asks for a sample that the ring has overwritten, the value is the oldest sample in the ring.
/// <see cref="Clone"/> returns the same object, so that the buffer is shared with the simulation.
/// </remarks>
public sealed class RingBufferWaveform : ParameterSet, IWaveform, IWaveformDescription
{
    readonly double[] ring;
    readonly double sampleRate;
    long pushed;
    IIntegrationMethod? method;
    double value;

    public RingBufferWaveform(int sampleRate, int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sampleRate, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
        this.sampleRate = sampleRate;
        ring = new double[capacity];
    }

    /// <summary>The number of samples that were pushed.</summary>
    public long Pushed => pushed;

    /// <summary>The number of reads of a sample that was not pushed yet.</summary>
    public long Starved { get; private set; }

    /// <summary>The value at the time of the last probe.</summary>
    public double Value => value;

    /// <summary>Appends the samples. A sample older than the capacity is lost.</summary>
    public void Push(ReadOnlySpan<double> samples)
    {
        foreach (var sample in samples)
        {
            ring[pushed % ring.Length] = sample;
            pushed++;
        }
    }

    /// <summary>The value at the time in seconds.</summary>
    public double At(double time)
    {
        if (pushed == 0)
        {
            return 0;
        }

        // NOTE: The tolerance keeps a time that is a whole sample, within rounding, at that sample.
        var position = Math.Max(0, time * sampleRate);
        var index = (long)Math.Floor(position + 1e-9);
        var fraction = position - index;
        if (fraction < 1e-9)
        {
            fraction = 0;
        }

        var first = Read(index);
        return fraction == 0 ? first : first + (fraction * (Read(index + 1) - first));
    }

    double Read(long index)
    {
        if (index >= pushed)
        {
            Starved++;
            index = pushed - 1;
        }

        return ring[Math.Max(index, pushed - ring.Length) % ring.Length];
    }

    public void Probe() => value = At(method?.Time ?? 0);

    public void Accept()
    {
    }

    public IWaveform Create(IBindingContext context)
    {
        method = null;
        context?.TryGetState(out method);
        return this;
    }

    public IWaveformDescription Clone() => this;
}
