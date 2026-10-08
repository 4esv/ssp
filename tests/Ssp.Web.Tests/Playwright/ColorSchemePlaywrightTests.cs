using System.Globalization;
using Microsoft.Playwright;

namespace Ssp.Web.Tests.Playwright;

/// <summary>
/// The app is light only (#318). A device that prefers dark must not darken the page: the previews are
/// committed SVGs of black strokes on a transparent background, and an SVG loaded through an img is its
/// own document, so currentColor cannot inherit from the page and stays black. A dark page hides every
/// circuit.
/// </summary>
[Collection(PlaywrightCollection.Name)]
public class ColorSchemePlaywrightTests
{
    [PlaywrightFact]
    public async Task ADarkPreferenceLeavesThePageLightAndItsPreviewsReadable()
    {
        var baseUrl = Environment.GetEnvironmentVariable(PlaywrightFactAttribute.BaseUrlVariable)!;
        Assert.Equal(0, Program.Main(new[] { "install", "chromium" }));

        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync(new() { ColorScheme = ColorScheme.Dark });
        await page.GotoAsync(baseUrl);

        // NOTE: Blazor WebAssembly boots after the first paint, so wait for a preview before reading styles.
        await Assertions.Expect(page.Locator("figure.preview img").First).ToBeVisibleAsync(new() { Timeout = 60_000 });

        var background = await page.EvaluateAsync<string>("getComputedStyle(document.body).backgroundColor");
        Assert.True(
            Luminance(background) > 0.5,
            $"A dark preference turned the page dark (body background {background}). The previews are black strokes on a transparent background, so they vanish.");
    }

    /// <summary>Relative luminance of a rendered `rgb(...)` colour, 0 is black and 1 is white.</summary>
    static double Luminance(string cssColor)
    {
        var channels = cssColor[(cssColor.IndexOf('(') + 1)..cssColor.IndexOf(')')].Split(',');
        double Channel(int index) => double.Parse(channels[index].Trim(), CultureInfo.InvariantCulture);
        return (0.2126 * Channel(0) + 0.7152 * Channel(1) + 0.0722 * Channel(2)) / 255;
    }
}
