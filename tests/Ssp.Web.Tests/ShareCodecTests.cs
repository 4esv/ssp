using System.Text;
using Ssp.Web.Sharing;

namespace Ssp.Web.Tests;

public class ShareCodecTests
{
    static string Fixture(string name) => File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", name));

    [Fact]
    public void RoundTripGivesEqualText()
    {
        var netlist = Fixture("divider-basic.cir");

        Assert.Equal(netlist, ShareCodec.Decode(ShareCodec.Encode(netlist)));
    }

    [Fact]
    public void RoundTripKeepsNonAsciiText()
    {
        const string netlist = "* 10 µF ±5 %\nC1 in out 10u\n";

        Assert.Equal(netlist, ShareCodec.Decode(ShareCodec.Encode(netlist)));
    }

    [Fact]
    public void HashIsUrlSafe()
    {
        var hash = ShareCodec.Encode(Fixture("opamp-buffer.cir"));

        Assert.Matches("^[A-Za-z0-9_-]+$", hash);
    }

    [Fact]
    public void TwoKilobyteNetlistFitsInFourKilobyteHash()
    {
        // NOTE: Real netlist text, joined from all .cir fixtures and cut to 2 kB.
        var text = new StringBuilder();
        foreach (var path in Directory.GetFiles(Path.Combine(RepoPaths.Root, "circuits", "fixtures"), "*.cir").Order(StringComparer.Ordinal))
        {
            text.Append(File.ReadAllText(path));
        }
        Assert.True(text.Length >= 2048, $"Fixtures give only {text.Length} chars.");
        var netlist = text.ToString(0, 2048);

        var hash = ShareCodec.Encode(netlist);

        Assert.True(hash.Length <= 4096, $"Hash is {hash.Length} chars.");
        Assert.Equal(netlist, ShareCodec.Decode(hash));
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("AAAA")]
    [InlineData("x")]
    public void BadHashThrowsFormatException(string hash)
    {
        Assert.Throws<FormatException>(() => ShareCodec.Decode(hash));
    }

    [Fact]
    public void CutHashThrowsFormatException()
    {
        var hash = ShareCodec.Encode(Fixture("opamp-buffer.cir"));

        // NOTE: A link can be cut at any length when a user copies it.
        for (var length = 0; length < hash.Length; length++)
        {
            Assert.Throws<FormatException>(() => ShareCodec.Decode(hash[..length]));
        }
    }
}
