namespace Ssp.Core.Import.LtSpice;

/// <summary>A pin position relative to the symbol origin, in LTspice grid units.</summary>
public readonly record struct PinOffset(string Name, int X, int Y);

/// <summary>Pin offsets of the built-in LTspice symbols.</summary>
public static class PinTable
{
    // NOTE: Source of every offset: the PIN lines of the stock LTspice .asy files, as published in
    // https://github.com/evenator/LTSpice-Libraries at commit 8b77969893ad4330b9ec288e4da48121f7914b0a
    // (sym/res.asy, cap.asy, ind.asy, diode.asy, npn.asy, pnp.asy, njf.asy, Opamps/opamp.asy).
    // opamp was cross-checked against https://github.com/ckuhlmann/lt2circuitikz at commit
    // 1b525c62ba71484cef3f85d4b5bf438c5e8b97cb (sym32a/Opamps/opamp.asy): same three pins.
    // Pins are in file order, which is the SpiceOrder of the symbol.
    // UNVERIFIED: not compared with a local LTspice install.
    public static IReadOnlyDictionary<string, PinOffset[]> Builtin { get; } =
        new Dictionary<string, PinOffset[]>
        {
        ["res"] = new PinOffset[]
        {
            new("A", 16, 16),
            new("B", 16, 96),
        },
        ["cap"] = new PinOffset[]
        {
            new("A", 16, 0),
            new("B", 16, 64),
        },
        ["ind"] = new PinOffset[]
        {
            new("A", 16, 16),
            new("B", 16, 96),
        },
        ["diode"] = new PinOffset[]
        {
            new("+", 16, 0),
            new("-", 16, 64),
        },
        ["npn"] = new PinOffset[]
        {
            new("C", 64, 0),
            new("B", 0, 48),
            new("E", 64, 96),
        },
        ["pnp"] = new PinOffset[]
        {
            new("C", 64, 0),
            new("B", 0, 48),
            new("E", 64, 96),
        },
        ["njf"] = new PinOffset[]
        {
            new("D", 48, 0),
            new("G", 0, 64),
            new("S", 48, 96),
        },
        ["opamp"] = new PinOffset[]
        {
            new("invin", -32, 48),
            new("noninvin", -32, 80),
            new("out", 32, 64),
        },
        };
}
