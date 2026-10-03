using System.Globalization;
using System.Text;

namespace Ssp.Core.Parts;

/// <summary>
/// An op-amp macro-model made from behavioral sources.
/// A transconductance stage drives a resistor and a capacitor. The resistor sets the DC gain and the capacitor sets the gain-bandwidth product.
/// The stage current is limited with tanh, so the capacitor charges at the slew rate at most.
/// A clamp keeps the internal node inside the supply rails. The output stops <see cref="RailDrop"/> volts short of each rail.
/// </summary>
/// <param name="Gain">Open-loop DC voltage gain, in V/V.</param>
/// <param name="Gbw">Gain-bandwidth product, in Hz.</param>
/// <param name="SlewRate">Largest output rate of change, in V/s.</param>
/// <param name="RailDrop">Distance between a supply rail and the output limit, in V.</param>
/// <param name="OutputResistance">Output resistance, in ohms.</param>
/// <param name="InputResistance">Differential input resistance, in ohms.</param>
public sealed record OpAmpModel(
    double Gain,
    double Gbw,
    double SlewRate,
    double RailDrop,
    double OutputResistance = 100.0,
    double InputResistance = 1e9)
{
    // Resistance of the gain stage. A large value keeps the stage current, and so the capacitor, small.
    private const double StageResistance = 100e6;

    // Distance that the internal node can go past a rail when the full stage current flows into the clamp, in V.
    // NOTE: SPICE checks convergence on node voltages. A stiff clamp hides a current error larger than the stage current
    // inside the voltage tolerance, and the operating point latches at a rail. So scale the clamp to the stage current.
    private const double ClampOvershoot = 1.0;

    // Conductance that pulls the output back inside the rails. A hard clamp on the source voltage stops the Newton solver from converging.
    private const double OutputClampConductance = 100.0;

    // Squared size of the rounded corner of a clamp, in V^2.
    private const string Corner = "0.000001";

    /// <summary>TL072. Values from the TI TL07x datasheet. See models/opamp-tl072.cir.</summary>
    public static OpAmpModel Tl072 { get; } = new(Gain: 200e3, Gbw: 3e6, SlewRate: 13e6, RailDrop: 1.5, OutputResistance: 200, InputResistance: 1e12);

    /// <summary>LM308 with a 30 pF compensation capacitor. See models/opamp-lm308.cir.</summary>
    public static OpAmpModel Lm308 { get; } = new(Gain: 300e3, Gbw: 1e6, SlewRate: 0.3e6, RailDrop: 1.0, OutputResistance: 100, InputResistance: 40e6);

    /// <summary>JRC4558 (NJM4558). See models/opamp-jrc4558.cir.</summary>
    public static OpAmpModel Jrc4558 { get; } = new(Gain: 100e3, Gbw: 3e6, SlewRate: 1.7e6, RailDrop: 2.0, OutputResistance: 75, InputResistance: 5e6);

    /// <summary>
    /// Writes the model as a SPICE subcircuit with the pins <c>inp inn out vcc vee</c>.
    /// The text ends with a newline.
    /// </summary>
    public string ToSubcircuit(string name)
    {
        var gm = Gain / StageResistance;
        var cc = gm / (2.0 * Math.PI * Gbw);
        var imax = SlewRate * cc;
        var clamp = imax / ClampOvershoot;

        var text = new StringBuilder();
        text.AppendLine($"* Pins: inp inn out vcc vee. Gain {Num(Gain)} V/V, GBW {Num(Gbw)} Hz, slew rate {Num(SlewRate)} V/s, rail drop {Num(RailDrop)} V.");
        text.AppendLine($".subckt {name} inp inn out vcc vee");
        text.AppendLine($"Rid inp inn {Num(InputResistance)}");
        text.AppendLine($"Bgm 0 x I={{{Num(imax)}*tanh({Num(gm)}*V(inp,inn)/{Num(imax)})}}");
        text.AppendLine($"Rp x 0 {Num(StageResistance)}");
        text.AppendLine($"Cc x 0 {Num(cc)}");
        text.AppendLine($"Bclamp x 0 I={{{Num(clamp)}*(V(x)-{Clamp("V(x)", "V(vee)", "V(vcc)")})}}");
        text.AppendLine("Bo o 0 V={V(x)}");
        text.AppendLine($"Rout o out {Num(OutputResistance)}");
        text.AppendLine($"Bout out 0 I={{{Num(OutputClampConductance)}*(V(out)-{Clamp("V(out)", $"V(vee)+{Num(RailDrop)}", $"V(vcc)-{Num(RailDrop)}")})}}");
        text.AppendLine($".ends {name}");
        return text.ToString();
    }

    // NOTE: a hard min/max stops the Newton solver from converging with a feedback loop. Use a clamp with a rounded corner of about 1 mV.
    private static string Clamp(string value, string low, string high)
    {
        var raised = $"(0.5*({value}+{low}+sqrt(({value}-({low}))*({value}-({low}))+{Corner})))";
        return $"(0.5*({raised}+{high}-sqrt(({raised}-({high}))*({raised}-({high}))+{Corner})))";
    }

    private static string Num(double value) => value.ToString("G6", CultureInfo.InvariantCulture);
}
