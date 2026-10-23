using Microsoft.Playwright;

namespace Ssp.Web.Tests.Playwright;

// NOTE: #382: on a phone a bottom panel opens over the canvas. The schematic must stay in the part of the canvas that the panel leaves free.
public partial class TouchPlaywrightTests
{
    [PlaywrightFact]
    public Task A_bottom_panel_leaves_the_schematic_in_view() => SheetCore().WaitAsync(Limit);

    async Task SheetCore()
    {
        await using var phone = await Phone.Open();
        var page = phone.Page;
        await page.SetViewportSizeAsync(375, 812);
        await page.Locator("rect.part").First.WaitForAsync();

        foreach (var sheet in new[] { "results", "plots", "text" })
        {
            await page.Locator($".panel-bar button[data-panel={sheet}]").TapAsync();
            var panel = (await page.Locator($".dock-panel[data-panel={sheet}]").BoundingBoxAsync())!;
            // NOTE: The view fits after the panel opens, so the test polls until one part lies above the panel.
            var seen = false;
            for (var i = 0; i < 40 && !seen; i++)
            {
                var parts = await page.Locator("rect.part").EvaluateAllAsync<double[][]>(
                    "ps => ps.map(p => { const r = p.getBoundingClientRect(); return [r.top, r.bottom, r.left, r.right]; })");
                seen = parts.Any(p => p[0] >= 0 && p[1] <= panel.Y + 1 && p[2] >= 0 && p[3] <= 375);
                if (!seen) await Task.Delay(250);
            }
            output.WriteLine($"{sheet}: panel top {panel.Y:0}, a part is above it: {seen}");
            Assert.True(seen, $"With {sheet} open, no part of the schematic lies above the panel (top {panel.Y:0} px).");
            await page.Locator($".panel-bar button[data-panel={sheet}]").TapAsync();
        }
    }
}
