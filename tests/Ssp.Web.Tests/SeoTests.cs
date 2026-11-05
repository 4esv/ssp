using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Ssp.DocsGen;

namespace Ssp.Web.Tests;

/// <summary>
/// A crawler that runs no JavaScript reads the page title, the description, robots.txt, sitemap.xml and
/// llms.txt, and cites the site (#425). These files are committed in wwwroot, so the test reads them there.
/// </summary>
public sealed partial class SeoTests : IDisposable
{
    const string Site = "https://ssp.aesv.io";

    static readonly string Wwwroot = Path.Combine(RepoPaths.Root, "src", "Ssp.Web", "wwwroot");
    static readonly string Pages = Path.Combine(RepoPaths.Root, "src", "Ssp.Web", "Pages");
    static readonly string Index = File.ReadAllText(Path.Combine(Wwwroot, "index.html"));

    readonly string _docsOut = Path.Combine(Path.GetTempPath(), "ssp-seo-docs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_docsOut)) Directory.Delete(_docsOut, true);
    }

    IReadOnlyList<DocPage> Docs() => DocsGenerator.Generate(RepoPaths.Root, _docsOut);

    static string Title(string html) => TitleTag().Match(html).Groups[1].Value.Trim();

    static string? Meta(string html, string name) => NamedMeta().Matches(html)
        .Select(m => (Name: m.Groups[1].Value, Value: m.Groups[2].Value))
        .Where(m => m.Name == name).Select(m => m.Value).FirstOrDefault();

    static string? Property(string html, string name) => PropertyMeta().Matches(html)
        .Select(m => (Name: m.Groups[1].Value, Value: m.Groups[2].Value))
        .Where(m => m.Name == name).Select(m => m.Value).FirstOrDefault();

    static string AppPaint() => AppDiv().Match(Index).Groups[1].Value;

    static string TextOf(string html) => Whitespace().Replace(Tags().Replace(html, " "), " ").Trim();

    static IReadOnlyList<string> Words(string html) => Word().Matches(TextOf(html)).Select(m => m.Value).ToList();

    /// <summary>The pages the publish step writes: the app shell for each route, and the docs generator output.</summary>
    HashSet<string> PublishedPages()
    {
        var pages = new HashSet<string>(StringComparer.Ordinal) { "/" };
        foreach (var file in Directory.GetFiles(Pages, "*.razor"))
            foreach (Match m in PageDirective().Matches(File.ReadAllText(file)))
                pages.Add(m.Groups[1].Value.StartsWith('/') ? m.Groups[1].Value : "/" + m.Groups[1].Value);
        foreach (var page in Docs()) pages.Add("/docs/" + page.Output);
        return pages;
    }

    static string SitemapPath(string url)
    {
        Assert.StartsWith(Site + "/", url);
        return url[Site.Length..];
    }

    static IReadOnlyList<string> SitemapUrls()
    {
        var doc = XDocument.Load(Path.Combine(Wwwroot, "sitemap.xml"));
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        return doc.Root!.Elements(ns + "url").Select(u => u.Element(ns + "loc")!.Value.Trim()).ToList();
    }

    // The title block

    [Fact]
    public void The_title_says_what_the_app_is()
    {
        var title = Title(Index);

        Assert.InRange(title.Length, 30, 60);
        Assert.StartsWith("ssp", title);
        Assert.Contains("pedal", title);
        Assert.Contains("circuit", title);
    }

    [Fact]
    public void The_meta_description_is_the_length_a_search_engine_shows()
    {
        var description = Meta(Index, "description");

        Assert.NotNull(description);
        Assert.InRange(description.Length, 70, 160);
        Assert.Contains("pedal", description);
    }

    [Fact]
    public void The_page_names_its_canonical_url()
    {
        Assert.Equal(Site + "/", Canonical().Match(Index).Groups[1].Value);
        Assert.Single(Canonical().Matches(Index));
    }

    [Fact]
    public void The_open_graph_and_twitter_tags_hold_the_same_story()
    {
        var description = Meta(Index, "description");

        Assert.Equal(Title(Index), Property(Index, "og:title"));
        Assert.Equal(description, Property(Index, "og:description"));
        Assert.Equal(description, Meta(Index, "twitter:description"));
        Assert.Equal(Title(Index), Meta(Index, "twitter:title"));
        Assert.Equal(Site + "/", Property(Index, "og:url"));
        Assert.Equal("website", Property(Index, "og:type"));
        Assert.False(string.IsNullOrWhiteSpace(Meta(Index, "twitter:card")));

        foreach (var image in new[] { Property(Index, "og:image"), Meta(Index, "twitter:image") })
        {
            Assert.NotNull(image);
            Assert.StartsWith(Site + "/", image);
            var path = Path.Combine(Wwwroot, image[(Site.Length + 1)..]);
            Assert.True(File.Exists(path), $"{path} is not in wwwroot");
        }
    }

