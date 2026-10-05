using Microsoft.Playwright;
using Ssp.Web.Library;
using Ssp.Web.Sharing;

namespace Ssp.Web.Tests.Playwright;

[Collection(PlaywrightCollection.Name)]
public class ProjectReloadPlaywrightTests
{
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(3);

    [PlaywrightFact]
    public Task An_edit_to_the_fuzz_starter_survives_a_reload() => ReloadCore().WaitAsync(Limit);

    static async Task ReloadCore()
    {
        var baseUrl = Environment.GetEnvironmentVariable(PlaywrightFactAttribute.BaseUrlVariable)!;
        Assert.Equal(0, Program.Main(["install", "chromium"]));

        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1280, Height = 900 } });
        page.SetDefaultTimeout(30_000);
        await page.RouteAsync(baseUrl + "editor", async route =>
            await route.FulfillAsync(new() { Response = await route.FetchAsync(new() { Url = baseUrl }) }));

        var fuzz = CircuitLibrary.All.Single(c => c.Title == "Transistor fuzz");
        await page.GotoAsync(baseUrl + "editor#" + ShareCodec.Encode(fuzz.Netlist));
        var r2 = page.Locator("svg.schematic-pins rect.part[data-ref=R2]");
        await r2.WaitForAsync(new() { Timeout = 60_000 });

        // NOTE: A rotation is a hand layout edit and a netlist edit is a text edit. Both must come back.
        await r2.ClickAsync();
        await page.Keyboard.PressAsync("r");
        await page.WaitForTimeoutAsync(300);
        var box = await Place(r2);
        await page.Locator("textarea[aria-label=Netlist]").FocusAsync();
        await page.Keyboard.PressAsync("Control+End");
        await page.Keyboard.TypeAsync("* kept\n");
        await Assertions.Expect(page.Locator(".project-state")).ToHaveTextAsync("Saved");
        var netlist = await page.Locator("textarea[aria-label=Netlist]").InputValueAsync();
        Assert.EndsWith("editor", page.Url);

        await page.ReloadAsync();
        await r2.WaitForAsync(new() { Timeout = 60_000 });

        Assert.Equal(netlist, await page.Locator("textarea[aria-label=Netlist]").InputValueAsync());
        Assert.Equal(box, await Place(r2));
    }

    // NOTE: The place in schematic units. The view on the screen fits the page, so a screen box is not the layout.
    static Task<string> Place(ILocator part) =>
        part.EvaluateAsync<string>("e => ['x', 'y', 'width', 'height'].map(a => e.getAttribute(a)).join(' ')");
}
