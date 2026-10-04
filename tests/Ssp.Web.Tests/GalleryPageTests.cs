using Bunit;
using Microsoft.Extensions.DependencyInjection;
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
}
