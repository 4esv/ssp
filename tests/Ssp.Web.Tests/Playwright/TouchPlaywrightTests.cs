using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Ssp.Web.Sharing;
using Xunit.Abstractions;

namespace Ssp.Web.Tests.Playwright;

// NOTE: A phone with a finger and no mouse (#176). Every control the test taps must be 44 x 44 CSS px or more, so no tap
// needs a precise position. The test prints the size of each target and the findings.
[Collection(PlaywrightCollection.Name)]
public partial class TouchPlaywrightTests(ITestOutputHelper output)
{
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(4);
    const double Finger = 44;

    [PlaywrightFact]
    public Task A_finger_builds_a_circuit_with_taps_only() => BuildCore().WaitAsync(Limit);

    async Task BuildCore()
    {
        await using var phone = await Phone.Open();
        var page = phone.Page;
        var failures = new List<string>();

        async Task Tap(ILocator target, string name)
        {
            await target.ScrollIntoViewIfNeededAsync();
            var box = (await target.BoundingBoxAsync())!;
            var under = await page.EvaluateAsync<string>(string.Create(CultureInfo.InvariantCulture,
                $"() => {{ const e = document.elementFromPoint({box.X + box.Width / 2}, {box.Y + box.Height / 2}); return e ? `${{e.tagName}} ${{e.getAttribute('class')}} ${{e.dataset.ref ?? ''}}` : 'nothing'; }}"));
            output.WriteLine($"tap {name}: {box.Width:0.#} x {box.Height:0.#}, lands on {under}");
            if (box.Width < Finger - 0.5 || box.Height < Finger - 0.5) failures.Add($"{name} is {box.Width:0.#} x {box.Height:0.#} px");
            // NOTE: The finger lands at the centre of the target, whatever lies on top there.
            await page.Touchscreen.TapAsync((float)(box.X + box.Width / 2), (float)(box.Y + box.Height / 2));
        }

        // NOTE: A pin is hit through the transparent hit circle under it.
        ILocator PinHit(string reference, int pin) =>
            page.Locator($"svg.schematic-pins circle.pin-hit[data-ref=\"{reference}\"][data-pin=\"{pin}\"]");

        async Task Next(string kind, params string[] ways)
        {
            var dots = page.Locator("g.pin-add");
            await dots.First.WaitForAsync();
            var free = await dots.EvaluateAllAsync<string[]>("ds => ds.map(d => d.dataset.dir)");
            var way = ways.FirstOrDefault(free.Contains) ?? free[0];
            if (way != ways[0]) failures.Add($"{kind}: the + {ways[0]} is not there, so the test took {way}");
            await Tap(page.Locator($"g.pin-add[data-dir=\"{way}\"]"), $"+ {way}");
            await Tap(page.Locator($".pin-menu button[data-kind=\"{kind}\"]"), $"picker {kind}");
        }

        await Tap(page.Locator("button[data-action=new]"), "New");
        await Tap(page.Locator(".palette-item[data-kind=source]"), "palette source");
        await page.Locator("circle.pin[data-ref=V1]").First.WaitForAsync();
        var right = await page.Locator("circle.pin[data-ref=V1]").EvaluateAllAsync<int>(
            "ps => +ps.reduce((a, b) => +b.getAttribute('cx') > +a.getAttribute('cx') ? b : a).dataset.pin");
        await Tap(PinHit("V1", right), $"V1 pin {right + 1}");
        await Next("jack-in", "right", "down", "up", "left");
        await Next("capacitor", "right", "down", "up", "left");
        await Next("npn", "right", "down", "up", "left");

        // NOTE: The collector is pin 1 of a transistor.
        var transistor = await page.Locator("circle.pin[data-ref^=Q]").First.GetAttributeAsync("data-ref");
        // NOTE: A tap on the active pin clears it, so the test taps the collector only when it is not the active pin.
        var collector = page.Locator($"circle.pin[data-ref=\"{transistor}\"][data-pin=\"0\"]");
        if (!(await collector.GetAttributeAsync("class"))!.Split(' ').Contains("active")) await Tap(PinHit(transistor!, 0), $"{transistor} collector");
        await Next("resistor", "up");
        await Next("battery", "up");

        var netlist = await Netlist(page);
        output.WriteLine(netlist);
        var elements = netlist.Split('\n').Select(l => l.Trim()).Where(l => Element().IsMatch(l)).ToList();
        var jacks = await page.Locator("g.jack[data-jack=in]").CountAsync();
        output.WriteLine($"elements {elements.Count} [{string.Join(" | ", elements)}], jack in {jacks}");
        Assert.Equal(5, elements.Count);
        Assert.Equal(1, jacks);
        Assert.Contains(elements, e => e.StartsWith('C'));
        Assert.Contains(elements, e => e.StartsWith('Q'));
        Assert.Contains(elements, e => e.StartsWith('R'));
        Assert.Equal(2, elements.Count(e => e.StartsWith('V')));

        // NOTE: Each part action button is a target too.
        await page.Locator("svg.schematic-pins rect.part[data-ref^=C]").TapAsync();
        foreach (var action in await page.Locator(".part-action").EvaluateAllAsync<string[]>("bs => bs.map(b => b.dataset.action)"))
        {
            var box = (await page.Locator($".part-action[data-action={action}]").BoundingBoxAsync())!;
            output.WriteLine($"part action {action}: {box.Width:0.#} x {box.Height:0.#}");
            if (box.Width < Finger - 0.5 || box.Height < Finger - 0.5) failures.Add($"part action {action} is {box.Width:0.#} x {box.Height:0.#} px");
        }

        var wide = await page.EvaluateAsync<int[]>("() => [document.scrollingElement.scrollWidth, innerWidth]");
        output.WriteLine($"page {wide[0]} px wide in {wide[1]} px");
        if (wide[0] > wide[1]) failures.Add($"the page scrolls sideways: {wide[0]} px in {wide[1]} px");

        output.WriteLine($"findings: {failures.Count}");
        foreach (var f in failures) output.WriteLine("  " + f);
        Assert.Empty(failures);
    }

