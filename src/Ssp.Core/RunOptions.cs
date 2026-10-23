using Ssp.Core.Analysis;
using Ssp.Core.Netlist;

namespace Ssp.Core;

/// <summary>Settings for <see cref="Runner.Run"/>.</summary>
public sealed record RunOptions
{
    /// <summary>The sweep when the caller gives none: 10 Hz to 100 kHz, 10 points in each decade.</summary>
    public static DecadeSweep DefaultSweep { get; } = new(10, 100_000, 10);

    public RunOptions(DecadeSweep? sweep = null, IReadOnlyList<Override>? overrides = null)
    {
        Sweep = sweep ?? DefaultSweep;
        Overrides = overrides ?? [];
    }

    /// <summary>The sweep for the frequency response and the impedance.</summary>
    public DecadeSweep Sweep { get; init; }

    /// <summary>Part values to set before the analyses run.</summary>
    public IReadOnlyList<Override> Overrides { get; init; }

    /// <summary>The directory of the netlist file. A relative <c>.include</c> path resolves against it. Null means the working directory.</summary>
    public string? BaseDirectory { get; init; }
}
