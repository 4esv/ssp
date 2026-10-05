using System.Globalization;
using Microsoft.Playwright;
using Ssp.Web.Sharing;
using Xunit.Abstractions;

namespace Ssp.Web.Tests.Playwright;

// NOTE: The canvas must have room for the fuzz starter at laptop sizes (#163): 600 x 450 px or more, Fit at 50 percent
// or more, no control over a part, and no page scroll. The test prints the measured sizes.
[Collection(PlaywrightCollection.Name)]
public class EditorLayoutPlaywrightTests(ITestOutputHelper output)
{
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(4);

    [PlaywrightFact]
    public Task FuzzStarterHasRoomAtLaptopSizes() => FuzzStarterHasRoomAtLaptopSizesCore().WaitAsync(Limit);

    [PlaywrightFact]
    public Task SchematicIsTheWholeScreenOnAPhone() => SchematicIsTheWholeScreenOnAPhoneCore().WaitAsync(Limit);

    [PlaywrightFact]
    public Task ToolRowFitsAndGroupRowAndPasteMoveNothing() => ToolRowCore().WaitAsync(Limit);

    async Task ToolRowCore()
    {
        await using var session = await Session.Open();
        var failures = new List<string>();
        foreach (var (width, height) in new[] { (1456, 797), (1280, 800) })
        {
            var page = await session.Editor(width, height);
            var row = await page.EvaluateAsync<double[]>(RowScript);
            var outside = await page.EvaluateAsync<string[]>(OutsideScript);
            output.WriteLine($"{width}x{height}: tool row scrollWidth {row[0]} clientWidth {row[1]}, outside the viewport [{string.Join(", ", outside)}]");
            if (row[0] > row[1]) failures.Add($"{width}x{height}: the tool row scrolls ({row[0]} > {row[1]}).");
            if (outside.Length > 0) failures.Add($"{width}x{height}: controls outside the viewport: {string.Join(", ", outside)}.");

            // NOTE: The group action row covers no unselected part when a side of the group is free. A crowded group such as D1 and R4, whose four sides
            // all hold a part, is a known gap (see the issue "the group action row can still cover a part").
            foreach (var group in new[] { new[] { "R2", "R4", "R6" }, ["R2", "D2"], ["D1", "R6"] })
            {
                await page.Locator(".schematic-pins rect.part[data-ref=" + group[0] + "]").ClickAsync(new() { Force = true });
                foreach (var r in group.Skip(1))
                {
                    await page.Locator($".schematic-pins rect.part[data-ref={r}]").ClickAsync(new() { Force = true, Modifiers = [KeyboardModifier.Shift] });
                }
                await Assertions.Expect(page.Locator(".part-action[data-action=copy]")).ToBeVisibleAsync();
                var covered = await page.EvaluateAsync<string[]>(ActionOverlapScript);
                output.WriteLine($"{width}x{height}: group {string.Join("+", group)}: action row covers [{string.Join(", ", covered)}]");
                if (covered.Length > 0) failures.Add($"{width}x{height}: the action row of {string.Join("+", group)} covers {string.Join(", ", covered)}.");
                await page.Keyboard.PressAsync("Escape");
            }

            // NOTE: A paste makes warnings for the unconnected copies. They float, so the canvas stays where it was.
            await page.Locator(".schematic-pins rect.part[data-ref=R2]").ClickAsync(new() { Force = true });
            await page.Locator(".schematic-pins rect.part[data-ref=R4]").ClickAsync(new() { Force = true, Modifiers = [KeyboardModifier.Shift] });
            var before = (await page.Locator(".schematic-editor .schematic").BoundingBoxAsync())!;
            var problemsBefore = await page.Locator(".problems li").CountAsync();
            await page.Keyboard.PressAsync("ControlOrMeta+c");
            await page.Keyboard.PressAsync("ControlOrMeta+v");
            // NOTE: R2 and R4 are copied, and the free references are R1 and R3.
            await Assertions.Expect(page.Locator(".schematic-pins rect.part[data-ref=R3]")).ToBeAttachedAsync();
            await page.WaitForTimeoutAsync(300);
            var after = (await page.Locator(".schematic-editor .schematic").BoundingBoxAsync())!;
            var problemsAfter = await page.Locator(".problems li").CountAsync();
            output.WriteLine($"{width}x{height}: canvas top/height {before.Y:0.#}/{before.Height:0.#} before, {after.Y:0.#}/{after.Height:0.#} after a paste; problems {problemsBefore} then {problemsAfter}");
            if (Math.Abs(before.Y - after.Y) > 0.5 || Math.Abs(before.Height - after.Height) > 0.5 || Math.Abs(before.X - after.X) > 0.5 || Math.Abs(before.Width - after.Width) > 0.5)
            {
                failures.Add($"{width}x{height}: the paste moved the canvas ({before.Y:0.#}/{before.Height:0.#} to {after.Y:0.#}/{after.Height:0.#}).");
            }
            await page.CloseAsync();
        }
        Assert.Empty(failures);
    }

