using Ssp.Core.Netlist;

namespace Ssp.Core.Tests;

// NOTE: #337: a mistake in the netlist gives an error with the line and the part reference.
public class NetlistMistakeTests
{
    static Diagnostic Single(string netlist, RunOptions? options = null)
    {
        var result = Runner.Run(netlist, options ?? new RunOptions());
        var errors = result.Diagnostics.Where(d => d.Severity == Severity.Error).ToList();
        Assert.Single(errors);
        Assert.Null(result.OperatingPoint);
        return errors[0];
    }

    [Fact]
    public void ResistorWithoutValueNamesTheLineAndThePart()
    {
        var d = Single("* t\nV1 in 0 DC 1\nR1 in out\nR2 out 0 1k\n.END\n");

        Assert.Equal(3, d.Line);
        Assert.Equal("R1 on line 3 has no value.", d.Message);
    }

    [Fact]
    public void UnknownModelNamesTheLineThePartAndTheModel()
    {
        var d = Single("* t\nV1 in 0 DC 1\nR1 in out 1k\nD1 out 0 NOSUCH\n.END\n");

        Assert.Equal(4, d.Line);
        Assert.Equal("D1 on line 4 uses the model NOSUCH. The netlist has no .model line with that name.", d.Message);
    }

    [Fact]
    public void NegativeResistanceIsAnError()
    {
        var d = Single("* t\nV1 in 0 DC 1\nR1 in out -5\nR2 out 0 1k\n.END\n");

        Assert.Equal(3, d.Line);
        Assert.Equal("R1 on line 3 has a value of -5. A resistor needs a value above 0.", d.Message);
    }

    [Fact]
    public void ZeroResistanceIsAnError()
    {
        var d = Single("* t\nV1 in 0 DC 1\nR1 in out 1k\nR2 out 0 0\n.END\n");

        Assert.Equal(4, d.Line);
        Assert.Equal("R2 on line 4 has a value of 0. A resistor needs a value above 0.", d.Message);
    }

    [Fact]
    public void NegativeOverrideNamesThePartWithoutDotNetWording()
    {
        var options = new RunOptions { Overrides = [Overrides.Parse("R1=-1")] };
        var d = Single("* t\nV1 in 0 DC 1\nR1 in out 1k\nR2 out 0 1k\n.END\n", options);

        Assert.Equal("R1 cannot take the value -1. A resistor needs a value above 0.", d.Message);
        Assert.DoesNotContain("Parameter", d.Message);
        Assert.DoesNotContain("\n", d.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n\n")]
    public void EmptyNetlistIsItsOwnError(string netlist)
    {
        var d = Single(netlist);

        Assert.Equal("The netlist is empty. Add a title line and at least one part.", d.Message);
    }

    // NOTE: #364: SpiceSharp reads the number, an optional scale suffix, and ignores the letters after. 10x is 10 meg (1e7).
    static List<Diagnostic> Warnings(string value, string part = "C1 out 0")
    {
        var loaded = NetlistLoader.Load($"* t\nV1 in 0 DC 1\nR1 in out 1k\n{part} {value}\n.END\n");
        Assert.DoesNotContain(loaded.Diagnostics, d => d.Severity == Severity.Error);
        return loaded.Diagnostics.Where(d => d.Severity == Severity.Warning).ToList();
    }

    [Fact]
    public void LettersThatAreNotAUnitGiveAWarningWithTheNumberUsed()
    {
        var w = Assert.Single(Warnings("10x"));

        Assert.Equal(4, w.Line);
        Assert.Equal("C1 has the value 10x. ssp reads it as 10000000. The letters 'x' are not a unit.", w.Message);
    }

    [Fact]
    public void LettersAfterTheScaleSuffixGiveAWarning()
    {
        var w = Assert.Single(Warnings("10kxyz", "L1 out 0"));

        Assert.Equal("L1 has the value 10kxyz. ssp reads it as 10000. The letters 'xyz' are not a unit.", w.Message);
    }

    [Theory]
    [InlineData("10")]
    [InlineData("10meg")]
    [InlineData("10m")]
    [InlineData("10u")]
    [InlineData("10uF")]
    [InlineData("10kohm")]
    [InlineData("10KOHM")]
    [InlineData("10megohm")]
    [InlineData("10mF")]
    [InlineData("1e3")]
    [InlineData("10f")]
    public void ValidValuesGiveNoWarning(string value)
    {
        Assert.Empty(Warnings(value));
    }
}
