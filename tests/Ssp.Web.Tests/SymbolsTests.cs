using Ssp.Core.Parts;
using Ssp.Web.Schematic;

namespace Ssp.Web.Tests;

public class SymbolsTests
{
    static string PartsPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "models", "parts.toml")))
        {
            dir = dir.Parent;
        }
        return Path.Combine(dir?.FullName ?? throw new FileNotFoundException("models/parts.toml"), "models", "parts.toml");
    }

    public static TheoryData<string> Kinds()
    {
        var data = new TheoryData<string>();
        foreach (var kind in Parts.Load(PartsPath()).Rows.Select(r => r.Kind).Distinct())
        {
            data.Add(kind);
        }
        return data;
    }

    [Fact]
    public void DiodeHasTwoPins() => Assert.Equal(2, Symbols.For("diode").Pins.Count);

    [Fact]
    public void OpampHasThreePins() => Assert.Equal(3, Symbols.For("opamp").Pins.Count);

    [Theory]
    [MemberData(nameof(Kinds))]
    public void EveryKindInPartsTableHasASymbol(string kind)
    {
        var symbol = Symbols.For(kind);

        Assert.Contains("<svg", symbol.Svg);
        Assert.NotEmpty(symbol.Pins);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void PinsAreOnTheGrid(string kind)
    {
        foreach (var pin in Symbols.For(kind).Pins)
        {
            Assert.Equal(0, pin.X % Symbols.Grid);
            Assert.Equal(0, pin.Y % Symbols.Grid);
        }
    }

    [Fact]
    public void UnknownKindThrows() => Assert.Throws<KeyNotFoundException>(() => Symbols.For("flux-capacitor"));
}
