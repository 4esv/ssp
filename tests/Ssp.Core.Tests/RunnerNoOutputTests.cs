using Ssp.Core.Netlist;

namespace Ssp.Core.Tests;

public class RunnerNoOutputTests
{
    const string Spdt = """
        * spdt divider
        V1 in 0 DC 9
        R1 in a 1k
        RSW1_1 a b 1m
        RSW1_2 a c 1G
        * ssp:switch RSW1 1
        R2 b 0 1k
        R3 c 0 3k
        .END
        """;

    [Fact]
    public void NoOutputNodeGivesNodeVoltagesAndAnInfoLine()
    {
        var result = Runner.Run(Spdt, new RunOptions());

        Assert.NotNull(result.OperatingPoint);
        Assert.Equal(4.5, result.OperatingPoint.NodeVoltages["b"], 3);
        Assert.Equal(0.0, result.OperatingPoint.NodeVoltages["c"], 3);
        Assert.Contains(result.Diagnostics, d => d.Severity == Severity.Info
            && d.Message == "No output node. Name a node out or add * ssp:output <node> to see output impedance and noise.");
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Severity.Error);
        Assert.Null(result.Impedance);
        Assert.Null(result.Noise);
    }

    public static TheoryData<string, string> OddNetlists => new()
    {
        { "no output", "V1 in 0 DC 1\nR1 in a 1k\nR2 a 0 1k\n.END\n" },
        { "no output with input", "* ssp:input in\nV1 in 0 DC 1 AC 1\nR1 in a 1k\nR2 a 0 1k\n.END\n" },
        { "missing ssp:output", "* ssp:output nowhere\nV1 in 0 DC 1\nR1 in out 1k\nR2 out 0 1k\n.END\n" },
        { "no input", "* ssp:input nowhere\nV1 in 0 DC 1\nR1 in out 1k\nR2 out 0 1k\n.END\n" },
        { "no source", "R1 a out 1k\nR2 out 0 1k\n.END\n" },
        { "floating node", "V1 in 0 DC 1\nR1 in out 1k\nR2 out 0 1k\nR3 x y 1k\n.END\n" },
        { "empty", "" },
        { "only a title", "* nothing\n.END\n" },
        { "garbage", "this is not a netlist\n" },
    };

    [Theory]
    [MemberData(nameof(OddNetlists))]
    public void OddNetlistNeverThrows(string name, string netlist)
    {
        var result = Runner.Run(netlist, new RunOptions());

        Assert.NotNull(result);
        Assert.NotNull(result.Diagnostics);
        Assert.False(string.IsNullOrEmpty(name));
    }
}
