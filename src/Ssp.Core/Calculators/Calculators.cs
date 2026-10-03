namespace Ssp.Core.Calculators;

/// <summary>Pure functions for common pedal design values. Units are SI: ohm, farad, volt, ampere, hertz.</summary>
public static class Calculators
{
    public readonly record struct BjtBiasPoint(double Base, double Emitter, double Collector, double Current);

    public readonly record struct SallenKeyResult(double CutoffHz, double Q);

    public readonly record struct ToneStackResult(double LowPassHz, double HighPassHz);

    /// <summary>Cutoff of a first-order RC filter: 1 / (2 pi R C).</summary>
    public static double RcFilter(double resistance, double capacitance) =>
        1.0 / (2.0 * Math.PI * resistance * capacitance);

    /// <summary>Output of an unloaded divider: Vin * R2 / (R1 + R2).</summary>
    public static double Divider(double vin, double r1, double r2) => vin * r2 / (r1 + r2);

    /// <summary>
    /// DC bias of a voltage-divider biased BJT stage. The model ignores base current.
    /// </summary>
    public static BjtBiasPoint BjtBias(double vcc, double r1, double r2, double rc, double re, double vbe = 0.7)
    {
        var vb = Divider(vcc, r1, r2);
        var ve = vb - vbe;
        var ie = ve / re;
        return new BjtBiasPoint(vb, ve, vcc - ie * rc, ie);
    }

    /// <summary>Series resistor for an LED: (Vs - Vf) / I.</summary>
    public static double LedResistor(double supply, double forwardVoltage, double current) =>
        (supply - forwardVoltage) / current;

    /// <summary>Voltage gain of a non-inverting stage: 1 + Rf / Rg.</summary>
    public static double Gain(double feedback, double ground) => 1.0 + feedback / ground;

    /// <summary>Unity-gain Sallen-Key low-pass. C2 is the capacitor to ground.</summary>
    public static SallenKeyResult SallenKey(double r1, double r2, double c1, double c2)
    {
        var root = Math.Sqrt(r1 * r2 * c1 * c2);
        return new SallenKeyResult(1.0 / (2.0 * Math.PI * root), root / (c2 * (r1 + r2)));
    }

    /// <summary>Corners of the low-pass and high-pass legs of a passive blend tone control.</summary>
    public static ToneStackResult ToneStack(double lowR, double lowC, double highR, double highC) =>
        new(RcFilter(lowR, lowC), RcFilter(highR, highC));
}
