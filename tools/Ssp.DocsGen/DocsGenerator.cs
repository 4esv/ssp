using System.Net;
using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Ssp.DocsGen;

/// <summary>One generated page. Source is the repo-relative Markdown path. Output is relative to the output directory.</summary>
public sealed record DocPage(string Source, string Output, string Title);

public static class DocsGenerator
{
    const string BlobBase = "https://github.com/4esv/ssp/blob/main/";
    const string TreeBase = "https://github.com/4esv/ssp/tree/main/";

    // Repo path of the Markdown source, and the page it becomes.
    static readonly (string Source, string Output)[] Fixed =
    [
        ("README.md", "index.html"),
        ("docs/README.md", "contents.html"),
        ("models/README.md", "models.html"),
        ("CHANGELOG.md", "CHANGELOG.html"),
    ];

    const string Css = """
        body { font-family: system-ui, sans-serif; line-height: 1.5; max-width: 50rem; margin: 0 auto; padding: 1rem; }
        nav { font-size: 0.9rem; margin-bottom: 1.5rem; padding-bottom: 0.5rem; border-bottom: 1px solid #ccc; }
        nav a { margin-right: 0.75rem; white-space: nowrap; display: inline-block; }
        pre { overflow-x: auto; padding: 0.5rem; background: #f4f4f4; }
        code { font-family: ui-monospace, monospace; font-size: 0.9em; }
        table { border-collapse: collapse; }
        th, td { border: 1px solid #ccc; padding: 0.25rem 0.5rem; text-align: left; }
        img { max-width: 100%; }
        """;

    public static IReadOnlyList<DocPage> Generate(string repoRoot, string outDir)
    {
        repoRoot = Path.GetFullPath(repoRoot);
        var sources = new List<(string Source, string Output)>(Fixed);
        sources.AddRange(Directory.GetFiles(Path.Combine(repoRoot, "docs"), "*.md")
            .Select(f => Path.GetFileName(f)).Where(n => n != "README.md").Order(StringComparer.Ordinal)
            .Select(n => ($"docs/{n}", Path.ChangeExtension(n, ".html"))));
        sources.AddRange(Directory.GetFiles(Path.Combine(repoRoot, "docs", "decisions"), "*.md")
            .Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal)
            .Select(n => ($"docs/decisions/{n}", "decisions/" + Path.ChangeExtension(n, ".html"))));

        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        var outputs = sources.ToDictionary(s => s.Source, s => s.Output, StringComparer.Ordinal);
        var parsed = sources.Select(s =>
        {
            var doc = Markdown.Parse(File.ReadAllText(Path.Combine(repoRoot, s.Source)), pipeline);
            return (s.Source, s.Output, Doc: doc, Title: TitleOf(doc, s.Source));
        }).ToList();
        var pages = parsed.Select(p => new DocPage(p.Source, p.Output, p.Title)).ToList();

        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "docs.css"), Css);
        foreach (var p in parsed)
        {
            RewriteLinks(p.Doc, p.Source, p.Output, outputs, repoRoot);
            var body = p.Doc.ToHtml(pipeline);
            var path = Path.Combine(outDir, p.Output);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Page(p.Title, p.Output, pages, body));
        }
        return pages;
    }

    static string TitleOf(MarkdownDocument doc, string source)
    {
        var h1 = doc.Descendants<HeadingBlock>().FirstOrDefault(h => h.Level == 1);
        if (h1?.Inline is null) return Path.GetFileNameWithoutExtension(source);
        var sb = new StringBuilder();
        foreach (var i in h1.Inline.Descendants())
        {
            if (i is LiteralInline l) sb.Append(l.Content.ToString());
            else if (i is CodeInline c) sb.Append(c.Content);
        }
        return sb.Length > 0 ? sb.ToString() : Path.GetFileNameWithoutExtension(source);
    }

    static string Rel(string fromOutput, string toOutput)
    {
        var fromDir = Path.GetDirectoryName(fromOutput);
        if (string.IsNullOrEmpty(fromDir)) fromDir = ".";
        return Path.GetRelativePath(fromDir, toOutput).Replace('\\', '/');
    }

    static void RewriteLinks(MarkdownDocument doc, string source, string output,
        Dictionary<string, string> outputs, string repoRoot)
    {
        var sourceDir = Path.GetDirectoryName(source)?.Replace('\\', '/') ?? "";
        foreach (var link in doc.Descendants<LinkInline>())
        {
            var url = link.Url;
            if (string.IsNullOrEmpty(url) || url.StartsWith('#') || url.Contains("://", StringComparison.Ordinal)
                || url.StartsWith("mailto:", StringComparison.Ordinal))
                continue;
            var hash = url.IndexOf('#', StringComparison.Ordinal);
            var file = WebUtility.UrlDecode(hash < 0 ? url : url[..hash]);
            var frag = hash < 0 ? "" : url[hash..];
            var joined = sourceDir == "" ? file : sourceDir + "/" + file;
            var target = Normalize(joined)
                ?? throw new InvalidDataException($"{source}: link leaves the repo: {url}");

            if (outputs.TryGetValue(target, out var page))
                link.Url = Rel(output, page) + frag;
            else if (File.Exists(Path.Combine(repoRoot, target)))
                link.Url = BlobBase + target + frag;
            else if (Directory.Exists(Path.Combine(repoRoot, target)))
                link.Url = TreeBase + target;
            else
                throw new InvalidDataException($"{source}: link to a missing file: {url}");
        }
    }

    static string? Normalize(string path)
    {
        var parts = new List<string>();
        foreach (var seg in path.Split('/'))
        {
            if (seg is "" or ".") continue;
            if (seg == "..")
            {
                if (parts.Count == 0) return null;
                parts.RemoveAt(parts.Count - 1);
            }
            else parts.Add(seg);
        }
        return string.Join('/', parts);
    }

    static string Page(string title, string output, IReadOnlyList<DocPage> pages, string body)
    {
        var up = string.Concat(Enumerable.Repeat("../", output.Count(c => c == '/')));
        var nav = new StringBuilder($"<a href=\"{up}../\">Home</a>");
        foreach (var p in pages)
            nav.Append($"<a href=\"{Rel(output, p.Output)}\">{WebUtility.HtmlEncode(p.Title)}</a>");
        return $"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{WebUtility.HtmlEncode(title)}</title>
            <link rel="stylesheet" href="{up}docs.css">
            </head>
            <body>
            <nav>{nav}</nav>
            <main>
            {body}</main>
            </body>
            </html>
            """;
    }
}
