using System.Globalization;
using System.Reflection;
using Ssp.Core.Audio;
using Ssp.Web.Projects;

namespace Ssp.Web.Components;

/// <summary>A bundled sample: its id, the name in the list and its resource.</summary>
public sealed record BundledSample(string Id, string Name, string Resource, double Seconds)
{
    public string Label => $"{Name} ({Seconds.ToString("0.#", CultureInfo.InvariantCulture)} s)";
}

/// <summary>A file the user loaded, as mono at the project rate. <see cref="SourceRate"/> and <see cref="SourceSeconds"/> describe the file.</summary>
public sealed record UploadedSample(string Name, double[] Samples, int SourceRate, double SourceSeconds)
{
    public string Id => $"upload:{Name}|{Samples.Length}|{SourceRate}";
}

/// <summary>
/// The sound that goes through the circuit. One value for the whole editor: every panel that plays a sample reads it,
/// and every picker sets it. The bundled id is kept in browser storage and in the project (#173). An upload is not kept.
/// </summary>
public sealed class SampleChoice(IKeyValueStore? storage = null)
{
    /// <summary>The rate of the circuit render. A file at another rate is resampled to it.</summary>
    public const int ProjectRate = 44_100;
    public const double MaxSeconds = 10;
    public const string StorageKey = "ssp.sample";
    public const string DefaultId = "clip";

    static readonly Assembly Resources = typeof(SampleChoice).Assembly;

    public static readonly IReadOnlyList<BundledSample> Bundled =
    [
        new("clip", "Bundled clip", "audio/clip.wav", 1),
        new("pluck", "Plucked note", "audio/pluck.wav", 1),
        new("chord", "Chord", "audio/chord.wav", 1),
        new("riff", "Palm-muted riff", "audio/riff.wav", 2),
        new("arpeggio", "Clean arpeggio", "audio/arpeggio.wav", 2),
        new("bass", "Bass note", "audio/bass.wav", 1),
        new("sine", "Sine 440 Hz", "audio/sine.wav", 1),
        new("sweep", "Sweep 20 Hz-20 kHz", "audio/sweep.wav", 2),
        new("noise", "White noise", "audio/noise.wav", 1),
        new("click", "Click", "audio/click.wav", 1),
    ];

    BundledSample bundled = Bundled[0];

    /// <summary>The upload on show, or null when a bundled sample is chosen.</summary>
    public UploadedSample? Upload { get; private set; }

    /// <summary>A file over <see cref="MaxSeconds"/> that waits for the user to take its first part.</summary>
    public UploadedSample? TooLong { get; private set; }

    /// <summary>The message of a file that did not read, or null.</summary>
    public string? Error { get; private set; }

    /// <summary>Raised after the choice changes.</summary>
    public event Action? Changed;

    /// <summary>The id for the render key: it differs for each bundled sample and each upload.</summary>
    public string Id => Upload?.Id ?? bundled.Id;

    /// <summary>The id of the bundled sample that is chosen, or the last one before an upload. The project keeps this one.</summary>
    public string BundledId => bundled.Id;

    public string Name => Upload?.Name ?? bundled.Name;

    /// <summary>The samples at <see cref="ProjectRate"/>, mono.</summary>
    public (double[] Samples, int Rate) Samples() =>
        Upload is { } u ? (u.Samples, ProjectRate) : Load(bundled);

    public string Description => Upload is { } u
        ? $"{u.Name}, {u.SourceRate} Hz, {u.SourceSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s, mono at {ProjectRate} Hz"
        : bundled.Label;

    /// <summary>Chooses a bundled sample by id. An unknown id is ignored.</summary>
    public async Task Select(string id)
    {
        if (Bundled.FirstOrDefault(b => b.Id == id) is not { } next)
        {
            return;
        }

        bundled = next;
        Upload = null;
        TooLong = null;
        Error = null;
        await Save();
        Changed?.Invoke();
    }

    /// <summary>Takes the id that a project keeps. It is not written to browser storage. An unknown id or none leaves the choice.</summary>
    public void Adopt(string? id)
    {
        if (Bundled.FirstOrDefault(b => b.Id == id) is { } next && (Upload is not null || next != bundled))
        {
            bundled = next;
            Upload = null;
            TooLong = null;
            Changed?.Invoke();
        }
    }

    /// <summary>Reads the id kept in browser storage. Storage that is off or empty leaves the default.</summary>
    public async Task Restore()
    {
        try
        {
            if (storage is not null && await storage.Get(StorageKey) is { } id && Bundled.FirstOrDefault(b => b.Id == id) is { } saved)
            {
                bundled = saved;
                Upload = null;
                Changed?.Invoke();
            }
        }
        catch (Exception)
        {
            // NOTE: Storage can be blocked. The default sample is correct.
        }
    }

    /// <summary>Reads a WAV file. A file over 10 s waits in <see cref="TooLong"/> until <see cref="UseFirstSeconds"/>.</summary>
    public void Load(Stream stream, string name)
    {
        Error = null;
        TooLong = null;
        try
        {
            var wav = Wav.Read(stream);
            var mono = Mono(wav);
            var seconds = (double)mono.Length / wav.SampleRate;
            var sample = new UploadedSample(name, Resample(mono, wav.SampleRate), wav.SampleRate, seconds);
            if (seconds > MaxSeconds)
            {
                TooLong = sample;
            }
            else
            {
                Upload = sample;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException or EndOfStreamException)
        {
            Error = "Upload failed: " + ex.Message;
        }
        Changed?.Invoke();
    }

    public void Fail(string message)
    {
        Error = message;
        Changed?.Invoke();
    }

    /// <summary>Takes the first 10 s of the file that was too long.</summary>
    public void UseFirstSeconds()
    {
        if (TooLong is { } long_)
        {
            Upload = long_ with { Samples = long_.Samples[..(int)(MaxSeconds * ProjectRate)] };
            TooLong = null;
            Changed?.Invoke();
        }
    }

    async Task Save()
    {
        try
        {
            if (storage is not null)
            {
                await storage.Set(StorageKey, bundled.Id);
            }
        }
        catch (Exception)
        {
            // NOTE: Storage can be blocked or full. The choice still holds until a reload.
        }
    }

    static (double[] Samples, int Rate) Load(BundledSample sample)
    {
        using var stream = Resources.GetManifestResourceStream(sample.Resource) ?? throw new InvalidOperationException($"Missing resource {sample.Resource}.");
        var wav = Wav.Read(stream);
        return (Resample(Mono(wav), wav.SampleRate), ProjectRate);
    }

    // NOTE: The worker renders one channel. A file with more is the mean of its channels.
    static double[] Mono(WavData wav) =>
        wav.Channels.Length == 1
            ? wav.Channels[0]
            : [.. Enumerable.Range(0, wav.Channels[0].Length).Select(i => wav.Channels.Average(c => c[i]))];

    // NOTE: Linear interpolation. The sample is a test input, not a master.
    static double[] Resample(double[] samples, int rate)
    {
        if (rate == ProjectRate || samples.Length == 0)
        {
            return samples;
        }

        var count = (int)((long)samples.Length * ProjectRate / rate);
        var step = (double)rate / ProjectRate;
        var output = new double[count];
        for (var i = 0; i < count; i++)
        {
            var at = i * step;
            var j = (int)at;
            var next = Math.Min(j + 1, samples.Length - 1);
            output[i] = samples[j] + (samples[next] - samples[j]) * (at - j);
        }
        return output;
    }
}
