using Microsoft.Playwright;

namespace Ssp.Web.Tests.Playwright;

/// <summary>
/// The V3 look (#416): a control is a word in brackets, the page is drawn in the substrate's two inks and no
/// other colour, and a finger has 44 px to land on. The schematic keeps its drawing and a chart keeps a colour
/// for each curve, so the ink check leaves the painted parts out.
/// </summary>
[Collection(PlaywrightCollection.Name)]
public class SubstratePlaywrightTests
{
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(3);

    [PlaywrightFact]
    public Task Every_toolbar_control_is_a_bracket_control() => Run(async (page, _) =>
    {
        var controls = await page.Locator("[role=toolbar] button:visible").AllAsync();
        Assert.True(controls.Count >= 4, $"The editor has {controls.Count} toolbar controls.");

        var wrong = new List<string>();
        foreach (var control in controls)
        {
            var name = (await control.InnerTextAsync()).Trim();
            var before = await Marks(control, "::before");
            var after = await Marks(control, "::after");
            if (!before.StartsWith("\"[\"", StringComparison.Ordinal)) wrong.Add($"{name} opens with {before}");
            if (!after.StartsWith("\"]\"", StringComparison.Ordinal)) wrong.Add($"{name} closes with {after}");
            // NOTE: The marks are drawn, not read: the alternative is empty, so a reader hears the word alone and
            // the text content carries no bracket.
            if (name.Contains('[') || name.Contains(']')) wrong.Add($"{name} has a bracket in its text");
        }

        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }).WaitAsync(Limit);

    static async Task<string> Marks(ILocator control, string pseudo) =>
        await control.EvaluateAsync<string>($"e => getComputedStyle(e, '{pseudo}').content");

    [PlaywrightFact]
    public Task No_colour_on_the_editor_is_off_the_two_inks() => Run(async (page, _) =>
    {
        var off = await page.EvaluateAsync<string[]>(
            """
            () => {
                const seen = new Set()
                const out = []
                // NOTE: The schematic and the charts paint themselves, and a curve is told from the others by
                // its colour, so a drawn part is not page colour.
                const drawn = 'svg, canvas, img'
                // NOTE: The two inks are #18181b and #e4e4e7, and every tone between them (dim, off) is a mix
                // of the two, so a colour is on them when it sits on that line: r = g, b = r + 3, r in 24..228.
                const offInk = (c) => {
                    const n = c.match(/-?[\d.]+/g)
                    if (!n || n.length < 3) return false
                    const [r, g, b] = n.slice(0, 3).map((v) => Number(v) * (c.startsWith('color(') ? 255 : 1))
                    return Math.abs(r - g) > 2 || Math.abs(b - r - 3) > 2 || r < 22 || r > 230
                }
                const props = ['color', 'backgroundColor', 'borderTopColor', 'borderRightColor',
                    'borderBottomColor', 'borderLeftColor', 'outlineColor', 'textDecorationColor']
                for (const el of document.querySelectorAll('*')) {
                    if (el.closest(drawn) || !el.getClientRects().length) continue
                    const s = getComputedStyle(el)
                    for (const prop of props) {
                        const c = s[prop]
                        if (!c || c === 'rgba(0, 0, 0, 0)' || seen.has(c)) continue
                        seen.add(c)
                        if (offInk(c)) out.push(`${el.tagName.toLowerCase()}.${el.className} ${prop} ${c}`)
                    }
                }
                return out
            }
            """);

        Assert.True(off.Length == 0, "Off the two inks: " + string.Join("; ", off));
    }).WaitAsync(Limit);

    [PlaywrightFact]
    public Task The_text_is_set_in_Handjet_Cells_and_no_page_asks_for_Pixelta() => Run(async (page, baseUrl) =>
    {
        var wrong = new List<string>();
        foreach (var path in new[] { "editor", "" })
        {
            if (path == "") await page.GotoAsync(baseUrl);
            await page.Locator(path == "" ? "nav.home-links a" : "button.start-here-button").First.WaitForAsync();
            await page.EvaluateAsync("document.fonts.ready");
            var faces = await page.EvaluateAsync<string[]>(
                "() => [...document.fonts].filter((f) => f.status !== 'unloaded').map((f) => f.family.replaceAll('\"', '') + ' ' + f.status)");
            var body = await page.EvaluateAsync<string>("getComputedStyle(document.body).fontFamily");
            // NOTE: The fonts are on the server only (they belong to aesv.io), so a build without them falls back.
            var served = await page.EvaluateAsync<bool>("async () => (await fetch('substrate/fonts/handjet-cells.woff2', { method: 'HEAD' })).ok");
            if (served && !faces.Contains("Handjet Cells loaded")) wrong.Add($"/{path} did not load Handjet Cells: {string.Join(", ", faces)}");
            if (faces.Any(f => f.StartsWith("Pixelta", StringComparison.Ordinal))) wrong.Add($"/{path} asked for Pixelta Cells");
            if (!body.StartsWith("\"Handjet Cells\"", StringComparison.Ordinal)) wrong.Add($"/{path} body is set in {body}");
        }

        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }).WaitAsync(Limit);

