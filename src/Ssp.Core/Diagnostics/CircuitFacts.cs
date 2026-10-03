using SpiceSharp.Components;
using Ssp.Core.Netlist;

namespace Ssp.Core.Diagnostics;

static class CircuitFacts
{
    public const string Ground = "0";

    public static IEnumerable<IComponent> Components(LoadedCircuit circuit) =>
        circuit.Circuit.OfType<IComponent>();

    /// <summary>The input node: the ssp:input node, else the positive node of the first voltage source.</summary>
    public static string? InputNode(LoadedCircuit circuit) =>
        circuit.Directives.Input ?? circuit.Circuit.OfType<VoltageSource>().FirstOrDefault()?.Nodes[0];

    /// <summary>The output node: the ssp:output node, else the first node that is not ground or the input.</summary>
    public static string? OutputNode(LoadedCircuit circuit) =>
        circuit.Directives.Output ?? circuit.NodeNames.FirstOrDefault(n => n != Ground && n != InputNode(circuit));
}
