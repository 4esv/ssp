using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using PartsApi = Ssp.Core.Parts.Parts;

namespace Ssp.Core.Tests;

public class PartMapTests
{
    static readonly PartsTable Table = PartsApi.Load(Path.Combine(RepoPaths.Root, "models", "parts.toml"));

    static PartMap Resolve(string netlist) => PartMap.Resolve(NetlistLoader.Load(netlist), Table);

    [Fact]
    public void MappedAndUnmappedReferencesAreInTheResult()
    {
        var map = Resolve("* ssp:part D1 1N4148\nV1 a 0 5\nD1 a b DGEN\nR1 b 0 1k\n.MODEL DGEN D\n.END\n");

        Assert.Equal("1N4148", map.Parts["D1"]?.Id);
        Assert.True(map.Parts.ContainsKey("R1"));
        Assert.Null(map.Parts["R1"]);
        var d = Assert.Single(map.Diagnostics, x => x.Message.Contains("R1"));
        Assert.Equal(Severity.Warning, d.Severity);
    }

    [Fact]
    public void DirectiveWinsOverDefaultMapping()
    {
        var map = Resolve("* ssp:part D1 1N4148\nV1 a 0 5\nD1 a b LED_RED\nR1 b 0 1k\n.MODEL LED_RED D\n.END\n");

        Assert.Equal("1N4148", map.Parts["D1"]?.Id);
    }

    [Fact]
    public void DefaultMappingUsesModelName()
    {
        var map = Resolve("V1 a 0 5\nD1 a b LED_RED\nR1 b 0 1k\n.MODEL LED_RED D\n.END\n");

        Assert.Equal("LED-RED", map.Parts["D1"]?.Id);
    }

    [Fact]
    public void UnknownPartIdGivesDiagnosticThatNamesTheId()
    {
        var map = Resolve("* ssp:part R1 NOPE-123\nV1 a 0 5\nR1 a 0 1k\n.END\n");

        Assert.Contains(map.Diagnostics, x => x.Message.Contains("NOPE-123"));
    }
}
