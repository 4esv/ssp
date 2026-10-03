using SpiceSharp.Components;
using Ssp.Core.Netlist;

namespace Ssp.Core.Parts;

/// <summary>
/// A potentiometer built from two resistors. <c>&lt;part&gt;_1</c> runs from the top to the wiper.
/// <c>&lt;part&gt;_2</c> runs from the wiper to the bottom. The knob keeps their sum and sets the split.
/// </summary>
public static class Pot
{
    // A log taper gives 10% of the total at the half-way position.
    private static readonly double LogExponent = Math.Log(0.1) / Math.Log(0.5);

    // SPICE rejects a resistor of zero ohms.
    private const double MinFraction = 1e-9;

    /// <summary>Sets the resistors of each <c>knob</c> directive. A bad knob gives a diagnostic and is skipped.</summary>
    public static IReadOnlyList<Diagnostic> Apply(LoadedCircuit circuit)
    {
        var diagnostics = new List<Diagnostic>();
        foreach (var knob in circuit.Directives.Knobs)
        {
            if (!(knob.Position >= 0 && knob.Position <= 1))
            {
                diagnostics.Add(Error($"Knob {knob.Part}: position {knob.Position} is outside 0 to 1 (0% to 100%)."));
                continue;
            }

            var ratio = Ratio(knob.Taper, knob.Position);
            if (ratio is null)
            {
                diagnostics.Add(Error($"Knob {knob.Part}: unknown taper \"{knob.Taper}\". Use linear, log or revlog."));
                continue;
            }

            if (Find(circuit, $"{knob.Part}_1") is not Resistor upper || Find(circuit, $"{knob.Part}_2") is not Resistor lower)
            {
                diagnostics.Add(Error($"Knob {knob.Part}: the netlist needs resistors {knob.Part}_1 and {knob.Part}_2."));
                continue;
            }

            var total = upper.Parameters.Resistance + lower.Parameters.Resistance;
            var fraction = Math.Clamp(ratio.Value, MinFraction, 1 - MinFraction);
            lower.Parameters.Resistance = total * fraction;
            upper.Parameters.Resistance = total * (1 - fraction);
        }

        return diagnostics;
    }

    /// <summary>The share of the total resistance below the wiper, or null for an unknown taper.</summary>
    public static double? Ratio(string taper, double position) => taper.ToLowerInvariant() switch
    {
        "linear" => position,
        "log" => Math.Pow(position, LogExponent),
        "revlog" => 1 - Math.Pow(1 - position, LogExponent),
        _ => null,
    };

    // NOTE: a chain knob <stage>.<part> names a part of the instance X<stage>. See Chain.Compose.
    private static SpiceSharp.Entities.IEntity? Find(LoadedCircuit circuit, string name) =>
        circuit.Circuit.TryGetEntity(name, out var entity) ? entity
        : name.Contains('.') && circuit.Circuit.TryGetEntity("X" + name, out var part) ? part
        : null;

    private static Diagnostic Error(string message) => new(Severity.Error, message, null);
}
