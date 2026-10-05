using SpiceSharp.Components;
using Ssp.Core.Netlist;

namespace Ssp.Core.Parts;

/// <summary>
/// A switch built from one resistor for each throw. <c>&lt;part&gt;_n</c> runs from the common to throw n. The
/// <c>switch</c> directive closes one throw: its resistor is 1 mohm and the others are 1 Gohm. Position 0 closes none.
/// A switch with one throw is on at 1 and off at 0.
/// </summary>
public static class Switch
{
    /// <summary>The resistance of a closed throw.</summary>
    public const double Closed = 1e-3;

    /// <summary>The resistance of an open throw.</summary>
    public const double Open = 1e9;

    /// <summary>The most throws that a switch can have.</summary>
    public const int MaxThrows = 3;

    /// <summary>The resistance of each throw at a position, throw 1 first.</summary>
    public static double[] Resistances(int throws, int position) =>
        Enumerable.Range(1, throws).Select(t => t == position ? Closed : Open).ToArray();

    /// <summary>The positions that a tap goes through, in order. A switch with one throw is off (0) or on (1).</summary>
    public static int[] Positions(int throws) => throws == 1 ? [0, 1] : Enumerable.Range(1, throws).ToArray();

    /// <summary>The position after a tap: the next of <see cref="Positions"/>, then the first again.</summary>
    public static int Next(int throws, int position)
    {
        var positions = Positions(throws);
        return positions[(Array.IndexOf(positions, position) + 1) % positions.Length];
    }

    /// <summary>The count of throws of a switch: the resistors <c>&lt;part&gt;_1</c> on, in a row.</summary>
    public static int Throws(LoadedCircuit circuit, string part)
    {
        var n = 0;
        while (n < MaxThrows && circuit.Circuit.TryGetEntity($"{part}_{n + 1}", out var entity) && entity is Resistor) n++;
        return n;
    }

    /// <summary>Sets the resistors of each <c>switch</c> directive. A bad switch gives a diagnostic and is skipped.</summary>
    public static IReadOnlyList<Diagnostic> Apply(LoadedCircuit circuit)
    {
        var diagnostics = new List<Diagnostic>();
        foreach (var s in circuit.Directives.Switches)
        {
            var throws = Throws(circuit, s.Part);
            if (throws == 0)
            {
                diagnostics.Add(Error($"Switch {s.Part}: the netlist needs the resistor {s.Part}_1."));
                continue;
            }
            if (s.Position < 0 || s.Position > throws)
            {
                diagnostics.Add(Error($"Switch {s.Part}: position {s.Position} is outside 0 to {throws}."));
                continue;
            }

            var ohms = Resistances(throws, s.Position);
            for (var t = 0; t < throws; t++) ((Resistor)circuit.Circuit[$"{s.Part}_{t + 1}"]).Parameters.Resistance = ohms[t];
        }

        return diagnostics;
    }

    private static Diagnostic Error(string message) => new(Severity.Error, message, null);
}
