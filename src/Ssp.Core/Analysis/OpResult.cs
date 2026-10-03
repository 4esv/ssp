namespace Ssp.Core.Analysis;

/// <summary>
/// DC operating point. Voltages are in volts, currents in amperes, powers in watts.
/// </summary>
/// <param name="NodeVoltages">Voltage of each node, keyed by node name.</param>
/// <param name="SourceCurrents">Current of each voltage source, keyed by source name. SPICE sign: positive into the positive pin.</param>
/// <param name="DevicePowers">Power dissipated by each device, keyed by device name. A source that delivers power has a negative value.</param>
public sealed record OpResult(
    IReadOnlyDictionary<string, double> NodeVoltages,
    IReadOnlyDictionary<string, double> SourceCurrents,
    IReadOnlyDictionary<string, double> DevicePowers);