    [PlaywrightFact]
    public Task A_long_press_on_a_part_opens_its_actions_and_on_the_canvas_does_nothing() => LongPressCore().WaitAsync(Limit);

    async Task LongPressCore()
    {
        await using var phone = await Phone.Open();
        var page = phone.Page;
        await page.Locator("button[data-action=new]").TapAsync();
        await page.Locator(".palette-item[data-kind=resistor]").TapAsync();
        await page.Locator("svg.schematic-pins rect.part[data-ref=R1]").WaitForAsync();

        // NOTE: Clear the selection, so only the long press can open the actions.
        var canvas = (await page.Locator(".schematic-editor .schematic").BoundingBoxAsync())!;
        await page.Touchscreen.TapAsync((float)(canvas.X + 30), (float)(canvas.Y + canvas.Height - 30));
        await Assertions.Expect(page.Locator(".part-action")).ToHaveCountAsync(0);

        var before = await Netlist(page);
        var part = (await page.Locator("svg.schematic-pins rect.part[data-ref=R1]").BoundingBoxAsync())!;
        await phone.Hold(part.X + part.Width / 2, part.Y + part.Height / 2, drift: 8);
        await Assertions.Expect(page.Locator(".part-action[data-action=delete]")).ToBeVisibleAsync(new() { Timeout = 5_000 });
        Assert.Equal(before, await Netlist(page));
        var after = (await page.Locator("svg.schematic-pins rect.part[data-ref=R1]").BoundingBoxAsync())!;
        Assert.Equal((part.X, part.Y), (after.X, after.Y));

        // NOTE: A long press on empty canvas adds, moves and deletes nothing, and the view stays.
        var view = await page.Locator(".schematic-editor .schematic").GetAttributeAsync("style");
        await phone.Hold(canvas.X + 30, canvas.Y + canvas.Height - 30, drift: 8);
        await page.WaitForTimeoutAsync(300);
        Assert.Equal(before, await Netlist(page));
        Assert.Equal(view, await page.Locator(".schematic-editor .schematic").GetAttributeAsync("style"));
        var selected = await page.EvaluateAsync<string>("() => String(getSelection())");
        Assert.Equal("", selected);
    }

    [PlaywrightFact]
    public Task The_palette_scrolls_with_one_finger_and_the_canvas_stays() => PaletteCore().WaitAsync(Limit);

    async Task PaletteCore()
    {
        await using var phone = await Phone.Open();
        var page = phone.Page;
        await page.Locator("button[data-action=new]").TapAsync();
        await page.Locator(".palette-item[data-kind=resistor]").TapAsync();
        await page.Locator("svg.schematic-pins rect.part[data-ref=R1]").WaitForAsync();
        var before = await Netlist(page);
        var view = await page.Locator(".schematic-editor .schematic").GetAttributeAsync("style");

        var palette = (await page.Locator(".palette").BoundingBoxAsync())!;
        var y = palette.Y + palette.Height / 2;
        await phone.Swipe(palette.X + palette.Width - 20, y, palette.X + 40, y);
        await page.WaitForTimeoutAsync(500);

        var scrolled = await page.Locator(".palette").EvaluateAsync<double>("p => p.scrollLeft");
        var wide = await page.EvaluateAsync<int[]>("() => [document.scrollingElement.scrollWidth, innerWidth, scrollX]");
        output.WriteLine($"palette scrollLeft {scrolled:0}, page {wide[0]} in {wide[1]}, scrollX {wide[2]}");
        Assert.True(scrolled > 50, $"The palette moved {scrolled:0} px.");
        Assert.Equal(before, await Netlist(page));
        Assert.Equal(view, await page.Locator(".schematic-editor .schematic").GetAttributeAsync("style"));
        Assert.True(wide[0] <= wide[1] && wide[2] == 0, $"The page scrolls sideways: {wide[0]} px in {wide[1]} px, at {wide[2]}.");
    }

