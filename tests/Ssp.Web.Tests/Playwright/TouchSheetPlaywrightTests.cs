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

    // NOTE: #387: with a panel open the canvas shows the circuit at a size that a user reads, and the editing controls give way to it.
    [PlaywrightFact]
    public Task An_open_panel_leaves_a_readable_circuit_and_no_palette() => ReadableCore().WaitAsync(Limit);

    async Task ReadableCore()
    {
        await using var phone = await Phone.Open();
        var page = phone.Page;
        await page.SetViewportSizeAsync(375, 812);
        await page.Locator("rect.part").First.WaitForAsync();
        var palette = page.Locator(".palette");
        Assert.True(await palette.IsVisibleAsync(), "The palette shows with no panel open.");

        // NOTE: No toolbar row holds one button alone.
        var tops = await page.Locator(".schematic-tools button:visible").EvaluateAllAsync<double[]>("bs => bs.map(b => Math.round(b.getBoundingClientRect().top))");
        var lone = tops.GroupBy(t => t).Where(g => g.Count() < 2).Select(g => g.Key).ToList();
        output.WriteLine("toolbar button rows: " + string.Join(", ", tops.GroupBy(t => t).Select(g => $"{g.Key}:{g.Count()}")));
        Assert.True(lone.Count == 0, "A toolbar row holds one button: " + string.Join(", ", lone));

        foreach (var sheet in new[] { "results", "plots", "text" })
        {
            await page.Locator($".panel-bar button[data-panel={sheet}]").TapAsync();
            var panel = (await page.Locator($".dock-panel[data-panel={sheet}]").BoundingBoxAsync())!;
            var zoom = 0;
            var seen = false;
            for (var i = 0; i < 40 && !(seen && zoom >= 60); i++)
            {
                zoom = int.Parse((await page.Locator(".zoom-level").InputValueAsync()).TrimEnd('%'));
                var parts = await page.Locator("rect.part").EvaluateAllAsync<double[][]>(
                    "ps => ps.map(p => { const r = p.getBoundingClientRect(); return [r.top, r.bottom, r.left, r.right]; })");
                seen = parts.Any(p => p[0] >= 0 && p[1] <= panel.Y + 1 && p[2] >= 0 && p[3] <= 375);
                if (!(seen && zoom >= 60)) await Task.Delay(250);
            }
            output.WriteLine($"{sheet}: zoom {zoom}%, a part is above the panel: {seen}, palette visible: {await palette.IsVisibleAsync()}");
            Assert.True(zoom >= 60, $"With {sheet} open the zoom is {zoom}%, not 60% or more.");
            Assert.True(seen, $"With {sheet} open, no part lies above the panel.");
            Assert.False(await palette.IsVisibleAsync(), $"The palette shows with {sheet} open.");
            Assert.True(await page.Locator("button.run").IsVisibleAsync(), $"Run is hidden with {sheet} open.");
            await page.Locator($".panel-bar button[data-panel={sheet}]").TapAsync();
            await palette.WaitForAsync();
        }
    }
}
