using System.Globalization;
using System.Text.Json;
using Microsoft.Playwright;
using Ssp.Web.Library;
using Ssp.Web.Sharing;
using Xunit.Abstractions;

namespace Ssp.Web.Tests.Playwright;

[Collection(PlaywrightCollection.Name)]
public class ZoomPlaywrightTests(ITestOutputHelper output)
{
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(3);
    const double MaxDrift = 1;

    [PlaywrightFact]
    public Task The_point_under_the_pointer_stays_put_on_wheel_and_pinch() => Core().WaitAsync(Limit);

    async Task Core()
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
        await page.Locator("svg.schematic-pins rect.part[data-ref=R2]").WaitForAsync(new() { Timeout = 60_000 });
        await page.WaitForTimeoutAsync(500);

        var worst = 0d;
        foreach (var (kind, ctrl, dy) in new[] { ("wheel", false, -100d), ("pinch", true, -10d) })
        {
            foreach (var (x, y) in new[] { (200d, 200d), (700d, 400d) })
            {
                var ticks = await Measure(page, ctrl, dy, x, y);
                foreach (var (i, t) in ticks.Select((t, i) => (i + 1, t)))
                {
                    output.WriteLine($"{kind} at ({x},{y}) tick {i}: zoom {t.Zoom.ToString("0.####", CultureInfo.InvariantCulture)} drift {t.Drift.ToString("0.###", CultureInfo.InvariantCulture)} px time {t.Ms.ToString("0.#", CultureInfo.InvariantCulture)} ms");
                    worst = Math.Max(worst, t.Drift);
                }
            }
        }
        Assert.True(worst <= MaxDrift, $"The worst drift is {worst} px. The limit is {MaxDrift} px.");
    }

    record Tick(double Zoom, double Drift, double Ms);

    // NOTE: Ten ticks at one pointer position. The drift is the screen distance between the pointer and the place of the world point that was under it before the tick.
    // The world is the coordinate system of the pins svg, so its screen matrix gives the world point of a screen position and back.
    static async Task<Tick[]> Measure(IPage page, bool ctrl, double deltaY, double x, double y)
    {
        await page.Locator("[data-zoom=fit]").ClickAsync();
        await page.WaitForTimeoutAsync(100);
        var json = await page.EvaluateAsync<JsonElement>(@"async ([ctrl, dy, x, y]) => {
            const pane = document.querySelector('figure.schematic');
            const svg = pane.querySelector('svg.schematic-pins');
            const origin = pane.getBoundingClientRect();
            const cx = origin.left + pane.clientLeft + x, cy = origin.top + pane.clientTop + y;
            const world = (px, py) => { const p = new DOMPoint(px, py).matrixTransform(svg.getScreenCTM().inverse()); return [p.x, p.y]; };
            const screen = (wx, wy) => { const p = new DOMPoint(wx, wy).matrixTransform(svg.getScreenCTM()); return [p.x, p.y]; };
            const zoom = () => parseFloat(pane.style.getPropertyValue('--z'));
            const frame = () => new Promise(r => requestAnimationFrame(r));
            const out = [];
            for (let i = 0; i < 10; i++) {
                const [wx, wy] = world(cx, cy);
                const was = pane.getAttribute('style');
                const t0 = performance.now();
                pane.dispatchEvent(new WheelEvent('wheel', { bubbles: true, cancelable: true, clientX: cx, clientY: cy, deltaY: dy, deltaMode: 0, ctrlKey: ctrl }));
                while (pane.getAttribute('style') === was && performance.now() - t0 < 2000) await frame();
                const ms = performance.now() - t0;
                await frame();
                const [sx, sy] = screen(wx, wy);
                out.push({ zoom: zoom(), drift: Math.hypot(sx - cx, sy - cy), ms });
            }
            return out;
        }", new object[] { ctrl, deltaY, x, y });
        return json.EnumerateArray().Select(e => new Tick(e.GetProperty("zoom").GetDouble(), e.GetProperty("drift").GetDouble(), e.GetProperty("ms").GetDouble())).ToArray();
    }
}