    [PlaywrightFact]
    public Task Every_tapped_control_has_a_44_px_hit_box_on_a_phone() => Run(async (page, _) =>
    {
        var small = await page.EvaluateAsync<string[]>(
            """
            () => {
                const out = []
                const controls = 'button, a, select, input, [role=button], [role=tab]'
                for (const el of document.querySelectorAll(controls)) {
                    const r = el.getBoundingClientRect()
                    if (!r.width || !r.height) continue
                    if (r.width < 44 || r.height < 44) {
                        const text = el.textContent.trim().slice(0, 24)
                        out.push(`${el.tagName.toLowerCase()}.${el.className} ${Math.round(r.width)}x${Math.round(r.height)} "${text}"`)
                    }
                }
                return out
            }
            """);

        Assert.True(small.Length == 0, "Under 44 px on a phone: " + string.Join("; ", small));
    }, 390).WaitAsync(Limit);

    [PlaywrightFact]
    public Task The_page_never_scrolls_sideways_and_the_controls_never_overlap() => Run(async (page, baseUrl) =>
    {
        var wrong = new List<string>();
        foreach (var width in new[] { 390, 1440 })
        {
            await page.SetViewportSizeAsync(width, width == 390 ? 844 : 900);

            await page.GotoAsync(baseUrl + "editor");
            await Opening(page);
            wrong.AddRange(await Sideways(page, $"the editor at {width}"));
            wrong.AddRange(await Overlaps(page, $"the editor at {width}"));

            await page.GotoAsync(baseUrl);
            await page.Locator("nav.home-links a").First.WaitForAsync(new() { Timeout = 60_000 });
            wrong.AddRange(await Sideways(page, $"the home page at {width}"));
            wrong.AddRange(await Overlaps(page, $"the home page at {width}"));
        }

        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }).WaitAsync(Limit);

    static async Task<string[]> Sideways(IPage page, string what) =>
        await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > document.documentElement.clientWidth")
            ? [what + " scrolls sideways"]
            : [];

    /// <summary>No two controls of a bar sit on each other.</summary>
    static async Task<string[]> Overlaps(IPage page, string what) =>
        (await page.EvaluateAsync<string[]>(
            """
            () => {
                const out = []
                for (const bar of document.querySelectorAll('[role=toolbar], nav.home-links')) {
                    const boxes = [...bar.querySelectorAll('button, a')]
                        .filter((e) => e.getClientRects().length)
                        .map((e) => e.getBoundingClientRect())
                    for (let i = 0; i < boxes.length; i++)
                        for (let j = i + 1; j < boxes.length; j++) {
                            const a = boxes[i]
                            const b = boxes[j]
                            const w = Math.min(a.right, b.right) - Math.max(a.left, b.left)
                            const h = Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top)
                            if (w > 1 && h > 1) out.push(Math.round(w) + 'x' + Math.round(h) + ' px')
                        }
                }
                return out
            }
            """)).Select(hit => $"{what}: two controls overlap by {hit}").ToArray();

    /// <summary>
    /// Opens the app in a browser at a width, on the editor page, and hands the page and the served base URL to
    /// the test. Returns once the editor has booted.
    /// </summary>
    static async Task Run(Func<IPage, string, Task> test, int width = 1280)
    {
        var withDeps = Environment.GetEnvironmentVariable("SSP_PLAYWRIGHT_WITH_DEPS") == "1";
        Assert.Equal(0, Microsoft.Playwright.Program.Main(withDeps ? ["install", "--with-deps", "chromium"] : ["install", "chromium"]));
        var baseUrl = Environment.GetEnvironmentVariable(PlaywrightFactAttribute.BaseUrlVariable)!;
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var phone = width <= 400;
        var context = await browser.NewContextAsync(new()
        {
            HasTouch = phone,
            IsMobile = phone,
            ViewportSize = new() { Width = width, Height = phone ? 844 : 900 },
        });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(60_000);
        // NOTE: The test server has no fallback to index.html, so the test serves it for the editor route.
        await page.RouteAsync(baseUrl + "editor", async route =>
            await route.FulfillAsync(new() { Response = await route.FetchAsync(new() { Url = baseUrl }) }));
        await page.GotoAsync(baseUrl + "editor");
        await Opening(page);
        await test(page, baseUrl);
    }

    /// <summary>Waits for the app to boot: the Start here button is on the page.</summary>
    static async Task Opening(IPage page)
    {
        var start = page.Locator("button.start-here-button");
        for (var attempt = 0; ; attempt++)
        {
            try { await start.WaitForAsync(new() { Timeout = 30_000 }); return; }
            catch (TimeoutException) when (attempt < 2) { await page.ReloadAsync(); }
        }
    }
}
