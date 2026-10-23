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
}
