using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Ssp.Core;
using Ssp.Core.Netlist;
using Xunit.Abstractions;

namespace Ssp.Web.Tests.Playwright;

// NOTE: #456: a finger on a 375 x 812 phone places an op-amp from the palette and builds a non-inverting stage with taps
// only: the source on the + input, a 10k + 10k bias from the battery, the battery on V+, V- to ground, Rg and a 10u
// capacitor from the - input to ground, and Rf from the output to the - input. Run solves it, and Compare shows the gain
// at 1 kHz: 1 + Rf / Rg. Set SSP_SHOTS to a folder to save a screenshot of the palette and of the stage after Run.
[Collection(PlaywrightCollection.Name)]
public partial class OpampTapBuildPlaywrightTests(ITestOutputHelper output)
{
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(4);
    const double Rf = 100e3, Rg = 10e3;

    [PlaywrightFact]
    public Task A_finger_places_an_opamp_and_builds_a_non_inverting_stage_with_the_gain_one_plus_rf_over_rg() => Core().WaitAsync(Limit);

    async Task Core()
    {
        var withDeps = Environment.GetEnvironmentVariable("SSP_PLAYWRIGHT_WITH_DEPS") == "1";
        Assert.Equal(0, Microsoft.Playwright.Program.Main(withDeps ? ["install", "--with-deps", "chromium"] : ["install", "chromium"]));
        var baseUrl = Environment.GetEnvironmentVariable(PlaywrightFactAttribute.BaseUrlVariable)!;
        var shots = Environment.GetEnvironmentVariable("SSP_SHOTS");
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var context = await browser.NewContextAsync(new()
        {
            HasTouch = true,
            IsMobile = true,
            DeviceScaleFactor = 2,
            ViewportSize = new() { Width = 375, Height = 812 },
        });
        var page = await context.NewPageAsync();
        await page.AddInitScriptAsync(HelpSeen.Script);
        page.SetDefaultTimeout(60_000);
        page.PageError += (_, error) => output.WriteLine("page error: " + error);
        await page.RouteAsync(baseUrl + "editor", async route =>
            await route.FulfillAsync(new() { Response = await route.FetchAsync(new() { Url = baseUrl }) }));
        // NOTE: The test server sometimes drops a connection on the first load, so a page with no editor loads again.
        for (var attempt = 0; ; attempt++)
        {
            if (attempt == 0) await page.GotoAsync(baseUrl + "editor");
            else await page.ReloadAsync();
            try
            {
                await page.Locator("button[data-action=new]").WaitForAsync(new() { Timeout = 30_000 });
                break;
            }
            catch (TimeoutException) when (attempt < 2) { }
        }

        var steps = 0;

        async Task<string> Netlist() => await page.Locator("textarea[aria-label=Netlist]").InputValueAsync();

        // NOTE: The finger lands at the centre of the target. The test prints what lies there.
        async Task Tap(ILocator target, string name)
        {
            await target.ScrollIntoViewIfNeededAsync();
            var box = (await target.BoundingBoxAsync())!;
            var (x, y) = (box.X + box.Width / 2, box.Y + box.Height / 2);
            var under = await page.EvaluateAsync<string>(string.Create(CultureInfo.InvariantCulture,
                $"() => {{ const e = document.elementFromPoint({x}, {y}); return e ? `${{e.tagName}}.${{e.getAttribute('class')}} ${{e.dataset.ref ?? e.dataset.action ?? e.dataset.kind ?? e.dataset.dir ?? ''}} ${{e.dataset.pin ?? ''}}`.trim() : 'nothing'; }}"));
            output.WriteLine($"{++steps,3} tap {name} at {x:0},{y:0}: lands on {under}");
            await page.Touchscreen.TapAsync((float)x, (float)y);
        }

        async Task Changed(string before, string what)
        {
            try
            {
                await page.WaitForFunctionAsync("b => document.querySelector('textarea[aria-label=Netlist]').value !== b", before, new() { Timeout = 10_000, PollingInterval = 50 });
            }
            catch (TimeoutException)
            {
                Assert.Fail($"Step {steps}: {what} did not change the netlist.");
            }
        }

        async Task<Pin[]> Pins() =>
            await page.Locator("svg.schematic-pins circle.pin[data-ref]").EvaluateAllAsync<Pin[]>("""
                ps => ps.map(p => ({
                    reference: p.dataset.ref,
                    index: +p.dataset.pin,
                    node: /node (\S+)/.exec(p.querySelector('title')?.textContent ?? '')?.[1] ?? '',
                    active: p.classList.contains('active'),
                }))
                """);

        // NOTE: A tap on the active pin clears it, so the test taps a pin only when it is not active.
        async Task TapPin(string reference, int index, string name)
        {
            var pin = (await Pins()).SingleOrDefault(p => p.Reference == reference && p.Index == index)
                ?? throw new Xunit.Sdk.XunitException($"No pin {reference}.{index}. Pins: {string.Join(", ", (await Pins()).Select(p => $"{p.Reference}.{p.Index}"))}");
            if (pin.Active) return;
            await Tap(page.Locator($"svg.schematic-pins circle.pin-hit[data-ref=\"{reference}\"][data-pin=\"{index}\"]"), $"{name} ({reference} pin {index + 1})");
            await Assertions.Expect(page.Locator($"svg.schematic-pins circle.pin.active[data-ref=\"{reference}\"][data-pin=\"{index}\"]")).ToHaveCountAsync(1);
        }

        // NOTE: A + beside the active pin opens the picker. The test takes the first way of those it names whose + is free and
        // not under a control, such as the part actions at the foot of the canvas.
        async Task Pick(string choice, params string[] ways)
        {
            var dots = page.Locator("g.pin-add");
            await dots.First.WaitForAsync();
            const string Free = """
                ds => ds.filter(d => { const r = d.getBoundingClientRect(); const e = document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2); return !!e && d.contains(e); })
                        .map(d => d.dataset.dir)
                """;
            var free = await dots.EvaluateAllAsync<string[]>(Free);
            // NOTE: The view follows the build at a high zoom, so the + of a pin far from the last part can lie outside it. Fit shows it.
            if (free.Length == 0)
            {
                await Tap(page.Locator("button[data-zoom=fit]"), "Fit");
                await page.WaitForTimeoutAsync(300);
                free = await dots.EvaluateAllAsync<string[]>(Free);
            }
            if (free.Length == 0)
            {
                var all = await dots.EvaluateAllAsync<string[]>("ds => ds.map(d => { const r = d.getBoundingClientRect(); const e = document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2); return `${d.dataset.dir} at ${Math.round(r.x)},${Math.round(r.y)} under ${e ? e.tagName + '.' + e.getAttribute('class') : 'nothing'}`; })");
                output.WriteLine($"Step {steps}: every + is covered: {string.Join("; ", all)}");
                if (shots is not null) await page.ScreenshotAsync(new() { Path = Path.Combine(shots, $"debug-step-{steps}.png") });
            }
            Assert.True(free.Length > 0, $"Step {steps}: no + to tap for {choice}.");
            var way = ways.FirstOrDefault(free.Contains) ?? free[0];
            await Tap(page.Locator($"g.pin-add[data-dir=\"{way}\"]"), $"+ {way}");
            var item = choice is "ground" or "connect" ? $".pin-menu button[data-action=\"{choice}\"]" : $".pin-menu button[data-kind=\"{choice}\"]";
            var before = await Netlist();
            await Tap(page.Locator(item), $"picker {choice}");
            if (choice != "connect") await Changed(before, choice);
        }

        async Task Connect(string reference, int index, string name)
        {
            var before = await Netlist();
            await Tap(page.Locator($"svg.schematic-pins circle.pin-hit[data-ref=\"{reference}\"][data-pin=\"{index}\"]"), $"{name} ({reference} pin {index + 1})");
            await Changed(before, $"connect to {name}");
        }

        async Task<string> Newest(char prefix) =>
            Elements(await Netlist()).Select(e => e[0]).Where(r => r[0] == prefix && !r.StartsWith("RV", StringComparison.OrdinalIgnoreCase)).OrderBy(r => int.Parse(r[1..], CultureInfo.InvariantCulture)).Last();

        async Task SetValue(string reference, string value)
        {
            // NOTE: The pin targets can cover the middle of a short part, so the finger goes where the part itself is on top.
            var spot = await page.Locator($"svg.schematic-pins rect.part[data-ref=\"{reference}\"]").EvaluateAsync<double[]?>("""
                r => {
                    const b = r.getBoundingClientRect();
                    for (let i = 1; i < 12; i++) for (let j = 1; j < 4; j++) {
                        const [x, y] = [b.x + b.width * i / 12, b.y + b.height * j / 4];
                        if (document.elementFromPoint(x, y) === r) return [x, y];
                    }
                    return null;
                }
                """);
            Assert.True(spot is not null, $"No point of {reference} is on top.");
            output.WriteLine($"{++steps,3} tap part {reference} at {spot![0]:0},{spot[1]:0}");
            await page.Touchscreen.TapAsync((float)spot[0], (float)spot[1]);
            var box = page.Locator("input.value-edit");
            await Assertions.Expect(box).ToHaveAttributeAsync("aria-label", $"Value of {reference}");
            await Tap(box, $"value box of {reference}");
            await box.FillAsync(value);
            var typed = await Netlist();
            await box.PressAsync("Enter");
            await Changed(typed, $"the value {value}");
        }

        // 1. The palette has Op-amp. A tap places X1 with its five pins.
        await Tap(page.Locator("button[data-action=new]"), "New");
        var tile = page.Locator(".palette-item[data-kind=opamp5]");
        await Assertions.Expect(tile).ToContainTextAsync("Op-amp");
        await tile.ScrollIntoViewIfNeededAsync();
        if (shots is not null) await page.ScreenshotAsync(new() { Path = Path.Combine(shots, "palette-opamp-375x812.png") });
        var empty = await Netlist();
        await Tap(tile, "palette Op-amp");
        await Changed(empty, "palette Op-amp");
        await page.Locator("svg.schematic-pins circle.pin[data-ref=X1]").First.WaitForAsync();
        Assert.Equal(5, (await Pins()).Count(p => p.Reference == "X1"));
        Assert.Matches(@"(?m)^X1( n\d+){5} JRC4558\r?$", await Netlist());

        // 2. The source on the + input, the bias: a resistor to ground and a resistor up to the battery.
        await TapPin("X1", 0, "+ input");
        await Pick("source", "left", "down", "up");
        var source = await Newest('V');
        await Pick("resistor", "down", "left", "up");
        await Pick("ground", "down", "left", "right", "up");
        await TapPin(source, 1, "source minus");
        await Pick("resistor", "left", "up", "down");
        await Pick("battery", "up", "left", "down");
        var battery = await Newest('V');

        // 3. The supply pins: V+ to the battery, V- to ground.
        await TapPin("X1", 3, "V+");
        await Pick("connect", "up", "right", "left");
        await Connect(battery, 0, "battery plus");
        await TapPin("X1", 4, "V-");
        await Pick("ground", "down", "right", "left");

        // 4. Rg and the capacitor from the - input to ground, Rf from the output to the - input.
        await TapPin("X1", 1, "- input");
        await Pick("resistor", "up", "left", "down");
        var rg = await Newest('R');
        await Pick("electrolytic", "up", "left", "right");
        await Pick("ground", "up", "left", "right", "down");
        await TapPin("X1", 2, "output");
        await Pick("resistor", "up", "right", "down");
        var rf = await Newest('R');
        // NOTE: Rf is 100k, typed in the value box of the part while the view is still close on it. Then its far pin goes on.
        var far = (await Pins()).Single(p => p.Active);
        await SetValue(rf, "100k");
        await TapPin(far.Reference, far.Index, "far pin of Rf");
        await Pick("connect", "left", "up", "right", "down");
        await Connect("X1", 1, "- input");

        // 5. The jacks: in on the + input, out on the output. With a pin active, a jack on the palette marks that pin.
        async Task Jack(string kind, string reference, int index, string name)
        {
            await TapPin(reference, index, name);
            var before = await Netlist();
            await Tap(page.Locator($".palette-item[data-kind={kind}]"), $"palette {kind}");
            await Changed(before, kind);
        }
        await Jack("jack-in", "X1", 0, "+ input");
        await Jack("jack-out", "X1", 2, "output");

        var drawn = await Netlist();
        output.WriteLine("Netlist:\n" + drawn);
        var lines = Elements(drawn).ToDictionary(e => e[0], StringComparer.OrdinalIgnoreCase);
        var x1 = lines["X1"];
        Assert.Equal("JRC4558", x1[6]);
        var (plus, minus, outNode, vcc, vee) = (x1[1], x1[2], x1[3], x1[4], x1[5]);
        Assert.Equal("0", vee);
        Assert.Equal(vcc, lines[battery][1]);
        Assert.Contains(minus, lines[rf][1..3]);
        Assert.Contains(outNode, lines[rf][1..3]);
        Assert.Contains(minus, lines[rg][1..3]);
        Assert.Equal(plus, lines[source][1]);
        Assert.Equal(1, drawn.Split('\n').Count(l => l.StartsWith(".subckt JRC4558", StringComparison.Ordinal)));

        // 7. Run: no error, and the output sits at the bias, half the battery.
        await Tap(page.Locator("button.run"), "Run");
        var results = page.Locator(".panel-bar button[data-panel=results]");
        if (await results.GetAttributeAsync("aria-pressed") != "true") await Tap(results, "Results");
        await page.Locator(".dock-panel[data-panel=results] table.voltages tbody tr").First.WaitForAsync(new() { Timeout = 60_000 });
        var errors = await page.Locator(".run-errors li, .dock-panel[data-panel=results] li.error").AllTextContentsAsync();
        Assert.True(errors.Count == 0, string.Join(" | ", errors));
        var voltages = await page.Locator(".dock-panel[data-panel=results] table.voltages tbody tr").EvaluateAllAsync<string[][]>("rs => rs.map(r => [r.dataset.node, r.cells[1].textContent.trim()])");
        output.WriteLine("Node voltages: " + string.Join(", ", voltages.Select(v => $"{v[0]} {v[1]}")));
        Assert.InRange(Volts(voltages.Single(v => v[0] == outNode)[1]), 4.3, 4.7);

        // 8. Compare: the gain from the jack in to the jack out at 1 kHz.
        await Tap(page.Locator(".panel-bar button[data-panel=compare]"), "Compare panel");
        var panel = page.Locator(".dock-panel[data-panel=compare]");
        await panel.Locator("select.part").SelectOptionAsync(rf);
        await panel.Locator("input.values").FillAsync("100k");
        await Tap(panel.Locator("button.compare"), "Compare");
        var cell = panel.Locator("table.series td.gain").First;
        await cell.WaitForAsync(new() { Timeout = 60_000 });
        var gain = double.Parse((await cell.InnerTextAsync()).Replace("dB", "").Trim(), CultureInfo.InvariantCulture);
        var want = 20 * Math.Log10(1 + Rf / Rg);
        output.WriteLine($"Gain at 1 kHz: {gain:0.00} dB, want {want:0.00} dB");
        Assert.InRange(gain, want - 0.5, want + 0.5);

        // NOTE: The same netlist in the engine of the command line gives the same gain.
        var result = Runner.Run(drawn, new RunOptions());
        var ac = result.FrequencyResponse!;
        var circuit = NetlistLoader.Load(drawn);
        var at = Enumerable.Range(0, ac.Frequencies.Count).MinBy(i => Math.Abs(Math.Log10(ac.Frequencies[i] / 1000)));
        var engine = ac.MagnitudeDb[circuit.Directives.Output!][at] - ac.MagnitudeDb[circuit.Directives.Input!][at];
        output.WriteLine($"Engine gain at {ac.Frequencies[at]:0} Hz: {engine:0.00} dB");
        Assert.InRange(engine, want - 0.5, want + 0.5);

        if (shots is not null)
        {
            await Tap(page.Locator(".panel-bar button[data-panel=compare]"), "Compare panel, to close it");
            await Tap(page.Locator("button[data-zoom=fit]"), "Fit");
            await page.ScreenshotAsync(new() { Path = Path.Combine(shots, "opamp-stage-after-run-375x812.png") });
        }
    }

    // The element lines of a netlist outside a .subckt block, split into fields.
    static string[][] Elements(string netlist)
    {
        var inside = false;
        var found = new List<string[]>();
        foreach (var line in netlist.Split('\n').Select(l => l.Trim()))
        {
            if (line.StartsWith(".subckt", StringComparison.OrdinalIgnoreCase)) inside = true;
            else if (line.StartsWith(".ends", StringComparison.OrdinalIgnoreCase)) inside = false;
            else if (!inside && Element().IsMatch(line)) found.Add(line.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        return [.. found];
    }

    // NOTE: The table shows a voltage as "4.52 V" or "452 mV".
    static double Volts(string text)
    {
        var number = text.Replace("V", "").Replace(" ", "");
        Assert.True(Ssp.Web.Schematic.SchematicEdits.TryParseValue(number, out var volts, out _), $"Not a voltage: {text}");
        return volts;
    }

    [GeneratedRegex(@"^[A-Za-z]\w*\s")]
    private static partial Regex Element();

    sealed class Pin
    {
        public string Reference { get; set; } = "";
        public int Index { get; set; }
        public string Node { get; set; } = "";
        public bool Active { get; set; }
    }
}
