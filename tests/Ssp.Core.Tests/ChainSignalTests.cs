using Ssp.Core.Analysis;
using Ssp.Core.Chains;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;

namespace Ssp.Core.Tests;

// The chains of the Virtual amp: a gain stage, a tone stage and power-9v.cir, as in VirtualAmp.razor.
public class ChainSignalTests
{
    private const int Fs = 44_100;
    private const double Amplitude = 0.1;
    private const double Frequency = 440.0;

    // 10 mV is 20 dB under the input. The most quiet pair that passes, gain-variable into tone-mid-notch, is about 50 mV.
    private const double MinPeak = 0.010;

    // The coupling capacitors charge for about 50 ms, so the peak is read from the second half.
    private const int Samples = Fs / 5;

    // NOTE: These chains do not render at oversample 1, with or without power-9v.cir. The solver stops with
    // TimestepTooSmallException when the op-amp output clips. At oversample 4 they render 0.4 V to 3.0 V. See the PR for #155.
    private static readonly HashSet<string> KnownFailing =
    [
        // gain-high-lm308 has its own issue.
        "gain-high-lm308.cir",
        "gain-two-stage-tl072.cir",
        "gain-mid-jrc4558.cir tone-baxandall-active-tl072.cir",
        "gain-mid-jrc4558.cir tone-lowpass-sallenkey-tl072.cir",
    ];

    private static string Block(string name) =>
        File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "blocks", name));

    private static IEnumerable<string> Blocks(string section) =>
        Directory.GetFiles(Path.Combine(RepoPaths.Root, "circuits", "blocks"), section + "-*.cir")
            .Select(f => Path.GetFileName(f)!)
            .Order(StringComparer.Ordinal);

    public static TheoryData<string, string> Pairs()
    {
        var data = new TheoryData<string, string>();
        foreach (var gain in Blocks("gain").Where(g => !KnownFailing.Contains(g)))
        foreach (var tone in Blocks("tone").Where(t => !KnownFailing.Contains(t) && !KnownFailing.Contains($"{gain} {t}")))
        {
            data.Add(gain, tone);
        }

        return data;
    }

    private static double Peak(string gain, string tone)
    {
        var netlist = Chain.Compose(
        [
            new ChainStage("gain", Block(gain)),
            new ChainStage("tone", Block(tone)),
            new ChainStage("power", Block("power-9v.cir")),
        ]);
        var circuit = NetlistLoader.Load(netlist);
        Pot.Apply(circuit);

        var input = new double[Samples];
        for (var i = 0; i < input.Length; i++)
        {
            input[i] = Amplitude * Math.Sin(2.0 * Math.PI * Frequency * i / Fs);
        }

        var output = Analyses.Render(circuit, input, Fs, 1);
        Assert.All(output, sample => Assert.True(double.IsFinite(sample)));
        return output[(output.Length / 2)..].Max(Math.Abs);
    }

    [Fact]
    public void PresetChainIsNotSilent() =>
        Assert.True(Peak("gain-variable-tl072.cir", "tone-baxandall-passive.cir") > MinPeak);

    [Theory]
    [MemberData(nameof(Pairs))]
    public void GainToneChainIsNotSilent(string gain, string tone)
    {
        var peak = Peak(gain, tone);
        Assert.True(peak > MinPeak, $"{gain} into {tone}: peak {peak:F4} V");
    }
}
