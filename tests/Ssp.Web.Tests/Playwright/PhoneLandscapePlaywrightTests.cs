using Microsoft.Playwright;

namespace Ssp.Web.Tests.Playwright;

// NOTE: #395: a phone turned sideways (812x375, touch) gets the phone layout. Every toolbar control is on screen, and the circuit stays in view with no sheet and with Results open.
[Collection(PlaywrightCollection.Name)]
public class PhoneLandscapePlaywrightTests
{
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(3);

    [PlaywrightFact]
    public Task A_phone_in_landscape_gets_the_phone_layout() => Core().WaitAsync(Limit);

    async Task Core()
    {
        var withDeps = Environment.GetEnvironmentVariable("SSP_PLAYWRIGHT_WITH_DEPS") == "1";
        Assert.Equal(0, Microsoft.Playwright.Program.Main(withDeps ? ["install", "--with-deps", "chromium"] : ["install", "chromium"]));
        var baseUrl = Environment.GetEnvironmentVariable(PlaywrightFactAttribute.BaseUrlVariable)!;
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var context = await browser.NewContextAsync(new()
        {
            HasTouch = true,
            IsMobile = true,
            ViewportSize = new() { Width = 812, Height = 375 },
        });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(60_000);
        await page.RouteAsync(baseUrl + "editor", async route =>
            await route.FulfillAsync(new() { Response = await route.FetchAsync(new() { Url = baseUrl }) }));
        var start = page.Locator("button.start-here-button");
        for (var attempt = 0; ; attempt++)
        {
            if (attempt == 0) await page.GotoAsync(baseUrl + "editor");
            else await page.ReloadAsync();
            try
            {
                await start.WaitForAsync(new() { Timeout = 30_000 });
                break;
            }
            catch (TimeoutException) when (attempt < 2) { }
        }

        // NOTE: (a) The phone layout has the bottom panel bar and no dock tabs.
        Assert.True(await page.Locator(".panel-bar").First.IsVisibleAsync(), "The panel bar is hidden: the editor shows the desktop dock.");
        Assert.False(await page.Locator(".dock-tabs").First.IsVisibleAsync(), "The dock tabs show: the editor shows the desktop dock.");

        // NOTE: (b) Every toolbar button is inside the viewport.
        var outside = new List<string>();
        var buttons = page.Locator(".schematic-tools button");
        for (var i = 0; i < await buttons.CountAsync(); i++)
        {
            var box = await buttons.Nth(i).BoundingBoxAsync();
            if (box is null) continue;
            if (box.X < 0 || box.Y < 0 || box.X + box.Width > 812.5 || box.Y + box.Height > 375.5)
                outside.Add($"{(await buttons.Nth(i).InnerTextAsync()).Trim()} x={box.X:F0} y={box.Y:F0} w={box.Width:F0}");
        }
        Assert.True(outside.Count == 0, "Outside 812x375: " + string.Join("; ", outside));

        // NOTE: (c) A part lies inside the canvas with no sheet and with Results open.
        async Task<bool> PartSeen()
        {
            for (var i = 0; i < 40; i++)
            {
                var ok = await page.EvaluateAsync<bool>("""
                    (() => {
                      const c = document.querySelector('.dock-panel[data-panel=schematic]').getBoundingClientRect();
                      return [...document.querySelectorAll('rect.part')].some(p => { const r = p.getBoundingClientRect();
                        return r.left >= Math.max(0, c.left) && r.right <= Math.min(812, c.right) && r.top >= Math.max(0, c.top) && r.bottom <= Math.min(375, c.bottom) && r.width > 0; });
                    })()
                    """);
                if (ok) return true;
                await Task.Delay(250);
            }
            return false;
        }

        await page.Locator("rect.part").First.WaitForAsync();
        Assert.True(await PartSeen(), "With no sheet open, no part lies inside the canvas.");
        // NOTE: With no sheet the circuit is big enough to read, not a speck.
        var zoom = 0;
        for (var i = 0; i < 20 && zoom < 60; i++)
        {
            zoom = int.Parse((await page.Locator(".zoom-level").InputValueAsync()).TrimEnd('%'));
            if (zoom < 60) await Task.Delay(250);
        }
        Assert.True(zoom >= 60, $"With no sheet open the zoom is {zoom}%, not 60% or more.");
        await page.Locator(".panel-bar button[data-panel=results]").TapAsync();
        Assert.True(await PartSeen(), "With Results open, no part lies inside the visible canvas.");
    }
}
