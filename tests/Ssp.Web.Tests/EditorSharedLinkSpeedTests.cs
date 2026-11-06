using System.Diagnostics;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Web.Components;
using Ssp.Web.Hosting;
using Ssp.Web.Library;
using Ssp.Web.Pages;
using Ssp.Web.Sharing;
using LayoutDoc = Ssp.Core.Layout.Layout;

namespace Ssp.Web.Tests;

// NOTE: #449: a link to a library pedal after a value edit opened in 22 s to 145 s in the browser, because the editor placed
// the parts from nothing (AutoPlacer.Place: 25 s for overdrive-808 and 14 s for fuzz-muff on a laptop, before the browser
// slows it down). A netlist with the parts and nodes of a library circuit now opens with its hand layout.
public class EditorSharedLinkSpeedTests : BunitContext
{
    static readonly TimeSpan Limit = TimeSpan.FromSeconds(3);

    // NOTE: A link made before #449 by the share codec: clean-boost with R1 changed from 430k to 47k.
    const string OldLink = "eNptk8Fy2jAQhu9-iv-YZIxrE2iADofgkGk6GSYhHV87si1ARUgeS05Kn767smkvvdjalXb17-6nGzjXLLzyWiLXUhiU1jof3cDZrq3kAv4gYY0c-VYYp5y3bX8Edhf2nl9Wowz-3Eh8KH-A8g5NV2rlDrLGu9CddAvkyiBNsi5GqYTD5DY9QpiaFscY2xxZeozpUmC7xu087VMZi_LcCOdi5LbzQwJBhym6sLo7STTW94cFtN3jSnS1stfwopFtDGW8vQRoK-oYu9aeMEeR0G3fubRW7ZURGp0juQK12u1kK43H5mXzhSpUjsvnbRfqHW-m6WzW5znZWmr3qfzpXaJVyTnf1KnTwlOuoGpQKTyyBUrhJBVxN0MR4ymn5TTF6T5GZbWWFbf2NpnPeXcvqGHjaTJFvQrROH79nVw6dKAW_m0OKtGISlF0TEMLGkM0CXfSk0bn9ZnOcpu55RTPP0ONI-fhcd3P2HmxD98zZQwk8CmaZriUaGCrb8FJ6Rote5TZMwcCjlaE0D9I4nBBpVXT0BYrZ9CUaWiOygwmDZVt-g2ORrQer9nQ5KjIwNzggXqFe6IkyoOn5ImaaJvhvarImtwdo-2YFikDFVGh7K-YqojKpaYzVBEl5tMSr0P-fEyOjyFZkf3IyGDQphxH9jhYaW8_DwbDFBV5f0fQNh_UH40tQXGBRBouuQdaAij_ZYaIVJ5xIVCtcfKkhpjRA65oOHR3GiZRrNaU83OaoWAgCJ8lUcH05N9yTBr-r0lcc31Bm7K3oj3DSE-vkYYh2lYNFPeClNHKDG_8Q5YQTYO2M44f0xBcqbbqlO-189v28pcPgipheMT8rC5FJVHSJx4azG8IV09vy5kcZROsHpdcDeldThqWu2S5UbLePER_AAxNYTQ";

    public EditorSharedLinkSpeedTests()
    {
        Services.AddSingleton<TimeProvider>(new ManualTimeProvider());
        Services.AddSingleton<ISimulationHost, InProcessSimulationHost>();
    }

    public static TheoryData<string> Pedals() => ["clean-boost", "fuzz-face-si", "distortion-rat", "overdrive-808", "fuzz-muff"];

    static LibraryCircuit Pedal(string pedal) => CircuitLibrary.All.Single(c => c.FileName == pedal + ".cir");

    // The edit of the pedal-flow test: the first resistor value changes to 47k (or to 33k when it is 47k).
    static string Edited(string netlist) =>
        new System.Text.RegularExpressions.Regex(@"^(R(?!V|id\b|p\b|out\b)\w*\s+\S+\s+\S+\s+)(\S+)", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Replace(netlist, m => m.Groups[1].Value + (m.Groups[2].Value == "47k" ? "33k" : "47k"), 1);

    IRenderedComponent<SchematicEditor> Open(string hash)
    {
        Services.GetRequiredService<BunitNavigationManager>().NavigateTo("editor#" + hash);
        return Render<Editor>().FindComponent<SchematicEditor>();
    }

    [Theory]
    [MemberData(nameof(Pedals))]
    public void ALinkOfAnEditedPedalOpensWithTheHandLayout(string pedal)
    {
        var library = Pedal(pedal);
        var edited = Edited(library.Netlist);
        Assert.NotEqual(library.Netlist, edited);

        var clock = Stopwatch.StartNew();
        var schematic = Open(ShareCodec.Encode(edited));
        clock.Stop();

        Assert.Equal(edited, schematic.Instance.Netlist);
        Assert.Equal(LayoutDoc.Parse(library.Layout!).Parts, schematic.Instance.Drawn.Parts);
        Assert.True(clock.Elapsed < Limit, $"{pedal}: the edited link opened in {clock.Elapsed.TotalSeconds:0.0} s.");
    }

    [Fact]
    public void ALinkMadeBeforeTheFixStillOpens()
    {
        var expected = Pedal("clean-boost").Netlist.Replace("R1 vcc b 430k", "R1 vcc b 47k");
        var schematic = Open(OldLink);
        Assert.Equal(expected, schematic.Instance.Netlist);
        Assert.Equal(LayoutDoc.Parse(Pedal("clean-boost").Layout!).Parts, schematic.Instance.Drawn.Parts);
    }

    [Fact]
    public void AnEditThatAddsAPartFindsNoLibraryLayout()
    {
        var netlist = Pedal("clean-boost").Netlist.Replace(".END", "R99 out 0 1k\n.END");
        Assert.Null(CircuitLibrary.LayoutOf(netlist));
    }

    [Fact]
    public void AnEditThatMovesAPinFindsNoLibraryLayout()
    {
        var netlist = Pedal("clean-boost").Netlist.Replace("R2 b 0 43k", "R2 b e 43k");
        Assert.Null(CircuitLibrary.LayoutOf(netlist));
    }

    [Theory]
    [MemberData(nameof(Pedals))]
    public void AValueEditFindsTheLibraryLayout(string pedal) =>
        Assert.Equal(Pedal(pedal).Layout, CircuitLibrary.LayoutOf(Edited(Pedal(pedal).Netlist)));
}
