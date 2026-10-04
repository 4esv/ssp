using System.Globalization;
using System.Text;

namespace Ssp.Core.Parts;

/// <summary>
/// An op-amp macro-model made from behavioral sources.
/// A transconductance stage drives a resistor and a capacitor. The resistor sets the DC gain and the capacitor sets the gain-bandwidth product.
/// The stage current is limited with tanh, so the capacitor charges at the slew rate at most.
/// A clamp keeps the internal node inside the supply rails. The output stops <see cref="RailDrop"/> volts short of each rail.
/// The low output limit is a node. It stops at the high limit when the rails are closer than two rail drops,
/// as in the first steps of the source stepping of the operating point. A low limit above the high limit stops the solver from converging.
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

    /// <summary>TL072. Values from the TI TL07x datasheet SLOS080W (D and P packages). See models/opamp-tl072.cir.</summary>
    public static OpAmpModel Tl072 { get; } = new(Gain: 200e3, Gbw: 5.25e6, SlewRate: 20e6, RailDrop: 1.5, OutputResistance: 200, InputResistance: 1e12);

    /// <summary>LM308 with a 30 pF compensation capacitor. See models/opamp-lm308.cir.</summary>
    public static OpAmpModel Lm308 { get; } = new(Gain: 300e3, Gbw: 1e6, SlewRate: 0.3e6, RailDrop: 1.0, OutputResistance: 100, InputResistance: 40e6);

    /// <summary>JRC4558 (NJM4558). See models/opamp-jrc4558.cir.</summary>
    public static OpAmpModel Jrc4558 { get; } = new(Gain: 100e3, Gbw: 3e6, SlewRate: 1e6, RailDrop: 1.0, OutputResistance: 75, InputResistance: 5e6);

    /// <summary>
    /// Writes the model as a SPICE subcircuit with the pins <c>inp inn out vcc vee</c>.
    /// Lines end with \n on each platform, and the text ends with a newline.
    /// </summary>
    public string ToSubcircuit(string name)
    {
        var gm = Gain / StageResistance;
        var cc = gm / (2.0 * Math.PI * Gbw);
        var imax = SlewRate * cc;
        var clamp = imax / ClampOvershoot;

        var text = new StringBuilder();
        text.Append($"* Pins: inp inn out vcc vee. Gain {Num(Gain)} V/V, GBW {Num(Gbw)} Hz, slew rate {Num(SlewRate)} V/s, rail drop {Num(RailDrop)} V.\n");
        text.Append($".subckt {name} inp inn out vcc vee\n");
        text.Append($"Rid inp inn {Num(InputResistance)}\n");
        text.Append($"Bgm 0 x I={{{Num(imax)}*tanh({Num(gm)}*V(inp,inn)/{Num(imax)})}}\n");
        text.Append($"Rp x 0 {Num(StageResistance)}\n");
        text.Append($"Cc x 0 {Num(cc)}\n");
        text.Append($"Bclamp x 0 I={{{Num(0.5 * clamp)}*{Excess("V(x)-V(vcc)", "V(x)-V(vee)")}}}\n");
        text.Append("Bo o 0 V={V(x)}\n");
        text.Append($"Rout o out {Num(OutputResistance)}\n");
        var crossing = $"V(vee)-V(vcc)+{Num(2 * RailDrop)}";
        text.Append($"Blo lo 0 V={{V(vee)+{Num(RailDrop)}-0.5*({crossing}+sqrt(({crossing})*({crossing})+{Corner}))}}\n");
        text.Append($"Bout out 0 I={{{Num(0.5 * OutputClampConductance)}*{Excess($"V(out)-V(vcc)+{Num(RailDrop)}", "V(out)-V(lo)")}}}\n");
        text.Append($".ends {name}\n");
        return text.ToString();
    }

    // Twice the distance of a value above the high limit or below the low limit, and 0 between them.
    // The arguments are the value minus the high limit and the value minus the low limit.
    // NOTE: a hard min/max stops the Newton solver from converging with a feedback loop. Each limit is a sqrt with a rounded corner of about 1 mV.
    // PERF: one sqrt for each limit and no clamp inside a clamp. The engine loads the expression and its derivative to each node voltage
    // in each Newton iteration, and the nested clamp made the derivatives large (#223).
    private static string Excess(string aboveHigh, string aboveLow) =>
        $"(sqrt(({aboveHigh})*({aboveHigh})+{Corner})-sqrt(({aboveLow})*({aboveLow})+{Corner})+{aboveHigh}+{aboveLow})";

    private static string Num(double value) => value.ToString("G6", CultureInfo.InvariantCulture);
}