    [Fact]
    public void The_json_ld_names_a_free_software_application()
    {
        var json = JsonLd().Match(Index).Groups[1].Value;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("https://schema.org", root.GetProperty("@context").GetString());
        Assert.Equal("SoftwareApplication", root.GetProperty("@type").GetString());
        Assert.Equal("ssp", root.GetProperty("name").GetString());
        Assert.Equal(Site + "/", root.GetProperty("url").GetString());
        Assert.Equal(Meta(Index, "description"), root.GetProperty("description").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("applicationCategory").GetString()));
        Assert.True(root.GetProperty("isAccessibleForFree").GetBoolean());
        var offers = root.GetProperty("offers");
        Assert.Equal("0", offers.GetProperty("price").GetString());
        Assert.Equal("USD", offers.GetProperty("priceCurrency").GetString());
    }

    // The first paint, before Blazor starts

    [Fact]
    public void The_static_first_paint_is_the_landing_page()
    {
        var paint = AppPaint();

        Assert.DoesNotContain("Loading", paint);
        Assert.Matches("<h1>[^<]+</h1>", paint);
        Assert.True(Words(paint).Count >= 50, $"{Words(paint).Count} words in #app, expected 50 or more");
        Assert.Contains("href=\"editor\"", paint);
        Assert.Contains("href=\"gallery\"", paint);
    }

    [Fact]
    public void The_static_first_paint_matches_the_home_page_so_nothing_flashes()
    {
        var home = File.ReadAllText(Path.Combine(Pages, "Home.razor"));

        Assert.Equal(Title(Index), PageTitleTag().Match(home).Groups[1].Value.Trim());
        Assert.Equal(Words(MainBlock().Match(home).Groups[1].Value), Words(AppPaint()));
    }

    // The crawler files

    [Fact]
    public void Robots_txt_allows_the_site_and_names_the_sitemap()
    {
        var robots = File.ReadAllText(Path.Combine(Wwwroot, "robots.txt"));

        Assert.Contains("User-agent: *", robots);
        Assert.Contains("Allow: /", robots);
        Assert.Contains($"Sitemap: {Site}/sitemap.xml", robots);
    }

    [Fact]
    public void Every_sitemap_url_is_a_page_the_build_publishes()
    {
        var published = PublishedPages();
        var urls = SitemapUrls();

        Assert.NotEmpty(urls);
        Assert.Equal(urls.Count, urls.Distinct(StringComparer.Ordinal).Count());
        foreach (var url in urls)
            Assert.True(published.Contains(SitemapPath(url)), $"{url} is not a published page");
    }

    [Fact]
    public void The_sitemap_lists_the_home_page_and_every_docs_page()
    {
        var listed = SitemapUrls().Select(SitemapPath).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("/", listed);
        foreach (var page in Docs()) Assert.Contains("/docs/" + page.Output, listed);
        Assert.DoesNotContain("/not-found", listed);
    }

    [Fact]
    public void Llms_txt_follows_the_format()
    {
        var lines = File.ReadAllLines(Path.Combine(Wwwroot, "llms.txt"));

        Assert.Equal("# ssp", lines[0].Trim());
        var summary = lines.Skip(1).First(l => l.Trim().Length > 0);
        Assert.StartsWith("> ", summary);
        Assert.InRange(summary.Length, 70, 400);
        Assert.Contains(lines, l => l.StartsWith("## ", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("- [", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_llms_txt_link_is_a_published_page()
    {
        var published = PublishedPages();
        var links = MarkdownLink().Matches(File.ReadAllText(Path.Combine(Wwwroot, "llms.txt")))
            .Select(m => m.Groups[2].Value).ToList();

        Assert.NotEmpty(links);
        foreach (var link in links)
            Assert.True(published.Contains(SitemapPath(link)), $"{link} is not a published page");
    }

    // The other pages

    [Fact]
    public void Every_route_has_a_title_of_its_own()
    {
        var titles = Directory.GetFiles(Pages, "*.razor")
            .Select(file => (File: file, Title: PageTitleTag().Match(File.ReadAllText(file)).Groups[1].Value.Trim()))
            .ToList();

        Assert.All(titles, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Title), $"{t.File} has no <PageTitle>");
            Assert.InRange(t.Title.Length, 15, 60);
        });
        Assert.Equal(titles.Count, titles.Select(t => t.Title).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Each_docs_page_has_a_title_and_a_meta_description()
    {
        Docs();

        foreach (var page in Docs())
        {
            var html = File.ReadAllText(Path.Combine(_docsOut, page.Output));
            Assert.Equal(page.Title, Title(html));
            var description = Meta(html, "description");
            Assert.NotNull(description);
            Assert.InRange(description.Length, 70, 160);
            Assert.DoesNotMatch("\\.\\p{Lu}", description); // No two sentences run together.
            Assert.DoesNotContain(" .", description); // A link with no text leaves a gap.
        }
    }

    [GeneratedRegex("<title>([^<]*)</title>")]
    private static partial Regex TitleTag();

    [GeneratedRegex("<meta name=\"([^\"]+)\" content=\"([^\"]*)\"\\s*/?>")]
    private static partial Regex NamedMeta();

    [GeneratedRegex("<meta property=\"([^\"]+)\" content=\"([^\"]*)\"\\s*/?>")]
    private static partial Regex PropertyMeta();

    [GeneratedRegex("<link rel=\"canonical\" href=\"([^\"]*)\"")]
    private static partial Regex Canonical();

    [GeneratedRegex("<div id=\"app\">(.*?)</div>", RegexOptions.Singleline)]
    private static partial Regex AppDiv();

    [GeneratedRegex("<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex JsonLd();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex("\\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex("[A-Za-z0-9][A-Za-z0-9'-]*")]
    private static partial Regex Word();

    [GeneratedRegex("@page \"([^\"]+)\"")]
    private static partial Regex PageDirective();

    [GeneratedRegex("<PageTitle>([^<]*)</PageTitle>")]
    private static partial Regex PageTitleTag();

    [GeneratedRegex("<main class=\"home\">(.*?)</main>", RegexOptions.Singleline)]
    private static partial Regex MainBlock();

    [GeneratedRegex("\\[([^\\]]+)\\]\\(([^)]+)\\)")]
    private static partial Regex MarkdownLink();
}
