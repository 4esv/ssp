using Ssp.Core.Analysis;
using Ssp.Core.Diagnostics;
using Ssp.Core.Netlist;

namespace Ssp.Core.Tests;

public class SignalRulesTests
{
    private const int Fs = 44_100;

    // 10 ms of 1 kHz. The fixtures are resistive, so the output settles at once.
    private const int Samples = Fs / 100;

    static LoadedCircuit Load(string fixture) => NetlistLoader.Load(Fixtures.Read(fixture));

    static ZResult Impedance(string fixture) => Analyses.Impedance(Load(fixture));

    static double[] Render(string fixture, double amplitude)
    {
        var input = new double[Samples];
        for (var i = 0; i < input.Length; i++)
        {
            input[i] = amplitude * Math.Sin(2.0 * Math.PI * 1_000.0 * i / Fs);
        }

        return Analyses.Render(Load(fixture), input, Fs, 1);
    }

    static Diagnostic AssertOne(IRule rule, string fixture)
    {
        var diagnostic = Assert.Single(rule.Check(Load(fixture)));
        Assert.Equal(Severity.Warning, diagnostic.Severity);
        Assert.DoesNotContain("SpiceSharp", diagnostic.Message);
        Assert.DoesNotContain("Rule", diagnostic.Message);
        return diagnostic;
    }

    // signal-low-zin.cir: Zin = 10 kOhm.
    [Fact]
    public void InputImpedanceBelow100KilohmGivesADiagnostic()
    {
        var diagnostic = AssertOne(new LowInputImpedanceRule(Impedance("signal-low-zin.cir")), "signal-low-zin.cir");
        Assert.Contains("10.0 kΩ", diagnostic.Message);
    }

    // zin-1meg.cir: Zin = 1 MOhm.
    [Fact]
    public void InputImpedanceAbove100KilohmGivesNoDiagnostic() =>
        Assert.Empty(new LowInputImpedanceRule(Impedance("zin-1meg.cir")).Check(Load("zin-1meg.cir")));

    // signal-high-zout.cir: Zout = 50 kOhm.
    [Fact]
    public void OutputImpedanceAbove10KilohmGivesADiagnostic()
    {
        var diagnostic = AssertOne(new HighOutputImpedanceRule(Impedance("signal-high-zout.cir")), "signal-high-zout.cir");
        Assert.Contains("50.0 kΩ", diagnostic.Message);
    }

    // signal-low-zin.cir: Zout = 900 Ohm.
    [Fact]
    public void OutputImpedanceBelow10KilohmGivesNoDiagnostic() =>
        Assert.Empty(new HighOutputImpedanceRule(Impedance("signal-low-zin.cir")).Check(Load("signal-low-zin.cir")));

    // signal-low-zin.cir has a gain of 0.9: a 2 V peak gives 1.8 V, past full scale.
    [Fact]
    public void ClippedRenderOutputGivesADiagnostic()
    {
        var diagnostic = AssertOne(new OutputClippingRule(Render("signal-low-zin.cir", 2.0)), "signal-low-zin.cir");
        Assert.Contains("1.80 V", diagnostic.Message);
    }

    // A 1 V peak gives 0.9 V, inside full scale.
    [Fact]
    public void RenderOutputInsideFullScaleGivesNoDiagnostic() =>
        Assert.Empty(new OutputClippingRule(Render("signal-low-zin.cir", 1.0)).Check(Load("signal-low-zin.cir")));

    [Fact]
    public void ImpedanceRulesGiveNoDiagnosticWhenTheAnalysisDidNotRun()
    {
        var skipped = new ZResult([], [], [], [new Diagnostic(Severity.Warning, "skipped", null)]);
        var circuit = Load("signal-low-zin.cir");

        Assert.Empty(new LowInputImpedanceRule(skipped).Check(circuit));
        Assert.Empty(new HighOutputImpedanceRule(skipped).Check(circuit));
    }
}