    [PlaywrightFact]
    public Task A_tap_on_a_part_of_the_fuzz_selects_it_and_never_hits_the_palette() => FuzzTapCore().WaitAsync(Limit);

    // NOTE: #258. On the Transistor fuzz the tap meant for R4 added a resistor. The phone moved the tap onto the Duplicate button of R2,
    // whose edge lay at the centre of R4. The test prints each box and what lies at each part centre, before the finger-down, while
    // the finger is down, and after the finger-up. Each tap must reach the part itself, and no control may move while the finger is down.
    const string Layout = """
        () => {
            const box = e => { const r = e.getBoundingClientRect(); return `${Math.round(r.x)},${Math.round(r.y)} ${Math.round(r.width)}x${Math.round(r.height)}`; };
            const at = e => e ? `${e.tagName}.${e.getAttribute('class')}.${e.dataset.ref ?? e.dataset.action ?? e.dataset.kind ?? ''}` : 'nothing';
            const hint = document.querySelector('.schematic-hint').getBoundingClientRect();
            return {
                controls: [
                    `palette ${box(document.querySelector('.palette'))}`,
                    `canvas ${box(document.querySelector('.schematic-editor .schematic'))}`,
                    `hint at ${Math.round(hint.x)},${Math.round(hint.y)}`,
                    ...[...document.querySelectorAll('.part-action')].map(b => `${b.dataset.action} ${box(b)}`),
                ],
                parts: ['R2', 'R4', 'R6'].map(ref => {
                    const e = document.querySelector(`.schematic-pins rect.part[data-ref=${ref}]`);
                    const r = e.getBoundingClientRect();
                    const u = document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2);
                    return `${ref} ${box(e)} -> ${u === e ? 'itself' : at(u)}`;
                }),
            };
        }
        """;

    const string Listen = """
        () => {
            window.tapLog = [];
            for (const t of ['pointerdown', 'click'])
                document.addEventListener(t, e => tapLog.push(`${t} ${e.target.dataset?.ref ?? `${e.target.tagName}.${e.target.getAttribute('class')}.${e.target.dataset?.action ?? e.target.dataset?.kind ?? ''}`}`), true);
        }
        """;

    sealed class Snapshot
    {
        public string[] Controls { get; set; } = [];
        public string[] Parts { get; set; } = [];
    }

