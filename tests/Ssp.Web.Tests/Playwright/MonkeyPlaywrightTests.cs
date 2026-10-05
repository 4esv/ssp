using System.Diagnostics;
using Microsoft.Playwright;
using Ssp.Web.Sharing;
using Xunit.Abstractions;

namespace Ssp.Web.Tests.Playwright;

// NOTE: #271: a monkey drives the real editor with random actions from fixed seeds, so no sequence of edits takes the app down.
// After every action the error bar is hidden, no console error or page error came, and the page answers a frame within 2 s.
// CI runs 30 seeds. Set SSP_MONKEY_SEEDS=500 for a long local run, and SSP_MONKEY_FIRST to start at another seed.
[Collection(PlaywrightCollection.Name)]
public class MonkeyPlaywrightTests(ITestOutputHelper output)
{
    const int Actions = 200;

    static int Env(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var n) && n > 0 ? n : fallback;

    [PlaywrightFact]
    public async Task NoSequenceOfEditorActionsTakesTheAppDown()
    {
        var (first, seeds) = (Env("SSP_MONKEY_FIRST", 1), Env("SSP_MONKEY_SEEDS", 30));
        await Core(first, seeds).WaitAsync(TimeSpan.FromMinutes(2 + seeds));
    }

    static readonly string[] Starters = Directory.GetFiles(Path.Combine(RepoPaths.Root, "circuits", "library"), "*.cir").Order().ToArray();

    async Task Core(int first, int seeds)
    {
        Assert.Equal(0, Microsoft.Playwright.Program.Main(["install", "chromium"]));
        var baseUrl = Environment.GetEnvironmentVariable(PlaywrightFactAttribute.BaseUrlVariable)!;
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var clock = Stopwatch.StartNew();
        // NOTE: Each seed has its own context and its own Random, so a seed does the same actions also when seeds run side by side.
        var results = new (Monkey Monkey, string? Failure)[seeds];
        await Parallel.ForAsync(0, seeds, new ParallelOptions { MaxDegreeOfParallelism = Env("SSP_MONKEY_PARALLEL", 3) }, async (i, _) =>
        {
            // NOTE: The test server of scripts/playwright.sh has a short listen queue and drops a download now and then. That is not an
            // editor crash, so a seed that failed only by a dropped framework file runs again, and if it drops again it is inconclusive.
            var monkey = new Monkey(first + i);
            var failure = await monkey.Run(browser, baseUrl);
            for (var again = 0; again < 2 && IsDroppedDownload(failure); again++)
            {
                monkey = new Monkey(first + i);
                failure = await monkey.Run(browser, baseUrl);
            }
            results[i] = (monkey, failure);
        });
        foreach (var (monkey, failure) in results)
        {
            output.WriteLine($"seed {monkey.Seed}: {monkey.Start}, {monkey.Done} actions, {(failure is null ? "clean" : "FAILED")}");
            if (failure is not null) output.WriteLine(failure);
        }
        var inconclusive = results.Count(r => IsDroppedDownload(r.Failure));
        if (inconclusive > 0) output.WriteLine($"{inconclusive} seed(s) were inconclusive: the server dropped a download three times. They are not counted as crashes.");
        var failures = results.Where(r => r.Failure is not null && !IsDroppedDownload(r.Failure)).Select(r => r.Failure).ToList();
        var mix = results.SelectMany(r => r.Monkey.Mix).GroupBy(m => m.Key).Select(g => (g.Key, Count: g.Sum(m => m.Value)));
        output.WriteLine($"{results.Count(r => r.Failure is null)} of {seeds} seeds clean, {results.Sum(r => r.Monkey.Done)} actions, {clock.Elapsed.TotalSeconds:0} s. Mix: "
            + string.Join(", ", mix.OrderByDescending(m => m.Count).Select(m => $"{m.Key} {m.Count}")));
        Assert.True(failures.Count == 0, string.Join("\n\n", failures));
    }

    static bool IsDroppedDownload(string? failure) =>
        failure is not null && failure.Contains("net::ERR_CONNECTION_RESET", StringComparison.Ordinal) && failure.Contains("/_framework/", StringComparison.Ordinal);

    sealed class Monkey(int seed)
    {
        public readonly Dictionary<string, int> Mix = [];
        readonly Random rng = new(seed);
        readonly List<string> log = [];
        readonly List<string> errors = [];
        IPage page = null!;
        string before = "";
        public int Seed => seed;
        public string Start { get; private set; } = "";
        public int Done { get; private set; }

