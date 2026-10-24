using Microsoft.Playwright;

namespace Ssp.Web.Tests.Playwright;

// NOTE: #383: on a phone these controls are 44 px tall or more: Projects, the project name, the zoom input, the Read the docs link and the Open the Netlist panel button.
[Collection(PlaywrightCollection.Name)]
public class TouchTargetsPlaywrightTests
{
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(3);

    [PlaywrightFact]
    public Task Listed_controls_are_44_px_tall_on_a_phone() => Core().WaitAsync(Limit);

    async Task Core()
    {
        var withDeps = Environment.GetEnvironmentVariable("SSP_PLAYWRIGHT_WITH_DEPS") == "1";
        Assert.Equal(0, Microsoft.Playwright.Program.Main(withDeps ? ["install", "--with-deps", "chromium"] : ["install", "chromium"]));
        var baseUrl = Environment.GetEnvironmentVariable(PlaywrightFactAttribute.BaseUrlVariable)!;
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var context = await browser.NewContextAsync(new() { HasTouch = true, IsMobile = true, ViewportSize = new() { Width = 375, Height = 812 } });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(60_000);
        await page.RouteAsync(baseUrl + "editor", async route =>
            await route.FulfillAsync(new() { Response = await route.FetchAsync(new() { Url = baseUrl }) }));
        var start = page.Locator("button.start-here-button");
        for (var attempt = 0; ; attempt++)
        {
            if (attempt == 0) await page.GotoAsync(baseUrl + "editor");
            else await page.ReloadAsync();
            try { await start.WaitForAsync(new() { Timeout = 30_000 }); break; }
            catch (TimeoutException) when (attempt < 2) { }
        }

        var failures = new List<string>();
        async Task Measure(string name, ILocator locator)
        {
            var box = (await locator.First.BoundingBoxAsync())!;
            if (box.Height < 44) failures.Add($"{name} is {box.Height} px tall");
        }

        await Measure("Projects", page.Locator("button.projects-toggle"));
        await Measure("Project name", page.Locator("input.project-name"));
        await Measure("Zoom input", page.Locator(".zoom-level"));
        await start.TapAsync();
        await page.Locator(".shortcut-sheet").WaitForAsync();
        await Measure("Read the docs", page.Locator(".shortcut-sheet a", new() { HasTextString = "Read the docs" }));
        await page.Locator(".shortcut-sheet button", new() { HasTextString = "Close" }).TapAsync();

        var text = page.Locator(".panel-bar button[data-panel=text]");
        await text.TapAsync();
        var netlist = page.Locator("textarea[aria-label=Netlist]");
        await netlist.FillAsync((await netlist.InputValueAsync()).TrimEnd() + "\nstray text\n");
        await text.TapAsync();
        await page.Locator("button.open-netlist").First.WaitForAsync();
        await Measure("Open the Netlist panel", page.Locator("button.open-netlist"));

        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }
}
