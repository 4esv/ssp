using Microsoft.Playwright;
using Ssp.Web.Sharing;
using Xunit.Abstractions;

namespace Ssp.Web.Tests.Playwright;

/// <summary>
/// The app uses the vendored substrate at wwwroot/substrate: one pixel, two inks, three faces, and controls that are
/// words in brackets. These tests check the shell, the controls, the colours, the titles drawn as cells, and the tap
/// targets on a phone.
/// </summary>
[Collection(PlaywrightCollection.Name)]
public class SubstratePlaywrightTests(ITestOutputHelper output)
{
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(4);

    [PlaywrightFact]
    public Task TheShellLinksTheSubstrateBeforeTheAppSheet() => ShellCore().WaitAsync(Limit);

    [PlaywrightFact]
    public Task EveryToolbarAndPanelBarControlIsABracketControl() => BracketCore().WaitAsync(Limit);

    [PlaywrightFact]
    public Task NoColourOnTheEditorIsOffTheTwoInks() => ColourCore().WaitAsync(Limit);

    [PlaywrightFact]
    public Task APanelTitleIsDrawnAsCellsAfterThePanelOpens() => CellsCore().WaitAsync(Limit);

    [PlaywrightFact]
    public Task EveryTappedControlHasA44PxHitBoxOnAPhone() => TouchCore().WaitAsync(Limit);

    async Task ShellCore()
    {
        await using var session = await Session.Open();
        var page = await session.Home();
        var shell = await page.EvaluateAsync<string[]>("""
            () => {
              const links = [...document.querySelectorAll('link[rel=stylesheet]')].map(l => l.getAttribute('href'));
              const substrate = links.findIndex(h => /^substrate\/css\/index(\.[a-z0-9]+)?\.css$/.test(h));
              const app = links.findIndex(h => /^css\/app(\.[a-z0-9]+)?\.css$/.test(h));
              const out = [];
              if (substrate < 0) out.push('no link to substrate/css/index.css: ' + links.join(', '));
              else if (app >= 0 && substrate > app) out.push('substrate/css/index.css comes after css/app.css');
              if (document.documentElement.dataset.theme !== 'light') out.push('html data-theme is ' + document.documentElement.dataset.theme);
              if (!document.body.classList.contains('substrate')) out.push('body is not .substrate');
              if (!document.querySelector('main.doc')) out.push('no main.doc');
              const family = getComputedStyle(document.querySelector('main')).fontFamily;
              if (!family.startsWith('"Handjet Cells"')) out.push('the page face is ' + family);
              return out;
            }
            """);
        Assert.True(shell.Length == 0, string.Join("; ", shell));
    }

