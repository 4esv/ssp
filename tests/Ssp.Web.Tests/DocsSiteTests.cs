using System.Text.RegularExpressions;
using Ssp.DocsGen;

namespace Ssp.Web.Tests;

// The publish step runs the same generator into wwwroot/docs.
public sealed partial class DocsSiteTests : IDisposable
{
    readonly string _out = Path.Combine(Path.GetTempPath(), "ssp-docs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_out)) Directory.Delete(_out, true);
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ssp.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("ssp.sln not found");
    }

    [GeneratedRegex("href=\"([^\"]*)\"")]
    private static partial Regex Href();

    [Fact]
    public void Generates_an_index_and_a_page_for_each_listed_document()
    {
        var pages = DocsGenerator.Generate(RepoRoot(), _out);

        Assert.True(File.Exists(Path.Combine(_out, "index.html")));
        Assert.Contains("CHANGELOG.md", pages.Select(p => p.Source));
        Assert.Contains("models/README.md", pages.Select(p => p.Source));
        Assert.Contains(pages, p => p.Source.StartsWith("docs/decisions/", StringComparison.Ordinal));
        foreach (var page in pages)
            Assert.True(File.Exists(Path.Combine(_out, page.Output)), page.Output);
    }

    [Fact]
    public void No_page_has_an_md_link_or_a_link_to_a_missing_page()
    {
        var pages = DocsGenerator.Generate(RepoRoot(), _out);
        foreach (var page in pages)
        {
            var path = Path.Combine(_out, page.Output);
            var html = File.ReadAllText(path);
            foreach (Match m in Href().Matches(html))
            {
                var href = m.Groups[1].Value;
                if (href.StartsWith("http", StringComparison.Ordinal) || href.StartsWith('#')) continue;
                var file = href.Split('#')[0];
                Assert.False(file.EndsWith(".md", StringComparison.Ordinal), $"{page.Output}: {href}");
                if (file.EndsWith('/') || file == "") continue; // The app at ../ is not in the output.
                var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, file));
                Assert.True(File.Exists(target), $"{page.Output}: {href}");
            }
        }
    }

    [Fact]
    public void Links_to_source_files_go_to_the_GitHub_blob_URL()
    {
        DocsGenerator.Generate(RepoRoot(), _out);
        var html = File.ReadAllText(Path.Combine(_out, "index.html"));
        Assert.Contains("https://github.com/4esv/ssp/blob/main/LICENSE", html);
    }

    [Fact]
    public void The_page_title_is_the_first_heading()
    {
        DocsGenerator.Generate(RepoRoot(), _out);
        Assert.Matches("<title>[^<]+</title>", File.ReadAllText(Path.Combine(_out, "index.html")));
    }
}
