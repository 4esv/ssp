using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ssp.Web.Tests;

/// <summary>
/// wwwroot/substrate is vendored: it is the substrate export as published, and nobody edits it here. So CssBareTests
/// reads only app.css, and this test checks the vendored files against the export's manifest. The three Pixelta Cells
/// font files are left out on purpose: the licence of that font for a public repository is not decided. Its
/// font-face stays, and a face that does not load leaves its text as text.
/// </summary>
public partial class VendoredSubstrateTests
{
    static readonly string WwwRoot = Path.Combine(RepoPaths.Root, "src", "Ssp.Web", "wwwroot");
    static readonly string Substrate = Path.Combine(WwwRoot, "substrate");
    static readonly string[] LeftOut = ["fonts/pixelta-cells.ttf", "fonts/pixelta-cells.woff", "fonts/pixelta-cells.woff2"];

    [Fact]
    public void EveryVendoredFileIsTheExportUnchanged()
    {
        var manifest = Path.Combine(Substrate, "manifest.json");
        Assert.True(File.Exists(manifest), "wwwroot/substrate/manifest.json is missing.");
        using var json = JsonDocument.Parse(File.ReadAllText(manifest));
        Assert.False(json.RootElement.GetProperty("dirty").GetBoolean(), "The export came from a dirty tree.");
        var failures = new List<string>();
        var listed = new HashSet<string>(StringComparer.Ordinal) { "manifest.json" };
        foreach (var file in json.RootElement.GetProperty("files").EnumerateArray())
        {
            var path = file.GetProperty("path").GetString()!;
            listed.Add(path);
            var full = Path.Combine(Substrate, path);
            if (LeftOut.Contains(path))
            {
                if (File.Exists(full)) failures.Add($"{path} is vendored, but it must stay out.");
                continue;
            }

            if (!File.Exists(full)) { failures.Add($"{path} is missing."); continue; }
            var sha = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(full)));
            if (sha != file.GetProperty("sha256").GetString()) failures.Add($"{path} is changed.");
        }

        foreach (var full in Directory.EnumerateFiles(Substrate, "*", SearchOption.AllDirectories))
        {
            var path = Path.GetRelativePath(Substrate, full).Replace('\\', '/');
            if (!listed.Contains(path)) failures.Add($"{path} is not in the manifest.");
        }

        Assert.True(failures.Count == 0, string.Join(" ", failures));
    }

    [Fact]
    public void TheShellLinksTheSubstrateBeforeAppCss()
    {
        var html = File.ReadAllText(Path.Combine(WwwRoot, "index.html"));
        var substrate = html.IndexOf("href=\"substrate/css/index.css\"", StringComparison.Ordinal);
        var app = html.IndexOf("href=\"css/app.css\"", StringComparison.Ordinal);
        Assert.True(substrate >= 0, "index.html does not link substrate/css/index.css.");
        Assert.True(substrate < app, "substrate/css/index.css must come before css/app.css.");
        Assert.Contains("<html lang=\"en\" data-theme=\"light\">", html, StringComparison.Ordinal);
        Assert.Contains("<body class=\"substrate\">", html, StringComparison.Ordinal);
        Assert.Contains("js/substrate#[.{fingerprint}].js", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AppCssHasNoColourBeyondTheTokens()
    {
        var css = Comment().Replace(File.ReadAllText(Path.Combine(WwwRoot, "css", "app.css")), " ");
        var found = Literal().Matches(css).Select(m => m.Value).Distinct().ToArray();
        Assert.True(found.Length == 0, "app.css names colours outside the tokens: " + string.Join(", ", found));
    }

    // NOTE: A colour word counts only as a whole value, so white-space and the issue numbers in comments do not.
    [GeneratedRegex(@"#[0-9a-fA-F]{3,8}\b|\b(?:rgba?|hsla?|oklab|oklch|lab|lch|hwb)\(|(?<![-\w])(?:Canvas|CanvasText|white|black|red|blue|green)(?![-\w])")]
    private static partial Regex Literal();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comment();
}
