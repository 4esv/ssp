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

    // NOTE: #365: when a relative .include is missing, the message says the path is relative to the netlist file.
    [Fact]
    public void MissingRelativeIncludeSaysThePathIsRelativeToTheNetlist()
    {
        var dir = Temp();
        try
        {
            var loaded = NetlistLoader.Load("* t\nV1 a 0 1\n.include lib/amp.lib\nR2 b 0 1k\n.END\n", dir);

            var error = Assert.Single(loaded.Diagnostics);
            Assert.Equal(Severity.Error, error.Severity);
            Assert.Contains("A relative path is relative to the directory of the netlist file.", error.Message);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // NOTE: #365: when an absolute .include is missing, the message does not say the path is relative.
    [Fact]
    public void MissingAbsoluteIncludeDoesNotSayThePathIsRelative()
    {
        var dir = Temp();
        try
        {
            var missing = Path.Combine(dir, "amp.lib");
            var loaded = NetlistLoader.Load($"* t\nV1 a 0 1\n.include {missing}\nR2 b 0 1k\n.END\n", dir);

            var error = Assert.Single(loaded.Diagnostics);
            Assert.Equal(Severity.Error, error.Severity);
            Assert.DoesNotContain("A relative path is relative to the directory of the netlist file.", error.Message);
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
