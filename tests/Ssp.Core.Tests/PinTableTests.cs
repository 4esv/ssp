using Ssp.Core.Import.LtSpice;
using Xunit;

namespace Ssp.Core.Tests;

public class PinTableTests
{
    [Theory]
    [InlineData("res", 2)]
    [InlineData("cap", 2)]
    [InlineData("ind", 2)]
    [InlineData("diode", 2)]
    [InlineData("npn", 3)]
    [InlineData("pnp", 3)]
    [InlineData("njf", 3)]
    [InlineData("opamp", 3)]
    public void Table_has_symbol_with_pin_count(string symbol, int pins)
    {
        Assert.True(PinTable.Builtin.TryGetValue(symbol, out var table));
        Assert.Equal(pins, table.Length);
    }

    [Fact]
    public void Table_has_exactly_the_eight_symbols()
    {
        Assert.Equal(8, PinTable.Builtin.Count);
    }

    [Fact]
    public void Resistor_pins_are_at_the_stock_offsets()
    {
        var pins = PinTable.Builtin["res"];
        Assert.Equal(new PinOffset("A", 16, 16), pins[0]);
        Assert.Equal(new PinOffset("B", 16, 96), pins[1]);
    }

    [Fact]
    public void Npn_pins_are_at_the_stock_offsets()
    {
        var pins = PinTable.Builtin["npn"];
        Assert.Equal(new PinOffset("C", 64, 0), pins[0]);
        Assert.Equal(new PinOffset("B", 0, 48), pins[1]);
        Assert.Equal(new PinOffset("E", 64, 96), pins[2]);
    }
}
