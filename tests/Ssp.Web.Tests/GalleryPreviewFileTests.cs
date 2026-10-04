using Ssp.Web.Components;
using Ssp.Web.Library;

namespace Ssp.Web.Tests;

/// <summary>
/// The gallery shows committed SVG files, so the browser runs no loader, placer or renderer for it.
/// Set UPDATE_GOLDEN=1 (or run scripts/make-previews.sh) to write them again.
/// </summary>
public class GalleryPreviewFileTests
{
    static readonly string Directory = Path.Combine(RepoPaths.Root, "src", "Ssp.Web", "wwwroot", "previews");

    public static TheoryData<string> Files() => new(CircuitLibrary.All.Select(c => c.FileName));

    [Theory]
    [MemberData(nameof(Files))]
    public void CommittedPreviewEqualsAFreshRender(string fileName)
    {
        var circuit = CircuitLibrary.All.Single(c => c.FileName == fileName);
        var svg = SchematicView.Svg(circuit.Netlist);
        Assert.False(string.IsNullOrWhiteSpace(svg));
        var fresh = svg.Replace("\r\n", "\n");
        var path = Path.Combine(Directory, Path.ChangeExtension(fileName, ".svg"));

        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(path, fresh);
            return;
        }

        Assert.True(File.Exists(path), $"{path} does not exist. Run scripts/make-previews.sh.");
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), fresh);
    }

    [Fact]
    public void NoPreviewFileIsLeftOverFromARemovedCircuit()
    {
        var expected = CircuitLibrary.All.Select(c => Path.ChangeExtension(c.FileName, ".svg")).Order(StringComparer.Ordinal);
        var actual = System.IO.Directory.Exists(Directory)
            ? System.IO.Directory.GetFiles(Directory, "*.svg").Select(Path.GetFileName).Order(StringComparer.Ordinal)
            : Enumerable.Empty<string?>();
        Assert.Equal(expected, actual);
    }
}