    [PlaywrightFact]
    public Task RunAndNewStayInViewOnAPhone() => PhoneRowCore().WaitAsync(Limit);

    async Task PhoneRowCore()
    {
        await using var session = await Session.Open();
        var page = await session.Editor(390, 844);
        var row = await page.EvaluateAsync<double[]>(RowScript);
        var outside = await page.EvaluateAsync<string[]>(OutsideScript);
        output.WriteLine($"390x844: tool row scrollWidth {row[0]} clientWidth {row[1]}, outside the viewport [{string.Join(", ", outside)}]");
        Assert.DoesNotContain("run", outside);
        Assert.DoesNotContain("new", outside);
    }

    async Task FuzzStarterHasRoomAtLaptopSizesCore()
    {
        await using var session = await Session.Open();
        var failures = new List<string>();
        foreach (var (width, height) in new[] { (1456, 797), (1280, 800) })
        {
            var page = await session.Editor(width, height);
            await page.Locator("[data-zoom=fit]").ClickAsync();
            await page.WaitForTimeoutAsync(300);

            var canvas = (await page.Locator(".schematic-editor .schematic").BoundingBoxAsync())!;
            var zoom = int.Parse((await page.Locator(".zoom-level").TextContentAsync())!.TrimEnd('%'), CultureInfo.InvariantCulture);
            var scroll = await page.EvaluateAsync<int[]>("() => [document.scrollingElement.scrollWidth, document.scrollingElement.scrollHeight, innerWidth, innerHeight]");
            var overlaps = await page.EvaluateAsync<string[]>(OverlapScript);
            output.WriteLine($"{width}x{height}: canvas {canvas.Width:0}x{canvas.Height:0}, fit {zoom}%, page {scroll[0]}x{scroll[1]}, overlaps [{string.Join(", ", overlaps)}]");

            if (canvas.Width < 600 || canvas.Height < 450) failures.Add($"{width}x{height}: the canvas is {canvas.Width:0}x{canvas.Height:0}.");
            if (zoom < 50) failures.Add($"{width}x{height}: Fit gives {zoom}%.");
            if (scroll[0] > scroll[2] || scroll[1] > scroll[3]) failures.Add($"{width}x{height}: the page scrolls ({scroll[0]}x{scroll[1]}).");
            if (overlaps.Length > 0) failures.Add($"{width}x{height}: controls over parts: {string.Join(", ", overlaps)}.");
            await page.CloseAsync();
        }
        Assert.Empty(failures);
    }

    async Task SchematicIsTheWholeScreenOnAPhoneCore()
    {
        await using var session = await Session.Open();
        var page = await session.Editor(390, 844);

        var canvas = (await page.Locator(".schematic-editor .schematic").BoundingBoxAsync())!;
        var scroll = await page.EvaluateAsync<int[]>("() => [document.scrollingElement.scrollWidth, document.scrollingElement.scrollHeight]");
        output.WriteLine($"390x844: canvas {canvas.Width:0}x{canvas.Height:0}, page {scroll[0]}x{scroll[1]}");
        Assert.True(canvas.Width >= 360, $"The canvas is {canvas.Width:0} px wide.");
        Assert.True(canvas.Height >= 844 * 0.55, $"The canvas is {canvas.Height:0} px high.");
        Assert.True(scroll[1] <= 844, $"The page is {scroll[1]} px high.");
        Assert.False(await page.Locator(".dock-panel[data-panel=results]").IsVisibleAsync());

        // NOTE: The bottom bar opens a panel over the schematic, and its close button shuts it.
        await page.Locator(".panel-bar button[data-panel=results]").ClickAsync();
        await Assertions.Expect(page.Locator(".dock-panel[data-panel=results]")).ToBeVisibleAsync();
        await page.Locator(".panel-bar button[data-panel=results]").ClickAsync();
        await Assertions.Expect(page.Locator(".dock-panel[data-panel=results]")).ToBeHiddenAsync();
    }

