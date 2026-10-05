using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using Ssp.Core.Netlist;
using Ssp.Web.Projects;
using Ssp.Web.Schematic;
using Xunit.Abstractions;
using LayoutDoc = Ssp.Core.Layout.Layout;

namespace Ssp.Web.Tests.Playwright;

[Collection(PlaywrightCollection.Name)]
public class DragTimingTests(ITestOutputHelper output)
{
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(3);
    const int Frames = 60;

    [PlaywrightFact]
    public Task A_drag_on_a_100_part_circuit_keeps_up_with_the_frames() => Core().WaitAsync(Limit);

    /// <summary>The Transistor fuzz and 29 copies of the RC low-pass: 100 parts.</summary>
    internal static string Netlist()
    {
        var text = new StringBuilder(File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "library", "fuzz-transistor-diode.cir")));
        for (var i = 1; i <= 29; i++)
        {
            text.Append($"VR{i} rc{i}in 0 1\nRR{i} rc{i}in rc{i}out 1k\nCR{i} rc{i}out 0 100n\n");
        }
        return text.Append(".END\n").ToString();
    }

    async Task Core()
    {
        var baseUrl = Environment.GetEnvironmentVariable(PlaywrightFactAttribute.BaseUrlVariable)!;
        Assert.Equal(0, Program.Main(["install", "chromium"]));
        var netlist = Netlist();
        var circuit = NetlistLoader.Load(netlist);
        // NOTE: The layout is stored with the project, so the editor opens it as a hand layout and runs no placer.
        var layout = AutoPlacer.Place(circuit, circuit.Directives);
        Assert.True(layout.Parts.Count == 100, $"The layout has {layout.Parts.Count} parts: {string.Join(' ', layout.Parts.Select(p => p.Reference).Take(16))}");
        var entry = JsonSerializer.Serialize(new { name = "Drag 100", netlist, layout = LayoutDoc.Format(layout), saved = DateTimeOffset.UtcNow });

        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1280, Height = 900 } });
        page.SetDefaultTimeout(60_000);
        await page.AddInitScriptAsync($$"""
            if (!sessionStorage.getItem('seeded')) {
                sessionStorage.setItem('seeded', '1');
                localStorage.setItem('{{ProjectStore.IndexKey}}', '["drag100"]');
                localStorage.setItem('{{ProjectStore.EntryKey("drag100")}}', {{JsonSerializer.Serialize(entry)}});
                localStorage.setItem('{{ProjectStore.OpenKey}}', 'drag100');
            }
            """);
        await page.RouteAsync(baseUrl + "editor", async route =>
            await route.FulfillAsync(new() { Response = await route.FetchAsync(new() { Url = baseUrl }) }));
        await page.GotoAsync(baseUrl + "editor");
        var part = page.Locator("svg.schematic-pins rect.part[data-ref=RR15]");
        await part.WaitForAsync();
        Assert.True(await page.Locator("svg.schematic-pins rect.part").CountAsync() >= 100);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForTimeoutAsync(1000);
        var before = await Place(part);

        // NOTE: One pointer move for each animation frame. A gap between two frames is the time the page took to answer
        // the move before it: the event, the render of the editor and the browser layout.
        var gaps = await part.EvaluateAsync<double[]>(@"async (rect, frames) => {
            const box = rect.getBoundingClientRect();
            const x0 = box.x + box.width / 2, y0 = box.y + box.height / 2;
            const fire = (type, x, y) => rect.dispatchEvent(new PointerEvent(type, { bubbles: true, cancelable: true, pointerId: 1,
                pointerType: 'mouse', isPrimary: true, button: 0, buttons: type === 'pointerup' ? 0 : 1, clientX: x, clientY: y }));
            const frame = () => new Promise(r => requestAnimationFrame(r));
            fire('pointerdown', x0, y0);
            await frame();
            await new Promise(r => setTimeout(r, 50));
            let last = await frame();
            const gaps = [];
            for (let i = 1; i <= frames; i++) {
                fire('pointermove', x0 + i * 8, y0 + i * 4);
                const now = await frame();
                gaps.push(now - last);
                last = now;
            }
            fire('pointerup', x0 + frames * 2, y0 + frames);
            return gaps;
        }", Frames);
        await page.WaitForTimeoutAsync(500);

        output.WriteLine("Gaps in order: " + string.Join(" ", gaps.Select(g => g.ToString("F0"))));
        Array.Sort(gaps);
        var longest = gaps[^1];
        var p95 = gaps[(int)Math.Ceiling(0.95 * gaps.Length) - 1];
        output.WriteLine($"Drag of RR15 on 100 parts, {Frames} frames: longest {longest:F1} ms, p95 {p95:F1} ms, median {gaps[gaps.Length / 2]:F1} ms");

        // NOTE: The release writes the move as one edit (#169).
        Assert.NotEqual(before, await Place(part));
        // NOTE: The goal is 60 fps, a frame of 16.7 ms. An Apple M3 Pro measures a p95 of 16.8 to 33.4 ms; before #179 it was
        // 133 ms, as each move rendered the editor twice and read the netlist once for each part. The limit has room for a
        // slow CI runner, so a return of the old cost fails it (docs/benchmarks.md).
        Assert.True(p95 < 70, $"The p95 frame of the drag is {p95:F1} ms (goal 16.7 ms, limit 70 ms).");
    }

    static Task<string> Place(ILocator part) =>
        part.EvaluateAsync<string>("e => ['x', 'y'].map(a => e.getAttribute(a)).join(' ')");
}
