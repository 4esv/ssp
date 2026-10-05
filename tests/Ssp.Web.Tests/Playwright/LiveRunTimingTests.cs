using Microsoft.Playwright;
using Ssp.Web.Sharing;
using Xunit.Abstractions;

namespace Ssp.Web.Tests.Playwright;

[Collection(PlaywrightCollection.Name)]
public class LiveRunTimingTests(ITestOutputHelper output)
{
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(3);

    [PlaywrightFact]
    public Task LiveSolveNeverBlocksThePageFor100Ms() => Core().WaitAsync(Limit);

    async Task Core()
    {
        var baseUrl = Environment.GetEnvironmentVariable(PlaywrightFactAttribute.BaseUrlVariable)!;
        Assert.Equal(0, Program.Main(["install", "chromium"]));
        var fuzz = File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "library", "fuzz-transistor-diode.cir"));
        Assert.Contains("R4 n6 0 1k", fuzz);

        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1280, Height = 900 } });
        page.SetDefaultTimeout(60_000);

        // NOTE: The test server has no fallback to index.html, so the test serves it for the editor route.
        await page.RouteAsync(baseUrl + "editor", async route =>
            await route.FulfillAsync(new() { Response = await route.FetchAsync(new() { Url = baseUrl }) }));
        await page.GotoAsync(baseUrl + "editor#" + ShareCodec.Encode(fuzz));
        await page.Locator("textarea[aria-label=Netlist]").WaitForAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // NOTE: A gap between two animation frames is a time when the page did not answer. The edit gap ends when the
        // stale marker shows: it is the render of the edit. The solve gap is the longest gap from the marker to the result.
        var times = await page.EvaluateAsync<double[]>(@"async text => {
            const wait = () => new Promise(r => setTimeout(r, 10));
            let edit = 0, solve = 0, last = performance.now(), on = true, solving = false;
            const tick = () => {
                const now = performance.now();
                if (solving) solve = Math.max(solve, now - last); else edit = Math.max(edit, now - last);
                last = now;
                if (on) requestAnimationFrame(tick);
            };
            requestAnimationFrame(tick);
            await new Promise(r => setTimeout(r, 500));
            edit = 0;
            const t0 = performance.now();
            const area = document.querySelector('textarea[aria-label=Netlist]');
            area.value = text;
            area.dispatchEvent(new Event('input', { bubbles: true }));
            while (!document.querySelector('.live-state.stale')) await wait();
            await new Promise(r => requestAnimationFrame(r));
            solving = true;
            while (document.querySelector('.live-state.stale')) await wait();
            await new Promise(r => requestAnimationFrame(() => setTimeout(r, 0)));
            on = false;
            return [edit, solve, performance.now() - t0];
        }", fuzz.Replace("R4 n6 0 1k", "R4 n6 0 2.2k"));
        output.WriteLine($"Live run on the Transistor fuzz: edit gap {times[0]:F0} ms, solve gap {times[1]:F0} ms, edit to result {times[2]:F0} ms");

        Assert.Empty(await page.Locator(".live-failed").AllAsync());
        // TODO: the edit gap (the schematic auto-placement and the render of the edit) is not in the limit yet. See docs/benchmarks.md.
        Assert.True(times[1] < 100, $"The page did not answer for {times[1]:F0} ms while the live run solved.");
    }
}