    const string RowScript = "() => { const r = document.querySelector('.schematic-tools'); return [r.scrollWidth, r.clientWidth]; }";

    // NOTE: A control of the tool row that shows but is not wholly in the viewport.
    const string OutsideScript = """
        () => [...document.querySelectorAll('.schematic-tools button, .schematic-tools summary')].filter(b => b.offsetParent)
            .filter(b => { const r = b.getBoundingClientRect(); return r.left < 0 || r.right > innerWidth || r.top < 0 || r.bottom > innerHeight; })
            .map(b => b.dataset.action || b.dataset.tool || b.className)
        """;

    const string ActionOverlapScript = """
        () => {
            const hit = (a, b) => a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom;
            const parts = [...document.querySelectorAll('.schematic-pins rect.part[data-ref]:not(.selected)')];
            return [...document.querySelectorAll('.part-action')].flatMap(a => parts.filter(p => hit(a.getBoundingClientRect(), p.getBoundingClientRect())).map(p => p.dataset.ref));
        }
        """;

    // NOTE: A control is a button, input or toolbar in the schematic editor that is not on the canvas layer.
    const string OverlapScript = """
        () => {
            const parts = [...document.querySelectorAll('.schematic-pins rect.part[data-ref], .schematic-pins g.knob')]
                .map(e => [e.getAttribute('data-ref'), e.getBoundingClientRect()]);
            const controls = [...document.querySelectorAll('.schematic-zoom, .schematic-tools, .palette, .toolbar, .schematic-hint, .first-run-hints, .live-line, .dock-closed')]
                .map(e => [e.className, e.getBoundingClientRect()]).filter(([, r]) => r.width > 0 && r.height > 0);
            const hit = (a, b) => a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom;
            return controls.flatMap(([c, cr]) => parts.filter(([, pr]) => hit(cr, pr)).map(([p]) => `${c} over ${p}`));
        }
        """;

    sealed class Session : IAsyncDisposable
    {
        IPlaywright playwright = null!;
        IBrowser browser = null!;
        IBrowserContext? context;
        string baseUrl = "";
        string hash = "";

        public static async Task<Session> Open()
        {
            var withDeps = Environment.GetEnvironmentVariable("SSP_PLAYWRIGHT_WITH_DEPS") == "1";
            Assert.Equal(0, Microsoft.Playwright.Program.Main(withDeps ? ["install", "--with-deps", "chromium"] : ["install", "chromium"]));
            var session = new Session
            {
                baseUrl = Environment.GetEnvironmentVariable(PlaywrightFactAttribute.BaseUrlVariable)!,
                hash = ShareCodec.Encode(File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "library", "fuzz-transistor-diode.cir"))),
                playwright = await Microsoft.Playwright.Playwright.CreateAsync(),
            };
            session.browser = await session.playwright.Chromium.LaunchAsync();
            return session;
        }

        public async Task<IPage> Editor(int width, int height)
        {
            // NOTE: The pages share one context, so the second page takes the app from the cache. The test server drops
            // connections when a page fetches all of the app at once.
            context ??= await browser.NewContextAsync();
            var page = await context.NewPageAsync();
            await page.SetViewportSizeAsync(width, height);
            page.SetDefaultTimeout(60_000);
            await page.RouteAsync(baseUrl + "editor", async route =>
                await route.FulfillAsync(new() { Response = await route.FetchAsync(new() { Url = baseUrl }) }));
            await page.GotoAsync(baseUrl + "editor#" + hash);
            await page.Locator(".schematic[data-view]").WaitForAsync();
            return page;
        }

        public async ValueTask DisposeAsync()
        {
            await browser.DisposeAsync();
            playwright.Dispose();
        }
    }
}
