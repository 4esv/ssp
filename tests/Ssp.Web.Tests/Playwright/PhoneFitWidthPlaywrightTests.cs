using Microsoft.Playwright;
using Ssp.Web.Sharing;
using Xunit.Abstractions;

namespace Ssp.Web.Tests.Playwright;

// NOTE: #441: on a phone (375x812, touch) a library pedal opens fitted to the canvas width, so a 60-unit part is 30 px or more
// on screen, and a vertical touch pan brings the bottom row into the canvas. The page does not scroll sideways.
[Collection(PlaywrightCollection.Name)]
public class PhoneFitWidthPlaywrightTests(ITestOutputHelper output)
{
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(6);

    public static string[] Pedals => ["clean-boost", "overdrive-808", "distortion-rat", "fuzz-face-si", "fuzz-muff"];

    [PlaywrightFact]
    public Task A_library_pedal_opens_at_a_readable_zoom_and_pans_down() => Core().WaitAsync(Limit);

    async Task Core()
    {
        var withDeps = Environment.GetEnvironmentVariable("SSP_PLAYWRIGHT_WITH_DEPS") == "1";
        Assert.Equal(0, Microsoft.Playwright.Program.Main(withDeps ? ["install", "--with-deps", "chromium"] : ["install", "chromium"]));
        var baseUrl = Environment.GetEnvironmentVariable(PlaywrightFactAttribute.BaseUrlVariable)!;
        var shots = Environment.GetEnvironmentVariable("SSP_SHOTS");
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var context = await browser.NewContextAsync(new() { HasTouch = true, IsMobile = true, ViewportSize = new() { Width = 375, Height = 812 } });
        var failures = new List<string>();
        foreach (var pedal in Pedals)
        {
            var hash = ShareCodec.Encode(File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "library", pedal + ".cir")));
            var page = await context.NewPageAsync();
            await page.AddInitScriptAsync(HelpSeen.Script);
            page.SetDefaultTimeout(60_000);
            await page.RouteAsync(baseUrl + "editor", async route =>
                await route.FulfillAsync(new() { Response = await route.FetchAsync(new() { Url = baseUrl }) }));
            await page.GotoAsync(baseUrl + "editor#" + hash);
            await page.Locator(".schematic[data-view]").WaitForAsync();
            await page.Locator("rect.part").First.WaitForAsync();
            await page.WaitForTimeoutAsync(500);

            var zoom = int.Parse((await page.Locator(".zoom-level").InputValueAsync()).TrimEnd('%'));
            output.WriteLine($"{pedal}: opens at {zoom}%");
            if (shots is not null) await page.ScreenshotAsync(new() { Path = Path.Combine(shots, pedal + "-open.png") });
            // NOTE: 60 units at the zoom is 30 px or more.
            if (60 * zoom / 100.0 < 30) failures.Add($"{pedal}: opens at {zoom}%, a 60-unit part is {60 * zoom / 100.0:0} px.");
            var scroll = await page.EvaluateAsync<int>("() => document.scrollingElement.scrollWidth");
            if (scroll > 375) failures.Add($"{pedal}: the page is {scroll} px wide.");

            // NOTE: Touch swipes up on empty canvas until the lowest part is in the canvas.
            var cdp = await context.NewCDPSessionAsync(page);
            for (var swipe = 0; swipe < 12 && await LowestPartGap(page) > 0; swipe++) await Swipe(cdp, page);
            var gap = await LowestPartGap(page);
            output.WriteLine($"{pedal}: after the pan the lowest part is {-gap:0} px inside the canvas bottom");
            if (gap > 0) failures.Add($"{pedal}: a vertical pan does not bring the bottom row into the canvas ({gap:0} px below).");
            if (shots is not null) await page.ScreenshotAsync(new() { Path = Path.Combine(shots, pedal + "-panned.png") });
            await page.CloseAsync();
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    // NOTE: How far the lowest part's bottom is under the canvas bottom (negative: inside).
    static Task<double> LowestPartGap(IPage page) => page.EvaluateAsync<double>("""
        () => {
            const c = document.querySelector('.schematic-editor .schematic').getBoundingClientRect();
            return Math.max(...[...document.querySelectorAll('.schematic-pins rect.part')].map(p => p.getBoundingClientRect().bottom)) - c.bottom;
        }
        """);

    static async Task Swipe(ICDPSession cdp, IPage page)
    {
        // NOTE: Only the drawing layer takes a pan, so the swipe starts on a point of it that no part, wire or label covers.
        var start = await page.EvaluateAsync<double[]>("""
            () => {
                const c = document.querySelector('.schematic-editor .schematic').getBoundingClientRect();
                for (let y = c.top + c.height * 0.75; y > c.top + 20; y -= 6)
                    for (let x = c.left + 4; x < c.right - 4; x += 6)
                        if (document.elementFromPoint(x, y)?.classList.contains('schematic-pins')) return [x, y, c.top + c.height * 0.15];
                return [c.left + 6, c.top + c.height * 0.75, c.top + c.height * 0.15];
            }
            """);
        double x = start[0], from = start[1], to = Math.Max(start[2], from - 400);
        async Task Touch(string type, double y) => await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = type,
            ["touchPoints"] = type == "touchEnd" ? Array.Empty<object>() : new object[] { new { x, y } },
        });
        await Touch("touchStart", from);
        for (var i = 1; i <= 10; i++) await Touch("touchMove", from + (to - from) * i / 10);
        await Touch("touchEnd", to);
        await page.WaitForTimeoutAsync(150);
    }
}
