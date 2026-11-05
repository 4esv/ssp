using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ssp.Web.Tests;

/// <summary>
/// The V3 look is vendored, not hand-copied (#416). scripts/vendor-substrate.mjs runs the aesv.io export and
/// takes the files the app uses, with the export's hashes in substrate/manifest.json. The shell links the
/// substrate before the app's own sheet, so the app has the last word.
/// </summary>
public sealed partial class VendoredSubstrateTests
{
    static readonly string Wwwroot = Path.Combine(RepoPaths.Root, "src", "Ssp.Web", "wwwroot");
    static readonly string Folder = Path.Combine(Wwwroot, "substrate");
    static readonly string Index = File.ReadAllText(Path.Combine(Wwwroot, "index.html"));

    [Fact]
    public void TheShellLinksTheSubstrateBeforeTheAppSheet()
    {
        var substrate = Link("substrate/css/substrate.css");
        var app = Link("css/app.css");

        Assert.True(substrate < app, $"index.html links the substrate at {substrate} and the app sheet at {app}.");
    }

    static int Link(string href)
    {
        var at = Index.IndexOf($"href=\"{href}\"", StringComparison.Ordinal);
        Assert.True(at >= 0, $"index.html does not link {href}.");
        return at;
    }

    [Fact]
    public void TheAppStaysLightWhereTheReaderAsksForDark()
    {
        // NOTE: The substrate turns dark under prefers-color-scheme, and ssp is light only (#318).
        Assert.Contains("<html lang=\"en\" data-theme=\"light\">", Index);
    }

    [Fact]
    public void EveryVendoredFileMatchesTheManifest()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder, "manifest.json")));
        var listed = manifest.RootElement.GetProperty("files").EnumerateArray()
            .ToDictionary(f => f.GetProperty("path").GetString()!, f => f, StringComparer.Ordinal);

        var onDisk = Directory.GetFiles(Folder, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Folder, f).Replace('\\', '/'))
            .Where(p => p != "manifest.json")
            .ToList();

        var wrong = onDisk.Where(p => !listed.ContainsKey(p)).Select(p => $"{p} is not in the manifest").ToList();
        foreach (var (path, entry) in listed)
        {
            var file = Path.Combine(Folder, path);
            if (!File.Exists(file)) { wrong.Add($"{path} is in the manifest and not in the folder"); continue; }
            var sha = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
            if (sha != entry.GetProperty("sha256").GetString()) wrong.Add($"{path}: sha256 {sha} does not match the manifest");
        }

        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }

    /// <summary>
    /// The Pixelta Cells files are not vendored (their licence for a public repository is not decided). The
    /// vendored sheet still names them, so every face it names that is not vendored must be Pixelta, and the
    /// app sheet must set titles in Handjet Cells so no page asks for it.
    /// </summary>
    [Fact]
    public void OnlyThePixeltaFaceIsMissingAndTheAppDoesNotUseIt()
    {
        var css = File.ReadAllText(Path.Combine(Folder, "css", "substrate.css"));
        var missing = Url().Matches(css).Select(m => m.Groups[1].Value)
            .Where(u => !File.Exists(Path.GetFullPath(Path.Combine(Folder, "css", u))))
            .ToList();

        Assert.NotEmpty(Url().Matches(css));
        Assert.All(missing, u => Assert.StartsWith("../fonts/pixelta-cells.", u, StringComparison.Ordinal));
        Assert.DoesNotContain("Pixelta", File.ReadAllText(Path.Combine(Wwwroot, "css", "app.css")), StringComparison.Ordinal);
    }

    [GeneratedRegex(@"url\(([^)]+)\)")]
    private static partial Regex Url();
}
