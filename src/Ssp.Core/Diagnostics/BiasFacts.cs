using SpiceSharp.Components;
using Ssp.Core.Analysis;
using Ssp.Core.Netlist;

namespace Ssp.Core.Diagnostics;

/// <summary>A BJT with its pins and the model values that the bias rules use.</summary>
/// <param name="Polarity">1 for NPN, -1 for PNP.</param>
/// <param name="SaturationCurrent">The model IS, in A.</param>
sealed record Bjt(string Name, string Collector, string Base, string Emitter, double Polarity, double SaturationCurrent)
{
    // Thermal voltage at 300 K, in V.
    const double ThermalVoltage = 0.02585;

    /// <summary>Base-emitter voltage, with the sign of an NPN.</summary>
    public double Vbe(OpResult op) => Polarity * (op.NodeVoltages[Base] - op.NodeVoltages[Emitter]);

    /// <summary>Collector-emitter voltage, with the sign of an NPN.</summary>
    public double Vce(OpResult op) => Polarity * (op.NodeVoltages[Collector] - op.NodeVoltages[Emitter]);

    /// <summary>The forward current of the base-emitter junction, IS * exp(Vbe / Vt), in A.</summary>
    public double ForwardCurrent(OpResult op) => SaturationCurrent * Math.Exp(Vbe(op) / ThermalVoltage);
}

/// <summary>Facts about the operating point for the bias rules.</summary>
static class BiasFacts
{
    // SPICE default IS, for a BJT whose model is not in the circuit.
    const double DefaultSaturationCurrent = 1e-16;

    /// <summary>A BJT with a smaller forward current, in A, is cut off.</summary>
    public const double CutOffCurrent = 1e-6;

    /// <summary>The operating point, or null if the solver fails. The solver failure has its own diagnostic.</summary>
    public static OpResult? OperatingPoint(LoadedCircuit circuit) => SolverFailure.OperatingPoint(circuit).Value;

    public static IReadOnlyList<Bjt> Bjts(LoadedCircuit circuit) =>
        circuit.Circuit.OfType<BipolarJunctionTransistor>().Select(q =>
        {
            var model = circuit.Circuit.TryGetEntity(q.Model, out var entity) ? entity as BipolarJunctionTransistorModel : null;
            return new Bjt(
                q.Name,
                q.Nodes[0],
                q.Nodes[1],
                q.Nodes[2],
                model?.Parameters.BipolarType ?? 1.0,
                model?.Parameters.SatCur ?? DefaultSaturationCurrent);
        }).ToList();

    /// <summary>The op-amps: subcircuits with the pins <c>inp inn out vcc vee</c>, as <see cref="Parts.OpAmpModel"/> writes them.</summary>
    public static IReadOnlyList<SubcircuitInstance> OpAmps(LoadedCircuit circuit) =>
        circuit.Subcircuits.Where(x => x.Pins.Count == 5).ToList();
}
