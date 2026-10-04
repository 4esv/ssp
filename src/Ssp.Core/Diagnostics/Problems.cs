using SpiceSharp.Components;
using Ssp.Core.Netlist;

namespace Ssp.Core.Diagnostics;

/// <summary>A fault in plain words, with the part, and the pin if one pin causes it, that the editor highlights.</summary>
/// <param name="Pin">The zero-based pin of the part, or null when the whole part is the cause.</param>
public sealed record Problem(Severity Severity, string Message, string Reference, int? Pin);

/// <summary>Maps the structural faults to the part or pin that causes them. It runs no simulation.</summary>
public static class Problems
{
    public static IReadOnlyList<Problem> Find(LoadedCircuit circuit)
    {
        var problems = new List<Problem>();
        var parts = CircuitFacts.Components(circuit).Where(c => !c.Name.Contains('.')).ToList();
        if (circuit.NodeNames.Count > 0 && !circuit.NodeNames.Contains(CircuitFacts.Ground) && parts.Count > 0)
        {
            var part = parts.FirstOrDefault(c => c is VoltageSource) ?? parts[0];
            problems.Add(new Problem(Severity.Error, $"No part is connected to ground. Ground one pin of {part.Name}, or use the Ground tool.", part.Name, null));
        }

        problems.AddRange(Dangling(circuit, parts));
        problems.AddRange(OpAmpsWithNoBias(circuit, parts));
        return problems;
    }

    static IEnumerable<Problem> Dangling(LoadedCircuit circuit, List<IComponent> parts)
    {
        var exempt = new HashSet<string?> { CircuitFacts.Ground, circuit.Directives.Input, circuit.Directives.Output };
        var uses = CircuitFacts.Components(circuit).SelectMany(c => c.Nodes).Concat(circuit.Subcircuits.SelectMany(x => x.Pins))
            .GroupBy(n => n, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var part in parts)
        {
            for (var i = 0; i < part.Nodes.Count; i++)
            {
                if (uses[part.Nodes[i]] == 1 && !exempt.Contains(part.Nodes[i]))
                {
                    yield return new Problem(Severity.Warning, $"{part.Name} pin {i + 1} connects to nothing. Wire it to another pin or to ground.", part.Name, i);
                }
            }
        }
    }

    // NOTE: A DC path joins nodes through every part except a capacitor. An op-amp input needs a path to ground or to a driven node.
    static IEnumerable<Problem> OpAmpsWithNoBias(LoadedCircuit circuit, List<IComponent> parts)
    {
        var opAmps = BiasFacts.OpAmps(circuit);
        if (opAmps.Count == 0)
        {
            yield break;
        }

        var group = new Dictionary<string, string>(StringComparer.Ordinal);
        string Find(string n) => group.GetValueOrDefault(n, n) == n ? n : group[n] = Find(group[n]);
        void Join(string a, string b) => group[Find(a)] = Find(b);
        foreach (var part in parts.Where(p => p is not Capacitor))
        {
            foreach (var node in part.Nodes.Skip(1))
            {
                Join(part.Nodes[0], node);
            }
        }

        var anchored = new HashSet<string>(StringComparer.Ordinal) { Find(CircuitFacts.Ground) };
        foreach (var x in opAmps)
        {
            anchored.Add(Find(x.Pins[2]));
        }

        foreach (var x in opAmps)
        {
            foreach (var pin in new[] { 0, 1 })
            {
                if (!anchored.Contains(Find(x.Pins[pin])))
                {
                    var name = pin == 0 ? "positive" : "negative";
                    yield return new Problem(Severity.Error, $"The {name} input of {x.Name} has no bias. Connect it to ground, to a divider, or to the output.", x.Name, pin);
                }
            }
        }
    }
}