        static readonly string[] Values = ["10k", "4.7u", "100n", "1meg", "0", "-5", "abc", "", "1e99", "2N3904", "22p", "1.5.5", "  ", "k", "100", "0.0001"];
        static readonly string[] Names = ["out", "in", "vcc", "0", "gnd", "a b", "n1", "x-y", "", "bias", "OUT", "1"];
        static readonly string[] Keys = ["r", "c", "q", "d", "p", "b", "s", "i", "o", "l", "n", "g", "w", "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight",
            "Tab", "Escape", "Delete", "h", "v", "x", "+", "-", "0", "ControlOrMeta+a", "ControlOrMeta+z", "ControlOrMeta+y", "ControlOrMeta+Enter"];

        public async Task<string?> Run(IBrowser browser, string baseUrl)
        {
            var starter = rng.Next(Starters.Length + 1);
            var hash = starter < Starters.Length ? "#" + ShareCodec.Encode(File.ReadAllText(Starters[starter])) : "";
            Start = starter < Starters.Length ? Path.GetFileName(Starters[starter]) : "New";
            for (var attempt = 1; ; attempt++)
            {
                var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1456, Height = 797 } });
                try
                {
                    errors.Clear();
                    page = await context.NewPageAsync();
                    page.SetDefaultTimeout(1_000);
                    page.Console += (_, m) => { if (m.Type == "error") errors.Add("console.error: " + m.Text); };
                    page.PageError += (_, e) => errors.Add("page error: " + e);
                    page.RequestFailed += (_, r) => errors.Add("request failed: " + r.Url + " " + r.Failure);
                    await page.RouteAsync(baseUrl + "editor", async route => await route.FulfillAsync(new() { Response = await route.FetchAsync(new() { Url = baseUrl }) }));
                    // NOTE: The test server (python http.server) can reset a connection while the app loads. That is not the app, so the load is tried again.
                    if (!await Load(baseUrl + "editor" + hash) || errors.Any(e => e.Contains("net::ERR_")))
                    {
                        if (attempt < 3) continue;
                        return $"seed {seed}: the editor did not load in 3 tries. " + string.Join(" | ", errors);
                    }
                    if (await Check() is { } early) return Report(early);
                    for (Done = 0; Done < Actions; Done++)
                    {
                        var name = await Act();
                        log.Add($"{Done + 1}: {name}");
                        var kind = name.Contains('(') || name.StartsWith("skipped") ? name.Split(' ')[0] + " no-op" : name.Split(' ')[0];
                        Mix[kind] = Mix.GetValueOrDefault(kind) + 1;
                        if (await Check() is { } problem) return Report(problem);
                    }
                    return null;
                }
                finally
                {
                    await context.CloseAsync();
                }
            }
        }

        async Task<bool> Load(string url)
        {
            try
            {
                await page.GotoAsync(url, new() { Timeout = 60_000 });
                await page.Locator(".schematic-editor").WaitForAsync(new() { Timeout = 60_000 });
                if (url.Contains('#')) await page.Locator(".schematic[data-view]").WaitForAsync(new() { Timeout = 60_000 });
                else await Click("[data-action=new]");
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        string Report(string problem) =>
            $"seed {seed} ({Start}) failed after {Done + 1} actions: {problem}\nlast actions:\n  " + string.Join("\n  ", log.TakeLast(10))
            + "\nnetlist before the last action:\n" + before;

        // The page answers a frame within 2 s and shows no error bar. It also keeps the netlist, for the report of the next action.
        async Task<string?> Check()
        {
            try
            {
                var state = await page.EvaluateAsync<string[]>("""
                    async () => {
                        const alive = await Promise.race([new Promise(r => requestAnimationFrame(() => r(true))), new Promise(r => setTimeout(() => r(false), 2000))]);
                        const bar = document.getElementById('blazor-error-ui');
                        return [String(alive), String(!!bar && getComputedStyle(bar).display !== 'none'), document.querySelector('textarea[aria-label=Netlist]')?.value ?? ''];
                    }
                    """);
                if (state[0] != "true") return "no animation frame within 2 s";
                if (state[1] == "true") return "#blazor-error-ui is shown. " + string.Join(" | ", errors);
                if (errors.Count == 0) before = state[2];
            }
            catch (PlaywrightException e)
            {
                return "the page does not answer: " + e.Message;
            }
            return errors.Count > 0 ? string.Join(" | ", errors) : null;
        }

        T Pick<T>(IReadOnlyList<T> items) => items[rng.Next(items.Count)];

        // The centres of the visible elements that match, in client pixels.
        Task<double[][]> Spots(string selector) => page.EvaluateAsync<double[][]>("""
            s => [...document.querySelectorAll(s)].map(e => e.getBoundingClientRect())
                .filter(r => r.width > 0 && r.height > 0 && r.bottom > 0 && r.right > 0 && r.top < innerHeight && r.left < innerWidth)
                .map(r => [r.left + r.width / 2, r.top + r.height / 2])
            """, selector);

        async Task<bool> Click(string selector)
        {
            var spots = await Spots(selector);
            if (spots.Length == 0) return false;
            var s = Pick(spots);
            await page.Mouse.ClickAsync((float)s[0], (float)s[1]);
            return true;
        }

        async Task<double[]> CanvasPoint()
        {
            var r = await page.EvaluateAsync<double[]?>("() => { const r = document.querySelector('.schematic')?.getBoundingClientRect(); return r ? [r.left, r.top, r.width, r.height] : null; }");
            if (r is null) return [700, 400];
            return [r[0] + 10 + rng.NextDouble() * Math.Max(1, r[2] - 20), r[1] + 10 + rng.NextDouble() * Math.Max(1, r[3] - 20)];
        }

        async Task Drag(double[] from, double[] to)
        {
            await page.Mouse.MoveAsync((float)from[0], (float)from[1]);
            await page.Mouse.DownAsync();
            await page.Mouse.MoveAsync((float)to[0], (float)to[1], new() { Steps = 4 });
            await page.Mouse.UpAsync();
        }

        async Task FocusCanvas() => await page.EvaluateAsync("() => document.querySelector('.schematic')?.focus()");

        async Task<string> Act()
        {
            try
            {
                return await ActCore();
            }
            catch (Exception e) when (e is TimeoutException or PlaywrightException)
            {
                return "skipped: " + e.GetType().Name;
            }
        }

        async Task<string> ActCore()
        {
            switch (rng.Next(24))
            {
                case 0:
                case 1:
                    var kind = Pick(await page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('.palette-item[data-kind]')].map(e => e.dataset.kind)"));
                    await Click($".palette-item[data-kind='{kind}']");
                    return "palette " + kind;
                case 2:
                case 3:
                {
                    if (!await Click(".schematic-pins circle.pin-hit")) return "pin (none)";
                    if (!await Click(".pin-add")) return "pin (no +)";
                    var items = await page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('.pin-menu button')].map(e => e.dataset.kind ?? e.dataset.action)");
                    if (items.Length == 0) return "pin +";
                    var item = Pick(items);
                    await Click($".pin-menu button[data-{(item is "wire" or "ground" or "connect" ? "action" : "kind")}='{item}']");
                    if (item == "connect") await Click(".schematic-pins circle.pin-hit");
                    return "pin + " + item;
                }
                case 4:
                {
                    await Click(".schematic-pins circle.pin-hit");
                    await FocusCanvas();
                    var keys = Enumerable.Range(0, 1 + rng.Next(3)).Select(_ => Pick(Keys)).ToList();
                    foreach (var key in keys) await page.Keyboard.PressAsync(key);
                    return "keys " + string.Join(",", keys);
                }
                case 5:
                case 6:
                    return await Click(".schematic-pins rect.part[data-ref]") ? "tap part" : "tap part (none)";
                case 7:
                case 8:
                {
                    if (await Spots(".part-action") is { Length: 0 }) await Click(".schematic-pins rect.part[data-ref]");
                    var actions = await page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('.part-action')].map(e => e.dataset.action).filter(a => a !== 'save-block')");
                    if (actions.Length == 0) return "part action (none)";
                    var action = Pick(actions);
                    await Click($".part-action[data-action='{action}']");
                    return "part action " + action;
                }
                case 9:
                {
                    var parts = await Spots(".schematic-pins rect.part[data-ref]");
                    if (parts.Length == 0) return "drag (none)";
                    var from = Pick(parts);
                    await Drag(from, [from[0] + rng.Next(-150, 150), from[1] + rng.Next(-150, 150)]);
                    return "drag part";
                }
                case 10:
                {
                    if (await page.EvaluateAsync<string?>("() => document.querySelector('[data-tool=select]')?.getAttribute('aria-pressed')") != "true")
                        await Click("[data-tool=select]");
                    await Drag(await CanvasPoint(), await CanvasPoint());
                    await FocusCanvas();
                    var key = Pick(new[] { "ControlOrMeta+c", "ControlOrMeta+x", "ControlOrMeta+v", "Delete" });
                    await page.Keyboard.PressAsync(key);
                    if (rng.Next(2) == 0) await Click("[data-tool=select]");
                    return "box select " + key;
                }
                case 11:
                    if (!await Click("[data-action=paste]")) { await FocusCanvas(); await page.Keyboard.PressAsync("ControlOrMeta+v"); }
                    return "paste";
                case 12:
                    return await Click(rng.Next(2) == 0 ? "button.undo" : "button.redo") ? "undo/redo" : "undo/redo (none)";
                case 13:
                {
                    if (await Spots("input.value-edit:not(.net-name-edit)") is { Length: 0 }) await Click(".schematic-pins rect.part[data-ref]");
                    var box = page.Locator("input.value-edit:not(.net-name-edit)");
                    if (await box.CountAsync() == 0) return "value (none)";
                    var value = Pick(Values);
                    await box.FillAsync(value);
                    await box.PressAsync(Pick(new[] { "Enter", "Enter", "ArrowUp", "ArrowDown", "Escape" }));
                    return $"value '{value}'";
                }
                case 14:
                {
                    var knobs = await Spots(".knob");
                    if (knobs.Length == 0) return "knob (none)";
                    var k = Pick(knobs);
                    await Drag(k, [k[0] + rng.Next(-60, 60), k[1] + rng.Next(-120, 120)]);
                    return "knob";
                }
                case 15:
                    return await Click(".switch") ? "switch" : "switch (none)";
                case 16:
                    return await Click("button.run") ? "run" : "run (none)";
                case 17:
                case 18:
                    return await EditText();
                case 19:
                {
                    if (!await Click("[data-action=blocks]")) return "block (none)";
                    var blocks = await page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('.block-list button[data-block]')].map(e => e.dataset.block)");
                    if (blocks.Length == 0) return "block (closed)";
                    var block = Pick(blocks);
                    await Click($".block-list button[data-block='{block}']");
                    var spot = await CanvasPoint();
                    await page.Mouse.ClickAsync((float)spot[0], (float)spot[1]);
                    return "block " + block;
                }
                case 20:
                {
                    if (!await Click("[data-action=blocks]")) return "block on wire (none)";
                    var blocks = await page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('.block-list button[data-block]')].map(e => e.dataset.block)");
                    if (blocks.Length == 0) return "block on wire (closed)";
                    var block = Pick(blocks);
                    await Click($".block-list button[data-block='{block}']");
                    return await Click(".schematic-pins polyline.wire-hit") ? "block on wire " + block : "block on wire (no wire) " + block;
                }
                case 21:
                {
                    if (!await Click(".schematic-pins polyline.wire-hit")) return "name (no wire)";
                    var box = page.Locator("input.net-name-edit");
                    if (await box.CountAsync() == 0) return "name (no box)";
                    var name = Pick(Names);
                    await box.FillAsync(name);
                    await box.PressAsync(Pick(new[] { "Enter", "Enter", "Escape" }));
                    return $"name '{name}'";
                }
                case 22:
                    return await Click("[data-action=new]") ? "new" : "new (none)";
                default:
                {
                    var key = Pick(Keys);
                    await FocusCanvas();
                    await page.Keyboard.PressAsync(key);
                    return "key " + key;
                }
            }
        }

        async Task<string> EditText()
        {
            var text = page.Locator("textarea[aria-label=Netlist]");
            if (await text.CountAsync() == 0) return "text (none)";
            var lines = (await text.InputValueAsync()).Split('\n').ToList();
            var i = rng.Next(lines.Count);
            string what;
            switch (rng.Next(4))
            {
                case 0:
                    var words = lines[i].Split(' ');
                    words[rng.Next(words.Length)] = Pick(new[] { "1k", "x", "0", "n9", "", "R", "100u", ".end", "*", "Q" });
                    lines[i] = string.Join(' ', words);
                    what = "change line " + (i + 1);
                    break;
                case 1:
                    if (lines[i].Length > 0) lines[i] = lines[i].Remove(rng.Next(lines[i].Length), 1);
                    what = "delete a character on line " + (i + 1);
                    break;
                case 2:
                    lines.Insert(i, lines[i]);
                    what = "duplicate line " + (i + 1);
                    break;
                default:
                    lines.RemoveAt(i);
                    what = "remove line " + (i + 1);
                    break;
            }
            await text.FillAsync(string.Join('\n', lines));
            return "text " + what;
        }
    }
}