    async Task FuzzTapCore()
    {
        var fuzz = File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "library", "fuzz-transistor-diode.cir"));
        await using var phone = await Phone.Open("#" + ShareCodec.Encode(fuzz));
        var page = phone.Page;
        await page.Locator(".schematic[data-view]").WaitForAsync();
        var before = await Parts(page);
        await page.Locator("button[data-tool=select]").TapAsync();
        await page.WaitForTimeoutAsync(300);
        await page.EvaluateAsync(Listen);
        var failures = new List<string>();

        async Task<Snapshot> Snap(string when)
        {
            var snap = await page.EvaluateAsync<Snapshot>(Layout);
            output.WriteLine($"{when}: {string.Join(" | ", snap.Controls)} | {string.Join(" | ", snap.Parts)}");
            failures.AddRange(snap.Parts.Where(p => !p.EndsWith("-> itself")).Select(p => $"{when}: {p}"));
            return snap;
        }

        foreach (var reference in new[] { "R2", "R4", "R6" })
        {
            var start = await Snap($"before tap {reference}");
            var part = (await page.Locator($".schematic-pins rect.part[data-ref={reference}]").BoundingBoxAsync())!;
            await phone.Down(part.X + part.Width / 2, part.Y + part.Height / 2);
            await page.WaitForTimeoutAsync(100);
            var down = await Snap($"finger down on {reference}");
            await phone.Up();
            await page.WaitForTimeoutAsync(400);
            var up = await Snap($"after tap {reference}");
            if (!start.Controls.SequenceEqual(down.Controls)) failures.Add($"a control moved at the finger-down on {reference}");
            // NOTE: After the finger-up the selection changes, so the action row may move. The palette, the canvas and the hint stay.
            if (!down.Controls.Take(3).SequenceEqual(up.Controls.Take(3))) failures.Add($"a control moved at the finger-up on {reference}");
            var events = await page.EvaluateAsync<string[]>("() => tapLog.splice(0)");
            output.WriteLine($"tap {reference}: events [{string.Join(", ", events)}], selected [{string.Join(", ", await Selected(page))}]");
            if (!events.SequenceEqual([$"pointerdown {reference}", $"click {reference}"])) failures.Add($"the tap on {reference} reached [{string.Join(", ", events)}]");
        }

        output.WriteLine($"findings: {failures.Count}");
        foreach (var f in failures) output.WriteLine("  " + f);
        Assert.Equal(["R2", "R4", "R6"], await Selected(page));
        Assert.Equal(before, await Parts(page));
        Assert.Empty(failures);
    }

    static Task<string[]> Parts(IPage page) =>
        page.EvaluateAsync<string[]>("() => [...new Set([...document.querySelectorAll('.schematic-pins rect.part[data-ref]')].map(e => e.dataset.ref))].sort()");

    static Task<string[]> Selected(IPage page) =>
        page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('.schematic-pins rect.part.selected')].map(e => e.dataset.ref).sort()");

    static Task<string> Netlist(IPage page) => page.Locator("textarea[aria-label=Netlist]").InputValueAsync();

    [GeneratedRegex(@"^[RCQVDL]\w*\s", RegexOptions.IgnoreCase)]
    private static partial Regex Element();

    sealed class Phone : IAsyncDisposable
    {
        IPlaywright playwright = null!;
        IBrowser browser = null!;
        ICDPSession cdp = null!;
        public IPage Page { get; private set; } = null!;

        public static async Task<Phone> Open(string hash = "")
        {
            var withDeps = Environment.GetEnvironmentVariable("SSP_PLAYWRIGHT_WITH_DEPS") == "1";
            Assert.Equal(0, Microsoft.Playwright.Program.Main(withDeps ? ["install", "--with-deps", "chromium"] : ["install", "chromium"]));
            var baseUrl = Environment.GetEnvironmentVariable(PlaywrightFactAttribute.BaseUrlVariable)!;
            var phone = new Phone { playwright = await Microsoft.Playwright.Playwright.CreateAsync() };
            phone.browser = await phone.playwright.Chromium.LaunchAsync();
            var context = await phone.browser.NewContextAsync(new()
            {
                HasTouch = true,
                IsMobile = true,
                DeviceScaleFactor = 3,
                ViewportSize = new() { Width = 390, Height = 844 },
            });
            phone.Page = await context.NewPageAsync();
            phone.Page.SetDefaultTimeout(60_000);
            await phone.Page.RouteAsync(baseUrl + "editor", async route =>
                await route.FulfillAsync(new() { Response = await route.FetchAsync(new() { Url = baseUrl }) }));
            // NOTE: The test server sometimes drops a connection on the first load, so a page with no editor loads again.
            for (var attempt = 0; ; attempt++)
            {
                // NOTE: A second GotoAsync to the same address with a hash does not load the page again, so a retry reloads.
                if (attempt == 0) await phone.Page.GotoAsync(baseUrl + "editor" + hash);
                else await phone.Page.ReloadAsync();
                try
                {
                    await phone.Page.Locator("button[data-action=new]").WaitForAsync(new() { Timeout = 30_000 });
                    break;
                }
                catch (TimeoutException) when (attempt < 2) { }
            }
            phone.cdp = await context.NewCDPSessionAsync(phone.Page);
            return phone;
        }

        Task Touch(string type, params (double X, double Y)[] at) => Touch(type, 1, at);

        Task Touch(string type, double radius, params (double X, double Y)[] at) =>
            cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
            {
                ["type"] = type,
                ["touchPoints"] = at.Select(p => new Dictionary<string, object> { ["x"] = p.X, ["y"] = p.Y, ["radiusX"] = radius, ["radiusY"] = radius }).ToArray(),
            });

        // NOTE: A fingertip is about 8 px in radius, and the phone moves the tap onto a button within it.
        public Task Down(double x, double y) => Touch("touchStart", 8, (x, y));

        public Task Up() => Touch("touchEnd");

        // NOTE: A finger never stays still, so the hold drifts a few pixels.
        public async Task Hold(double x, double y, double drift)
        {
            await Touch("touchStart", (x, y));
            await Page.WaitForTimeoutAsync(150);
            await Touch("touchMove", (x + drift, y + drift));
            await Page.WaitForTimeoutAsync(650);
            await Touch("touchEnd");
        }

        public async Task Swipe(double fromX, double fromY, double toX, double toY)
        {
            await Touch("touchStart", (fromX, fromY));
            const int Steps = 12;
            for (var i = 1; i <= Steps; i++)
            {
                await Touch("touchMove", (fromX + (toX - fromX) * i / Steps, fromY + (toY - fromY) * i / Steps));
                await Page.WaitForTimeoutAsync(16);
            }
            await Touch("touchEnd");
        }

        public async ValueTask DisposeAsync()
        {
            await browser.DisposeAsync();
            playwright.Dispose();
        }
    }
}
