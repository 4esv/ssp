namespace Ssp.Web.Tests;

/// <summary>
/// app.css keeps layout and interaction states (#182). The look comes from the vendored substrate (#416): its
/// faces, so app.css names no font, and its pressed band, a one-ink gradient.
/// </summary>
public class CssBareTests
{
    static readonly string AppCss = File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Ssp.Web", "wwwroot", "css", "app.css"));

    [Theory]
    [InlineData("box-shadow")]
    [InlineData("border-radius")]
    [InlineData("transition")]
    [InlineData("animation")]
    [InlineData("font-family")]
    public void AppCssHasNoDecoration(string property)
    {
        Assert.DoesNotContain(property, AppCss, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppCssIsUnder250Lines()
    {
        var lines = AppCss.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Length;
        Assert.True(lines < 250, $"app.css has {lines} lines.");
    }
}
