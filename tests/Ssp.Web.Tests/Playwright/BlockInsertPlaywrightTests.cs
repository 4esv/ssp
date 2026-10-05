using Microsoft.Playwright;
using Ssp.Web.Sharing;
using Xunit.Abstractions;

namespace Ssp.Web.Tests.Playwright;

// NOTE: My Blocks on the fuzz starter (#181): save R2, R4 and R6 as a block, insert it on an empty spot, and one Undo takes it back.
// Set SSP_SHOTS to a folder to keep a screenshot of the block list at 1280 and 390 px.
[Collection(PlaywrightCollection.Name)]
public class BlockInsertPlaywrightTests(ITestOutputHelper output)
{
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(4);

    [PlaywrightFact]
    public Task ASavedBlockInsertsOnAnEmptySpotAndOneUndoTakesItBack() => Core().WaitAsync(Limit);

    static Task<string[]> Parts(IPage page) =>
        page.EvaluateAsync<string[]>("() => [...new Set([...document.querySelectorAll('.schematic-pins rect.part[data-ref]')].map(e => e.dataset.ref))].sort()");

    // A point on the canvas with nothing under it but the canvas, searched from the bottom left.
    const string EmptySpot = """
        () => {
            const r = document.querySelector('.schematic').getBoundingClientRect();
            for (let y = r.bottom - 80; y > r.top + 20; y -= 10)
                for (let x = r.left + 30; x < r.right - 200; x += 10) {
                    const e = document.elementFromPoint(x, y);
                    if (e && e.matches('svg.schematic-pins, .schematic, .schematic svg:not(.schematic-pins)')) return [x, y];
                }
            return null;
        }
        """;

