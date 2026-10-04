using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Core;
using Ssp.Core.Chains;
using Ssp.Core.Netlist;
using Ssp.Web.Components;
using Ssp.Web.Library;
using Ssp.Web.Pages;
using Ssp.Web.Sharing;

namespace Ssp.Web.Tests;

public class GalleryPageTests : BunitContext
{
    static readonly string Library = Path.Combine(RepoPaths.Root, "circuits", "library");

    [Fact]
    public void GalleryListsEachLibraryFile()
    {
        var page = Render<Gallery>();

        var names = page.FindAll("ul.gallery li .file").Select(e => e.TextContent.Trim()).Order(StringComparer.Ordinal);
        var files = Directory.GetFiles(Library).Select(Path.GetFileName).Order(StringComparer.Ordinal);
        Assert.NotEmpty(files);
        Assert.Equal(files, names);
    }

    [Fact]
    public void EachEntryOpensCircuitByShareUrl()
    {
        var page = Render<Gallery>();

        foreach (var entry in page.FindAll("ul.gallery li"))
        {
            var name = entry.QuerySelector(".file")!.TextContent.Trim();
            var href = entry.QuerySelector("a")!.GetAttribute("href")!;
            Assert.StartsWith("editor#", href);
            // NOTE: The page embeds the files at build time. A Windows checkout gives CRLF files.
            var netlist = File.ReadAllText(Path.Combine(Library, name)).Replace("\r\n", "\n");
            Assert.Equal(netlist, ShareCodec.Decode(href["editor#".Length..]).Replace("\r\n", "\n"));
        }
    }

    [Fact]
    public void EntryShowsCircuitTitle()
    {
        var page = Render<Gallery>();

        var entry = page.FindAll("ul.gallery li").Single(li => li.QuerySelector(".file")!.TextContent.Trim() == "gain-two-stage-tl072.cir");
        Assert.Equal("Two-stage gain, TL072", entry.QuerySelector("a")!.TextContent.Trim());
    }

    [Fact]
    public void EveryEntryLoadsAndRendersAPreview()
    {
        var page = Render<Gallery>();

        var entries = page.FindAll("ul.gallery li");
        Assert.Equal(Directory.GetFiles(Library).Length, entries.Count);
        Assert.All(entries, e =>
        {
            var src = e.QuerySelector("figure.preview img")!.GetAttribute("src")!;
            var file = Path.Combine(RepoPaths.Root, "src", "Ssp.Web", "wwwroot", src);
            Assert.StartsWith("<svg", File.ReadAllText(file).TrimStart());
            Assert.Null(e.QuerySelector("figure.preview svg"));
        });
    }

    [Fact]
    public void HomeOffersTheStartersWithPreviews()
    {
        Services.AddSingleton<Ssp.Web.Hosting.ISimulationHost, Ssp.Web.Hosting.InProcessSimulationHost>();

        var page = Render<Home>();

        Assert.Equal(Directory.GetFiles(Library).Length, page.FindAll("main.home ul.gallery li figure.preview img").Count);
        Assert.Single(page.FindAll("h1"));
    }

    const string Fuzz = "fuzz-transistor-diode.cir";

    static LibraryCircuit FuzzEntry() => Assert.Single(CircuitLibrary.All, c => c.FileName == Fuzz);

    [Fact]
    public void TransistorFuzzIsInTheLibrary()
    {
        Assert.Equal("Transistor fuzz", FuzzEntry().Title);
    }

    [Fact]
    public void TransistorFuzzRunsWithNoError()
    {
        var circuit = NetlistLoader.Load(FuzzEntry().Netlist);

        Assert.DoesNotContain(circuit.Diagnostics, d => d.Severity == Severity.Error);
    }

    [Fact]
    public void VirtualAmpAcceptsTransistorFuzzAsThePedal()
    {
        var stages = VirtualAmp.Preset
            .Select(p => new ChainStage(p.Key, File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "blocks", p.Value))))
            .Prepend(new ChainStage("pedal", FuzzEntry().Netlist))
            .ToArray();

        var netlist = Chain.Compose(stages);

        Assert.DoesNotContain(NetlistLoader.Load(netlist).Diagnostics, d => d.Severity == Severity.Error);
    }
}
