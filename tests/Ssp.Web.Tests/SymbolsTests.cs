using Ssp.Core.Layout;
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

public class SymbolSetTests
{
    public static TheoryData<string, int> PinCounts() => new()
    {
        { "resistor", 2 }, { "pot", 3 }, { "capacitor", 2 }, { "electrolytic", 2 }, { "inductor", 2 },
        { "diode", 2 }, { "led", 2 }, { "npn", 4 }, { "pnp", 4 }, { "njf", 3 }, { "pjf", 3 },
        { "opamp", 3 }, { "opamp5", 5 }, { "battery", 2 }, { "source", 2 }, { "jack-in", 1 }, { "jack-out", 1 }, { "isource", 2 }, { "ground", 1 }, { "rail", 1 },
    };

    public static TheoryData<string, int, bool> Orientations()
    {
        var data = new TheoryData<string, int, bool>();
        foreach (var kind in Symbols.Kinds)
        {
            foreach (var rotation in new[] { 0, 90, 180, 270 })
            {
                data.Add(kind, rotation, false);
                data.Add(kind, rotation, true);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(PinCounts))]
    public void KindHasItsPinCount(string kind, int pins) => Assert.Equal(pins, Symbols.For(kind).Pins.Count);

    [Theory]
    [MemberData(nameof(Orientations))]
    public void PinsStayOnTheGridAfterRotationAndFlip(string kind, int rotation, bool flip)
    {
        var placement = new PartPlacement("P1", 40, 70, rotation, flip);
        foreach (var pin in Symbols.For(kind).Pins)
        {
            var (x, y) = SchematicRenderer.Place(placement, pin.X, pin.Y);
            Assert.Equal(0, Math.Round(x, 6) % Symbols.Grid);
            Assert.Equal(0, Math.Round(y, 6) % Symbols.Grid);
        }
    }

    [Theory]
    [MemberData(nameof(Orientations))]
    public void PinsAreDistinct(string kind, int rotation, bool flip)
    {
        var placement = new PartPlacement("P1", 0, 0, rotation, flip);
        var placed = Symbols.For(kind).Pins.Select(p => SchematicRenderer.Place(placement, p.X, p.Y)).Select(p => (Math.Round(p.X), Math.Round(p.Y)));
        Assert.Equal(Symbols.For(kind).Pins.Count, placed.Distinct().Count());
    }

    // The placed arrow vector, and the placed vector from pin a to pin b.
    static ((double X, double Y) Arrow, (double X, double Y) Pins) Vectors(string kind, string a, string b, int rotation, bool flip)
    {
        var symbol = Symbols.For(kind);
        var arrow = symbol.Arrow ?? throw new Xunit.Sdk.XunitException($"{kind} has no arrow.");
        var p = new PartPlacement("P1", 0, 0, rotation, flip);
        var tail = SchematicRenderer.Place(p, arrow.TailX, arrow.TailY);
        var head = SchematicRenderer.Place(p, arrow.HeadX, arrow.HeadY);
        var pa = symbol.Pins.Single(x => x.Name == a);
        var pb = symbol.Pins.Single(x => x.Name == b);
        var from = SchematicRenderer.Place(p, pa.X, pa.Y);
        var to = SchematicRenderer.Place(p, pb.X, pb.Y);
        return ((head.X - tail.X, head.Y - tail.Y), (to.X - from.X, to.Y - from.Y));
    }

    static double Dot((double X, double Y) u, (double X, double Y) v) => u.X * v.X + u.Y * v.Y;

    public static TheoryData<int, bool> Turns()
    {
        var data = new TheoryData<int, bool>();
        foreach (var rotation in new[] { 0, 90, 180, 270 })
        {
            data.Add(rotation, false);
            data.Add(rotation, true);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Turns))]
    public void DiodeArrowPointsFromAnodeToCathode(int rotation, bool flip)
    {
        foreach (var kind in new[] { "diode", "led" })
        {
            var (arrow, pins) = Vectors(kind, "A", "K", rotation, flip);
            Assert.True(Dot(arrow, pins) > 0, $"{kind} at {rotation} flip {flip}: arrow is not anode to cathode.");
        }
    }

    [Theory]
    [MemberData(nameof(Turns))]
    public void NpnArrowPointsOutAndPnpArrowPointsIn(int rotation, bool flip)
    {
        // Out: away from the base, toward the emitter pin.
        var (npn, npnPins) = Vectors("npn", "B", "E", rotation, flip);
        var (pnp, pnpPins) = Vectors("pnp", "B", "E", rotation, flip);
        Assert.True(Dot(npn, npnPins) > 0, "NPN arrow does not point out.");
        Assert.True(Dot(pnp, pnpPins) < 0, "PNP arrow does not point in.");
    }

    [Theory]
    [MemberData(nameof(Turns))]
    public void NjfArrowPointsInAndPjfArrowPointsOut(int rotation, bool flip)
    {
        // In: from the gate pin toward the channel, which is between the drain and the source.
        var (njf, njfPins) = Vectors("njf", "G", "D", rotation, flip);
        var (pjf, pjfPins) = Vectors("pjf", "G", "D", rotation, flip);
        Assert.True(Dot(njf, njfPins) > 0, "N-channel arrow does not point in.");
        Assert.True(Dot(pjf, pjfPins) < 0, "P-channel arrow does not point out.");
    }

    [Theory]
    [MemberData(nameof(Turns))]
    public void FlipMirrorsTheArrow(int rotation, bool _)
    {
        foreach (var kind in new[] { "npn", "pnp" })
        {
            var (plain, _) = Vectors(kind, "B", "E", rotation, false);
            var (flipped, _) = Vectors(kind, "B", "E", rotation, true);
            Assert.NotEqual((Math.Round(plain.X, 3), Math.Round(plain.Y, 3)), (Math.Round(flipped.X, 3), Math.Round(flipped.Y, 3)));
        }
    }

    [Fact]
    public void ArrowOfNpnAndPnpIsReversed()
    {
        var npn = Symbols.For("npn").Arrow!;
        var pnp = Symbols.For("pnp").Arrow!;
        Assert.True(Dot((npn.HeadX - npn.TailX, npn.HeadY - npn.TailY), (pnp.HeadX - pnp.TailX, pnp.HeadY - pnp.TailY)) < 0);
    }
}