    async Task BracketCore()
    {
        await using var session = await Session.Open();
        var failures = new List<string>();
        foreach (var (width, height) in new[] { (1280, 900), (390, 844) })
        {
            var page = await session.Editor(width, height, phone: width < 600);
            var bad = await page.EvaluateAsync<string[]>(BracketScript);
            output.WriteLine($"{width}x{height}: {bad.Length} controls are not bracket controls");
            failures.AddRange(bad.Select(b => $"{width}x{height}: {b}"));
            await page.CloseAsync();
        }

        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    async Task ColourCore()
    {
        await using var session = await Session.Open();
        var page = await session.Editor(1280, 900);
        var offenders = new SortedSet<string>(StringComparer.Ordinal);
        async Task Sample(string state)
        {
            foreach (var o in await page.EvaluateAsync<string[]>(ColourScript)) offenders.Add(state + ": " + o);
        }

        await Sample("start");
        await page.Locator("button.run").ClickAsync();
        await page.Locator("table.voltages").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await Sample("after Run");
        var tabs = page.Locator("button.dock-tab");
        for (var i = 0; i < await tabs.CountAsync(); i++)
        {
            var tab = tabs.Nth(i);
            var name = await tab.GetAttributeAsync("data-panel") ?? (await tab.InnerTextAsync()).Trim();
            await tab.ClickAsync();
            await page.WaitForTimeoutAsync(300);
            await Sample("panel " + name);
        }

        await page.Locator("button.export-toggle").ClickAsync();
        await Sample("Export menu");
        await page.Keyboard.PressAsync("Escape");
        await page.Keyboard.PressAsync("ControlOrMeta+k");
        await Sample("command palette");

        foreach (var o in offenders) output.WriteLine(o);
        Assert.True(offenders.Count == 0, $"{offenders.Count} colours are off the line from the ink to the ground: " + string.Join("; ", offenders.Take(60)));
    }

    async Task CellsCore()
    {
        await using var session = await Session.Open();
        var page = await session.Editor(1280, 900);
        await page.Locator("button.run").ClickAsync();

        // NOTE: Open a panel with a title that the start layout does not show, then wait for its title to be drawn.
        var tab = page.Locator("button.dock-tab[data-panel=plots]");
        if (await tab.CountAsync() == 0) tab = page.Locator("button.dock-tab", new() { HasTextString = "Plots" });
        var before = await page.EvaluateAsync<int>("() => window.sspCells ? window.sspCells.layouts : -1");
        await tab.First.ClickAsync();
        var title = page.Locator(".dock-panel[data-panel=plots] h2:not(.dock-panel-title)").First;
        await Assertions.Expect(title).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bcells-on\b"), new() { Timeout = 10_000 });
        var after = await page.EvaluateAsync<int>("() => window.sspCells ? window.sspCells.layouts : -1");
        output.WriteLine($"layouts before the panel opened {before}, after {after}");
        Assert.True(before >= 0 && after > before, $"layout() did not run when the panel opened ({before} then {after}).");

        // NOTE: The canvas draws the title, so the DOM text is transparent and the canvas is above the panels.
        var paint = await page.EvaluateAsync<string[]>("""
            () => {
              const canvas = document.querySelector('canvas.cells');
              if (!canvas) return ['no canvas.cells'];
              const out = [];
              const z = Number(getComputedStyle(canvas).zIndex);
              if (!(z > 5)) out.push('canvas.cells z-index is ' + getComputedStyle(canvas).zIndex);
              return out;
            }
            """);
        Assert.True(paint.Length == 0, string.Join("; ", paint));
    }

    async Task TouchCore()
    {
        await using var session = await Session.Open();
        var page = await session.Editor(390, 844, phone: true);
        var failures = new SortedSet<string>(StringComparer.Ordinal);
        async Task Measure(string state)
        {
            foreach (var f in await page.EvaluateAsync<string[]>(HitScript)) failures.Add(state + ": " + f);
        }

        await Measure("editor");
        var panels = page.Locator(".panel-bar button[data-panel]");
        for (var i = 0; i < await panels.CountAsync(); i++)
        {
            var button = panels.Nth(i);
            var name = await button.GetAttributeAsync("data-panel");
            await button.TapAsync();
            await page.WaitForTimeoutAsync(300);
            await Measure("panel " + name);
            await button.TapAsync();
        }

        var home = await session.Home(phone: true);
        foreach (var f in await home.EvaluateAsync<string[]>(HitScript)) failures.Add("home: " + f);

        foreach (var f in failures) output.WriteLine(f);
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    // NOTE: A bracket control has the classes ctl and btn and a label whose ::before is the open bracket.
    const string BracketScript = """
        () => {
          const out = [];
          for (const b of document.querySelectorAll('.schematic-tools button, .panel-bar button')) {
            const name = (b.className || b.tagName) + ' "' + b.textContent.trim().slice(0, 20) + '"';
            if (!b.classList.contains('ctl') || !b.classList.contains('btn')) { out.push(name + ' is not .ctl.btn'); continue; }
            const lbl = b.querySelector(':scope > .lbl');
            if (!lbl) { out.push(name + ' has no .lbl'); continue; }
            const open = getComputedStyle(lbl, '::before').content;
            if (!open.includes('[')) out.push(name + ' label ::before is ' + open);
          }
          return out;
        }
        """;

    // NOTE: The two inks are on one line in RGB, so a tone of the ink (a mix with the ground, or the ink with alpha
    // over the ground) is on that line too. A canvas turns every CSS colour syntax into bytes.
    const string ColourScript = """
        () => {
          const css = getComputedStyle(document.documentElement);
          const ctx = Object.assign(document.createElement('canvas'), { width: 1, height: 1 }).getContext('2d', { willReadFrequently: true });
          const bytes = c => { ctx.clearRect(0, 0, 1, 1); ctx.fillStyle = '#000'; ctx.fillStyle = c; ctx.fillRect(0, 0, 1, 1); return [...ctx.getImageData(0, 0, 1, 1).data]; };
          const ink = bytes(css.getPropertyValue('--ink').trim() || 'rgb(24,24,27)');
          const ground = bytes(css.getPropertyValue('--ground').trim() || 'rgb(228,228,231)');
          const d = [0, 1, 2].map(i => ground[i] - ink[i]);
          const dd = d.reduce((s, v) => s + v * v, 0);
          const onLine = p => {
            const t = [0, 1, 2].reduce((s, i) => s + (p[i] - ink[i]) * d[i], 0) / dd;
            if (t < -0.03 || t > 1.03) return false;
            return Math.hypot(...[0, 1, 2].map(i => p[i] - (ink[i] + t * d[i]))) <= 6;
          };
          const seen = new Set();
          const out = [];
          const visible = el => { const r = el.getBoundingClientRect(); return r.width > 0 && r.height > 0 && r.bottom > 0 && r.right > 0 && r.top < innerHeight && r.left < innerWidth; };
          const name = el => el.tagName.toLowerCase() + (typeof el.className === 'string' && el.className ? '.' + el.className.trim().split(/\s+/).join('.') : '');
          const check = (el, prop, value) => {
            if (!value || value === 'none' || value === 'transparent' || value.startsWith('url(')) return;
            const p = bytes(value);
            if (p[3] === 0 || onLine(p)) return;
            const key = name(el) + ' ' + prop + ' ' + value;
            if (!seen.has(key)) { seen.add(key); out.push(key); }
          };
          for (const el of [document.body, ...document.body.querySelectorAll('*')]) {
            if (el.closest('canvas.cells') || !visible(el)) continue;
            const s = getComputedStyle(el);
            if (s.visibility === 'hidden' || s.opacity === '0') continue;
            check(el, 'color', s.color);
            check(el, 'background-color', s.backgroundColor);
            for (const side of ['Top', 'Right', 'Bottom', 'Left'])
              if (parseFloat(s['border' + side + 'Width']) > 0 && s['border' + side + 'Style'] !== 'none') check(el, 'border-' + side.toLowerCase(), s['border' + side + 'Color']);
            if (s.outlineStyle !== 'none' && parseFloat(s.outlineWidth) > 0) check(el, 'outline', s.outlineColor);
            if (el instanceof SVGElement) { check(el, 'fill', s.fill); check(el, 'stroke', s.stroke); }
          }
          return out;
        }
        """;

    // NOTE: The hit box is what a tap reaches: step out from the centre while elementFromPoint still lands on the
    // control. A pad drawn by a pseudo-element counts, and a control covered at its centre is not tappable now.
    const string HitScript = """
        () => {
          const out = [];
          const targets = new Set();
          for (const el of document.querySelectorAll('button, a[href], select, textarea, input:not([type=checkbox]):not([type=radio]):not([type=file]), label:has(> input[type=checkbox]), label:has(input[type=file])'))
            targets.add(el);
          const inView = r => r.width > 0 && r.height > 0 && r.left >= 0 && r.top >= 0 && r.right <= innerWidth && r.bottom <= innerHeight;
          // A control that a scrolling row cuts is reached after a scroll, so it counts only when it shows whole. The
          // row's scroll bar is inside the box that is kept, so a bar over a control still fails it.
          const whole = (el, r) => {
            for (let p = el.parentElement; p; p = p.parentElement) {
              const s = getComputedStyle(p);
              if (s.overflowX === 'visible' && s.overflowY === 'visible') continue;
              const q = p.getBoundingClientRect();
              if (r.left < q.left + p.clientLeft - 0.5 || r.right > q.right - parseFloat(s.borderRightWidth) + 0.5 || r.top < q.top + p.clientTop - 0.5 || r.bottom > q.bottom - parseFloat(s.borderBottomWidth) + 0.5) return false;
            }
            return true;
          };
          for (const el of targets) {
            const r = el.getBoundingClientRect();
            if (!inView(r) || !whole(el, r) || getComputedStyle(el).visibility === 'hidden') continue;
            const owner = e => !!e && (e === el || el.contains(e) || (e.tagName === 'INPUT' && e.closest('label') === el));
            const own = (x, y) => owner(document.elementFromPoint(x, y));
            const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
            if (!own(cx, cy)) continue;
            const reach = (dx, dy) => { let n = 0; while (n < 60 && own(cx + dx * (n + 1), cy + dy * (n + 1))) n++; return n; };
            const w = reach(-1, 0) + reach(1, 0) + 1, h = reach(0, -1) + reach(0, 1) + 1;
            if (w < 44 || h < 44) {
              const label = (el.getAttribute('aria-label') || el.textContent || el.className || el.tagName).trim().replace(/\s+/g, ' ').slice(0, 24);
              out.push(el.tagName.toLowerCase() + ' "' + label + '" is ' + w + 'x' + h);
            }
          }
          return out;
        }
        """;

    sealed class Session : IAsyncDisposable
    {
        IPlaywright playwright = null!;
        IBrowser browser = null!;
        IBrowserContext? desk;
        IBrowserContext? phone;
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

        async Task<IPage> NewPage(bool onPhone)
        {
            // NOTE: The pages of one kind share a context, so the next page takes the app from the cache.
            // NOTE: A phone has three device pixels to the CSS pixel. The substrate sizes its pixel from the screen and that
            // ratio, so a phone at ratio 1 draws every control three times as large and hides a small hit box.
            IBrowserContext context = onPhone
                ? phone ??= await browser.NewContextAsync(new() { HasTouch = true, IsMobile = true, DeviceScaleFactor = 3, ViewportSize = new() { Width = 390, Height = 844 } })
                : desk ??= await browser.NewContextAsync();
            var page = await context.NewPageAsync();
            await page.AddInitScriptAsync(HelpSeen.Script);
            page.SetDefaultTimeout(60_000);
            await page.RouteAsync(baseUrl + "editor", async route =>
                await route.FulfillAsync(new() { Response = await route.FetchAsync(new() { Url = baseUrl }) }));
            return page;
        }

        public async Task<IPage> Editor(int width, int height, bool phone = false)
        {
            var page = await NewPage(phone);
            await page.SetViewportSizeAsync(width, height);
            await page.GotoAsync(baseUrl + "editor#" + hash);
            await page.Locator(".schematic[data-view]").WaitForAsync();
            return page;
        }

        public async Task<IPage> Home(bool phone = false)
        {
            var page = await NewPage(phone);
            await page.GotoAsync(baseUrl);
            await page.Locator("ul.gallery a").First.WaitForAsync();
            return page;
        }

        public async ValueTask DisposeAsync()
        {
            await browser.DisposeAsync();
            playwright.Dispose();
        }
    }
}