    async Task Core()
    {
        Assert.Equal(0, Microsoft.Playwright.Program.Main(["install", "chromium"]));
        var baseUrl = Environment.GetEnvironmentVariable(PlaywrightFactAttribute.BaseUrlVariable)!;
        var hash = ShareCodec.Encode(File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "library", "fuzz-transistor-diode.cir")));
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1456, Height = 797 } });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(60_000);
        await page.RouteAsync(baseUrl + "editor", async route => await route.FulfillAsync(new() { Response = await route.FetchAsync(new() { Url = baseUrl }) }));
        await page.GotoAsync(baseUrl + "editor#" + hash);
        await page.Locator(".schematic[data-view]").WaitForAsync();
        var before = await Parts(page);

        await page.Locator(".schematic-pins rect.part[data-ref=R2]").ClickAsync(new() { Force = true });
        await page.Locator(".schematic-pins rect.part[data-ref=R4]").ClickAsync(new() { Force = true, Modifiers = [KeyboardModifier.Shift] });
        await page.Locator(".schematic-pins rect.part[data-ref=R6]").ClickAsync(new() { Force = true, Modifiers = [KeyboardModifier.Shift] });
        await page.Locator(".part-action[data-action=save-block]").ClickAsync();
        await page.Locator("input.block-name").FillAsync("Bias");
        await page.Locator("input.block-name").PressAsync("Enter");
        await Assertions.Expect(page.Locator(".schematic-hint")).ToContainTextAsync("Saved Bias");

        await page.Locator("button[data-action=blocks]").ClickAsync();
        var first = page.Locator(".block-list button[data-block]").First;
        Assert.Equal("Bias", (await first.TextContentAsync())!.Trim());
        await Shot(page, "blocks-1456.png");
        await first.ClickAsync();
        await Assertions.Expect(page.Locator(".schematic-hint")).ToContainTextAsync("Tap an empty spot");
        var spot = await page.EvaluateAsync<double[]?>(EmptySpot);
        Assert.NotNull(spot);
        await page.Mouse.ClickAsync((float)spot[0], (float)spot[1]);
        await Assertions.Expect(page.Locator(".schematic-pins rect.part[data-ref=R7]")).ToBeAttachedAsync();

        var added = (await Parts(page)).Except(before).ToArray();
        var netlist = await page.Locator("textarea[aria-label=Netlist]").InputValueAsync();
        output.WriteLine($"1456x797: inserted {string.Join(", ", added)} at {spot[0]:0},{spot[1]:0}; hint: {await page.Locator(".schematic-hint").TextContentAsync()}");
        output.WriteLine(string.Join('\n', netlist.Split('\n').Where(l => l.StartsWith("R1 ") || l.StartsWith("R3 ") || l.StartsWith("R7 "))));
        Assert.Equal(["R1", "R3", "R7"], added);
        // NOTE: The inserted parts cover none of the parts that were there.
        var overlaps = await page.EvaluateAsync<string[]>("""
            () => {
                const boxes = [...document.querySelectorAll('.schematic-pins rect.part[data-ref]')].map(e => [e.dataset.ref, e.getBoundingClientRect()]);
                const fresh = new Set(['R1', 'R7', 'R8']);
                return boxes.filter(([a]) => fresh.has(a)).flatMap(([a, r]) => boxes.filter(([b, q]) => !fresh.has(b)
                    && r.left < q.right && q.left < r.right && r.top < q.bottom && q.top < r.bottom).map(([b]) => a + '/' + b));
            }
            """);
        Assert.Empty(overlaps);

        await page.Keyboard.PressAsync("ControlOrMeta+z");
        await Assertions.Expect(page.Locator(".schematic-pins rect.part[data-ref=R7]")).ToHaveCountAsync(0);
        Assert.Equal(before, await Parts(page));

        foreach (var (w, h) in new[] { (1280, 800), (390, 844) })
        {
            await page.SetViewportSizeAsync(w, h);
            await page.Locator("button[data-action=blocks]").ClickAsync();
            await Assertions.Expect(page.Locator(".block-list")).ToBeVisibleAsync();
            await Shot(page, $"blocks-{w}.png");
            await page.Locator("button[data-action=blocks]").ClickAsync();
        }
    }

    [PlaywrightFact]
    public Task AStageDroppedOnAWireOfTheFuzzStarterSitsInSeriesAndRuns() => WireCore().WaitAsync(Limit);

    // The middle of the longest wire segment that a tap there hits, in client pixels, and the wire index.
    const string WireSpot = """
        () => {
            const found = [];
            for (const p of document.querySelectorAll('.schematic-pins polyline.wire-hit')) {
                const m = p.getScreenCTM();
                for (let i = 1; i < p.points.numberOfItems; i++) {
                    const a = p.points.getItem(i - 1), b = p.points.getItem(i);
                    const s = new DOMPoint((a.x + b.x) / 2, (a.y + b.y) / 2).matrixTransform(m);
                    if (document.elementFromPoint(s.x, s.y) === p) found.push([Math.hypot(b.x - a.x, b.y - a.y), s.x, s.y, +p.dataset.wire]);
                }
            }
            found.sort((x, y) => y[0] - x[0]);
            return found.length ? found[0].slice(1) : null;
        }
        """;

    // NOTE: #265: a stage dropped on a wire cuts it and sits in series. The test prints the netlist lines that changed and the node voltages of a run.
    async Task WireCore()
    {
        Assert.Equal(0, Microsoft.Playwright.Program.Main(["install", "chromium"]));
        var baseUrl = Environment.GetEnvironmentVariable(PlaywrightFactAttribute.BaseUrlVariable)!;
        var hash = ShareCodec.Encode(File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "library", "fuzz-transistor-diode.cir")));
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1456, Height = 797 } });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(60_000);
        await page.RouteAsync(baseUrl + "editor", async route => await route.FulfillAsync(new() { Response = await route.FetchAsync(new() { Url = baseUrl }) }));
        await page.GotoAsync(baseUrl + "editor#" + hash);
        await page.Locator(".schematic[data-view]").WaitForAsync();
        var netlist = page.Locator("textarea[aria-label=Netlist]");
        var before = await netlist.InputValueAsync();

        await page.Locator("button[data-action=blocks]").ClickAsync();
        await page.Locator(".block-list button[data-block='clip-shunt-led.cir']").ClickAsync();
        await Assertions.Expect(page.Locator(".schematic-hint")).ToContainTextAsync("a wire");
        var spot = await page.EvaluateAsync<double[]?>(WireSpot);
        Assert.NotNull(spot);
        await page.Mouse.ClickAsync((float)spot[0], (float)spot[1]);
        await Assertions.Expect(page.Locator(".schematic-hint")).ToContainTextAsync("in series");

        var after = await netlist.InputValueAsync();
        var (was, now) = (before.Split('\n'), after.Split('\n'));
        output.WriteLine($"1456x797: wire {spot[2]} at {spot[0]:0},{spot[1]:0}; hint: {await page.Locator(".schematic-hint").TextContentAsync()}");
        foreach (var line in was.Except(now)) output.WriteLine("- " + line);
        foreach (var line in now.Except(was)) output.WriteLine("+ " + line);
        Assert.NotEqual(before, after);

        await page.GetByRole(AriaRole.Button, new() { Name = "Run", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("table.voltages tbody tr").First).ToBeVisibleAsync(new() { Timeout = 120_000 });
        var voltages = await page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('table.voltages tbody tr')].map(r => r.innerText.replace('\\t', ' '))");
        output.WriteLine("Node voltages: " + string.Join("; ", voltages));
        Assert.Contains(voltages, v => v.StartsWith("n10 "));

        await page.Keyboard.PressAsync("ControlOrMeta+z");
        await Assertions.Expect(netlist).ToHaveValueAsync(before);
    }

    static async Task Shot(IPage page, string name)
    {
        if (Environment.GetEnvironmentVariable("SSP_SHOTS") is not { Length: > 0 } dir) return;
        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new() { Path = Path.Combine(dir, name) });
    }
}
