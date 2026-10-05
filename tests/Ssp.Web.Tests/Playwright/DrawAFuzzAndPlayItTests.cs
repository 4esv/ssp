using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Playwright;
using Ssp.Web.Schematic;
using Xunit.Abstractions;

namespace Ssp.Web.Tests.Playwright;

// NOTE: The goal of the app, done as a user does it (#273): draw a fuzz on an empty canvas with taps and keys, set its values
// on the canvas, plug it into the virtual amp and play it. No pasted netlist and no DOM shortcut: every step is a mouse click
// at the centre of a control or a key press. The test prints what lies under each click, the netlist, the node voltages,
// the audio statistics and the times.
[Collection(PlaywrightCollection.Name)]
public class DrawAFuzzAndPlayItTests(ITestOutputHelper output)
{
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(3);

    const string Status = "() => document.querySelector('.virtual-amp .clip-status')?.textContent.trim() ?? ''";

    [PlaywrightFact]
    public Task A_user_draws_a_fuzz_sets_its_values_and_plays_it_through_the_amp() => DrawCore().WaitAsync(Limit);

    async Task DrawCore()
    {
        var whole = Stopwatch.StartNew();
        var withDeps = Environment.GetEnvironmentVariable("SSP_PLAYWRIGHT_WITH_DEPS") == "1";
        Assert.Equal(0, Microsoft.Playwright.Program.Main(withDeps ? ["install", "--with-deps", "chromium"] : ["install", "chromium"]));
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1456, Height = 797 } });
        page.SetDefaultTimeout(30_000);
        page.PageError += (_, error) => output.WriteLine("page error: " + error);
        await ClipPlayerTests.Session.OpenEditor(page);
        await page.Locator("button[data-action=new]").WaitForAsync();

        var steps = 0;

        async Task<string> Netlist() => await page.Locator("textarea[aria-label=Netlist]").InputValueAsync();

        async Task<string> Hint() => await page.Locator(".schematic-hint").InnerTextAsync();

        // NOTE: The mouse clicks the centre of the target, whatever lies on top there. The test prints what that is.
        async Task Tap(ILocator target, string name)
        {
            await target.ScrollIntoViewIfNeededAsync();
            var box = (await target.BoundingBoxAsync())!;
            var (x, y) = (box.X + box.Width / 2, box.Y + box.Height / 2);
            var under = await page.EvaluateAsync<string>(string.Create(CultureInfo.InvariantCulture,
                $"() => {{ const e = document.elementFromPoint({x}, {y}); return e ? `${{e.tagName}}.${{e.getAttribute('class')}} ${{e.dataset.ref ?? e.dataset.action ?? e.dataset.kind ?? e.dataset.dir ?? ''}} ${{e.dataset.pin ?? ''}}`.trim() : 'nothing'; }}"));
            var same = await target.EvaluateAsync<bool>(string.Create(CultureInfo.InvariantCulture,
                $"t => {{ const e = document.elementFromPoint({x}, {y}); return !!e && (t === e || t.contains(e)); }}"));
            output.WriteLine($"{++steps,3} tap {name} at {x:0},{y:0}: lands on {under}");
            Assert.True(same, $"Step {steps}: the click on {name} lands on {under}, not on {name}.");
            await page.Mouse.ClickAsync((float)x, (float)y);
        }

        // NOTE: A letter that adds a part changes the netlist. The test waits for that change.
        async Task Press(string key, bool changes)
        {
            var before = await Netlist();
            await page.Keyboard.PressAsync(key);
            if (changes)
            {
                try
                {
                    await page.WaitForFunctionAsync("b => document.querySelector('textarea[aria-label=Netlist]').value !== b", before, new() { Timeout = 10_000, PollingInterval = 50 });
                }
                catch (TimeoutException)
                {
                    Assert.Fail($"Step {steps + 1}: the key {key} did nothing. Hint: {await Hint()}");
                }
            }
            output.WriteLine($"{++steps,3} key {key}: {await Hint()}");
        }

        // NOTE: An arrow opens the picker in that direction. A blocked direction writes a status and opens nothing.
        async Task Go(string arrow, string letter)
        {
            await page.Keyboard.PressAsync(arrow);
            var menu = page.Locator(".pin-menu");
            try
            {
                await menu.WaitForAsync(new() { Timeout = 5_000 });
            }
            catch (TimeoutException)
            {
                Assert.Fail($"Step {steps + 1}: {arrow} opened no picker. Hint: {await Hint()}");
            }
            output.WriteLine($"{++steps,3} key {arrow}: {await Hint()}");
            await Press(letter, true);
        }

        async Task<Pin[]> Pins() =>
            (await page.Locator("svg.schematic-pins circle.pin[data-ref]").EvaluateAllAsync<Pin[]>("""
                ps => ps.map(p => ({
                    reference: p.dataset.ref,
                    index: +p.dataset.pin,
                    node: /node (\S+)/.exec(p.querySelector('title')?.textContent ?? '')?.[1] ?? '',
                    x: +p.getAttribute('cx'),
                    active: p.classList.contains('active'),
                }))
                """));

        // NOTE: A click on the active pin clears it, so the test clicks a pin only when it is not active.
        async Task TapPin(Pin pin, string name)
        {
            if ((await Pins()).Single(p => p.Reference == pin.Reference && p.Index == pin.Index).Active)
            {
                output.WriteLine($"{++steps,3} {name} ({pin.Reference} pin {pin.Index + 1}) is active already");
                return;
            }
            await Tap(page.Locator($"svg.schematic-pins circle.pin[data-ref=\"{pin.Reference}\"][data-pin=\"{pin.Index}\"]"), $"{name} ({pin.Reference} pin {pin.Index + 1})");
            await Assertions.Expect(page.Locator($"svg.schematic-pins circle.pin.active[data-ref=\"{pin.Reference}\"][data-pin=\"{pin.Index}\"]")).ToHaveCountAsync(1);
        }

        async Task<Pin> PinOf(Func<Pin, bool> match, string what)
        {
            var all = await Pins();
            return all.FirstOrDefault(match) ?? throw new Xunit.Sdk.XunitException(
                $"No pin is {what}. Pins: {string.Join(", ", all.Select(p => $"{p.Reference}.{p.Index}={p.Node}"))}");
        }

        // 1. The source, the jack in, the input capacitor, the NPN, the collector resistor and the battery.
        await Tap(page.Locator("button[data-action=new]"), "New");
        await Tap(page.Locator(".palette-item[data-kind=source]"), "palette Source");
        await page.Locator("svg.schematic-pins circle.pin[data-ref=V1]").First.WaitForAsync();
        var sourcePins = (await Pins()).Where(p => p.Reference == "V1").OrderBy(p => p.X).ToArray();
        await TapPin(sourcePins[^1], "source right pin");
        await Go("ArrowRight", "i");
        await Go("ArrowRight", "c");
        await Go("ArrowRight", "q");
        var transistor = (await Pins()).First(p => p.Reference.StartsWith('Q')).Reference;
        Pin Collector(Pin[] all) => all.Single(p => p.Reference == transistor && p.Index == 0);
        await TapPin(Collector(await Pins()), "collector");
        await Go("ArrowUp", "r");
        await Go("ArrowUp", "b");

        // 2. The coupling capacitor, the diode to ground, the volume pot to ground and the jack out on its wiper.
        await TapPin(Collector(await Pins()), "collector");
        await Go("ArrowRight", "c");
        await Go("ArrowRight", "d");
        await Press("g", true);
        var collectorNode = Collector(await Pins()).Node;
        var onCollector = (await Pins()).Where(p => p.Node == collectorNode).Select(p => p.Reference).ToHashSet();
        var coupling = await PinOf(p => p.Reference.StartsWith('C') && p.Node != collectorNode && onCollector.Contains(p.Reference),
            "the output of the coupling capacitor");
        await TapPin(coupling, "coupling capacitor output");
        await Go("ArrowDown", "p");
        await Press("g", true);
        var pins = await Pins();
        var pot = pins.First(p => p.Reference.StartsWith("RV", StringComparison.OrdinalIgnoreCase)).Reference;
        var wiper = pins.Single(p => p.Reference == pot && p.Node != coupling.Node && p.Node != "0");
        await TapPin(wiper, "pot wiper");
        await Go("ArrowRight", "o");

        // 3. The emitter resistor and capacitor, the base divider, the ground of the source.
        Pin Emitter(Pin[] all) => all.Single(p => p.Reference == transistor && p.Index == 2);
        await TapPin(Emitter(await Pins()), "emitter");
        await Go("ArrowDown", "r");
        await Press("g", true);
        await TapPin(Emitter(await Pins()), "emitter");
        await Go("ArrowRight", "c");
        await Press("g", true);
        pins = await Pins();
        var baseNode = pins.Single(p => p.Reference == transistor && p.Index == 1).Node;
        var input = pins.Single(p => p.Reference.StartsWith('C') && p.Node == baseNode);
        await TapPin(input, "input capacitor output");
        await Go("ArrowDown", "r");
        await Press("g", true);
        await TapPin(input, "input capacitor output");
        await Go("ArrowUp", "r");
        await Tap(page.Locator("g.pin-add").First, "a + of the upper base resistor");
        await Tap(page.Locator(".pin-menu button[data-action=connect]"), "Connect to a pin");
        pins = await Pins();
        var collectorResistor = pins.First(p => p.Reference.StartsWith('R') && !p.Reference.StartsWith("RV", StringComparison.OrdinalIgnoreCase) && p.Node == collectorNode).Reference;
        var rail = pins.Single(p => p.Reference == collectorResistor && p.Node != collectorNode).Node;
        var battery = pins.Single(p => p.Reference.StartsWith('V') && p.Reference != "V1" && p.Node == rail);
        var joined = await Netlist();
        await Tap(page.Locator($"svg.schematic-pins circle.pin[data-ref=\"{battery.Reference}\"][data-pin=\"{battery.Index}\"]"), $"rail pin ({battery.Reference} pin {battery.Index + 1})");
        await page.WaitForFunctionAsync("b => document.querySelector('textarea[aria-label=Netlist]').value !== b", joined, new() { Timeout = 10_000, PollingInterval = 50 });
        output.WriteLine($"{++steps,3} joined: {await Hint()}");
        // NOTE: The circuit is wider than the pane at the zoom of the build (200 percent), so the user taps Fit to see the source again.
        await Tap(page.Locator("button[data-zoom=fit]"), "Fit");
        await TapPin((await Pins()).Where(p => p.Reference == "V1").OrderBy(p => p.X).First(), "source left pin");
        await Press("g", true);

        // 4. The values, typed into the value box on the canvas. Each part is found by its role in the netlist.
        var netlist = Elements(await Netlist());
        string Part(Func<string[], bool> role, string what) =>
            netlist.Where(e => !e[0].StartsWith("RV", StringComparison.OrdinalIgnoreCase)).SingleOrDefault(role)?[0]
            ?? throw new Xunit.Sdk.XunitException($"No part is {what} in:\n{string.Join('\n', netlist.Select(e => string.Join(' ', e)))}");
        var emitterNode = Emitter(await Pins()).Node;
        var values = new (string Reference, string Value)[]
        {
            (Part(e => e[0][0] is 'R' && e.Contains(collectorNode), "the collector resistor"), "2.2k"),
            (Part(e => e[0][0] is 'R' && e.Contains(emitterNode), "the emitter resistor"), "1k"),
            (Part(e => e[0][0] is 'C' && e.Contains(emitterNode), "the emitter capacitor"), "22u"),
            (Part(e => e[0][0] is 'R' && e.Contains(baseNode) && e.Contains("0"), "the lower base resistor"), "47k"),
            (Part(e => e[0][0] is 'R' && e.Contains(baseNode) && !e.Contains("0"), "the upper base resistor"), "100k"),
        };
        foreach (var (reference, value) in values)
        {
            await Tap(page.Locator($"svg.schematic-pins rect.part[data-ref=\"{reference}\"]"), $"part {reference}");
            var box = page.Locator("input.value-edit");
            await Assertions.Expect(box).ToHaveAttributeAsync("aria-label", $"Value of {reference}");
            await Tap(box, $"value box of {reference}");
            await page.Keyboard.PressAsync("ControlOrMeta+A");
            await page.Keyboard.TypeAsync(value);
            await Press("Enter", true);
            Assert.True(SchematicEdits.TryParseValue(value, out var want, out _));
            var now = Elements(await Netlist()).Single(e => e[0] == reference)[3];
            Assert.True(SchematicEdits.TryParseValue(now, out var got, out _) && Math.Abs(got - want) <= want * 1e-9, $"{reference} is {now}, not {value}.");
        }
        var drawn = await Netlist();
        output.WriteLine("Netlist:\n" + drawn);

        // NOTE: The samples that the page hands to the audio output are what a user hears. Record them at start(), with the
        // step that played them: Run plays the clip through the circuit after the analyses, and Try plays it through the amp.
        await page.EvaluateAsync(@"() => {
            window.__phase = 'run';
            window.__played = [];
            const start = AudioBufferSourceNode.prototype.start;
            AudioBufferSourceNode.prototype.start = function (...args) {
                const d = this.buffer.getChannelData(0);
                let peak = 0, sum = 0, flips = 0;
                for (let i = 0; i < d.length; i++) { const v = d[i]; peak = Math.max(peak, Math.abs(v)); sum += v * v; if (i && (d[i - 1] < 0) !== (v < 0)) flips++; }
                window.__played.push({ phase: window.__phase, seconds: d.length / this.buffer.sampleRate, peak, rms: Math.sqrt(sum / d.length), zeroCrossings: flips });
                return start.apply(this, args);
            };
        }");

        // 5. Run: no error, and the NPN in its active region.
        await Tap(page.Locator("button.run"), "Run");
        await page.Locator("table.voltages").WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 60_000 });
        var voltages = await page.Locator("table.voltages tbody tr").EvaluateAllAsync<string[][]>("rs => rs.map(r => [r.dataset.node, r.cells[1].textContent.trim()])");
        output.WriteLine("Node voltages: " + string.Join(", ", voltages.Select(v => $"{v[0]} {v[1]}")));
        var errors = await page.Locator("ul.diagnostics li.error, .run-errors li").AllTextContentsAsync();
        output.WriteLine($"Errors: {errors.Count} {string.Join(" | ", errors)}");
        Assert.Empty(errors);
        var collectorVolts = Volts(voltages.Single(v => v[0] == collectorNode)[1]);
        Assert.InRange(collectorVolts, 2, 7);
        // NOTE: The test waits for the clip of Run, so that Try is the only render and its buffer is the one the test checks.
        await page.WaitForFunctionAsync("() => window.__played.length > 0", null, new() { Timeout = 120_000, PollingInterval = 100 });

        // 6. The virtual amp: the pedal is in, Try plays it.
        await Tap(page.Locator(".dock-tab[data-panel=amp]"), "Virtual amp tab");
        Assert.True(await page.Locator(".virtual-amp input.use-pedal").IsCheckedAsync(), "The pedal checkbox is not checked.");
        await page.EvaluateAsync("() => window.__phase = 'try'");
        var tryWatch = Stopwatch.StartNew();
        await Tap(page.Locator(".virtual-amp button.amp-try"), "Try");
        await page.WaitForFunctionAsync(
            $"() => {{ const s = ({Status})(); return s !== '' && !s.startsWith('Rendering') && !s.startsWith('Applying'); }}",
            null,
            new() { Timeout = 120_000, PollingInterval = 100 });
        tryWatch.Stop();
        var status = await page.EvaluateAsync<string>(Status);
        var played = await page.EvaluateAsync<JsonElement>("() => window.__played");
        whole.Stop();
        output.WriteLine($"Status: {status}");
        output.WriteLine($"Audio: {played}");
        output.WriteLine($"Wall time: whole test {whole.Elapsed.TotalSeconds:F1} s, Try {tryWatch.Elapsed.TotalSeconds:F1} s");

        Assert.StartsWith("Playing", status);
        var buffer = Assert.Single(played.EnumerateArray(), b => b.GetProperty("phase").GetString() == "try");
        Assert.True(buffer.GetProperty("seconds").GetDouble() >= 1, "Less than 1 s of audio.");
        Assert.True(buffer.GetProperty("peak").GetDouble() > 0.1);
        Assert.True(buffer.GetProperty("rms").GetDouble() > 0.02);
        Assert.True(buffer.GetProperty("zeroCrossings").GetInt32() > 100);
    }

    // The element lines of a netlist, split into fields.
    static string[][] Elements(string netlist) =>
        netlist.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && char.IsLetter(l[0]) && l[0] != '.')
            .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToArray();

    // NOTE: The table shows a voltage as "4.52 V" or "452 mV".
    static double Volts(string text)
    {
        var number = text.Replace("V", "").Trim();
        Assert.True(SchematicEdits.TryParseValue(number.Replace(" ", ""), out var volts, out _), $"Not a voltage: {text}");
        return volts;
    }

    sealed class Pin
    {
        public string Reference { get; set; } = "";
        public int Index { get; set; }
        public string Node { get; set; } = "";
        public double X { get; set; }
        public bool Active { get; set; }
    }
}
