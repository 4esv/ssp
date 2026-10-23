using Ssp.Core.Netlist;

namespace Ssp.Core.Tests;

public class NetlistIncludeTests
{
    const string Sub = """
        .subckt DIV a b
        R1 a b 1k
        .ends DIV
        """;

    static string Temp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ssp-include-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // NOTE: #336: a relative .include resolves against the directory of the netlist file, not against the working directory.
    [Fact]
    public void RelativeIncludeResolvesAgainstTheBaseDirectory()
    {
        var dir = Temp();
        try
        {
            File.WriteAllText(Path.Combine(dir, "div.cir"), Sub);
            var loaded = NetlistLoader.Load("* t\n.include div.cir\nV1 a 0 1\nX1 a b DIV\nR2 b 0 1k\n.END\n", dir);

            Assert.Empty(loaded.Diagnostics);
            Assert.Contains("DIV", loaded.SubcircuitPins.Keys, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // NOTE: #336: an unquoted absolute path is a valid .include token.
    [Fact]
    public void UnquotedAbsoluteIncludeLoads()
    {
        var dir = Temp();
        try
        {
            var file = Path.Combine(dir, "div.cir");
            File.WriteAllText(file, Sub);
            var loaded = NetlistLoader.Load($"* t\n.include {file}\nV1 a 0 1\nX1 a b DIV\nR2 b 0 1k\n.END\n");

            Assert.Empty(loaded.Diagnostics);
            Assert.Contains("DIV", loaded.SubcircuitPins.Keys, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
