namespace Ssp.Web.Components;

/// <summary>What a render depends on. Two renders with equal keys give the same samples.</summary>
public sealed record RenderKey(string Netlist, string SampleId, int Oversample, string CabinetId);

/// <summary>A finished render: the samples after the cabinet, as the player plays and saves them.</summary>
public sealed record RenderedClip(float[] Samples, int Rate, string Summary);

/// <summary>
/// The last few renders, most recently used last. The editor page owns one, so a tab switch or a hidden panel keeps it.
/// </summary>
public sealed class RenderCache
{
    public const int MaxEntries = 5;
    public const long MaxBytes = 50L * 1024 * 1024;

    readonly List<(RenderKey Key, RenderedClip Clip)> entries = [];

    public int Count => entries.Count;

    public long Bytes => entries.Sum(e => (long)e.Clip.Samples.Length * sizeof(float));

    /// <summary>The render that was added or played last, kept after the cache drops it.</summary>
    public (RenderKey Key, RenderedClip Clip)? Latest { get; private set; }

    public bool TryGet(RenderKey key, out RenderedClip clip)
    {
        var at = entries.FindIndex(e => e.Key == key);
        if (at < 0)
        {
            clip = null!;
            return false;
        }

        var entry = entries[at];
        entries.RemoveAt(at);
        entries.Add(entry);
        Latest = entry;
        clip = entry.Clip;
        return true;
    }

    public void Add(RenderKey key, RenderedClip clip)
    {
        entries.RemoveAll(e => e.Key == key);
        entries.Add((key, clip));
        Latest = (key, clip);
        while (entries.Count > MaxEntries || Bytes > MaxBytes)
        {
            entries.RemoveAt(0);
        }
    }
}
