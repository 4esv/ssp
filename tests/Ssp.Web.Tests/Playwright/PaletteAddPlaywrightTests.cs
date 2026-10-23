using System.Globalization;
using Xunit.Abstractions;

namespace Ssp.Web.Tests.Playwright;

// NOTE: #338: on an empty canvas, Source, Diode, Battery and Npn from the palette stacked at the centre of the view, and
// the place joined their nets with no wire. Each part must keep its own nodes and its own spot.
[Collection(PlaywrightCollection.Name)]
public class PaletteAddPlaywrightTests(ITestOutputHelper output)
{
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(3);

    [PlaywrightFact]
    public Task Four_palette_parts_share_no_node_and_do_not_overlap() => Core().WaitAsync(Limit);

    async Task Core()
    {
        var withDeps = Environment.GetEnvironmentVariable("SSP_PLAYWRIGHT_WITH_DEPS") == "1";
        Assert.Equal(0, Microsoft.Playwright.Program.Main(withDeps ? ["install", "--with-deps", "chromium"] : ["install", "chromium"]));
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1456, Height = 797 } });
        page.SetDefaultTimeout(30_000);
        await ClipPlayerTests.Session.OpenEditor(page);

        async Task<string> Netlist() => await page.Locator("textarea[aria-label=Netlist]").InputValueAsync();

        await page.Locator("button[data-action=new]").ClickAsync();
        foreach (var kind in new[] { "source", "diode", "battery", "npn" })
        {
            var before = await Netlist();
            await page.Locator($".palette-item[data-kind={kind}]").ClickAsync();
            await page.WaitForFunctionAsync("b => document.querySelector('textarea[aria-label=Netlist]').value !== b", before, new() { Timeout = 10_000, PollingInterval = 50 });
        }

        var netlist = await Netlist();
        output.WriteLine(netlist);
        var elements = netlist.Replace("\r\n", "\n").Split('\n').Skip(1)
            .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(w => w.Length > 0 && char.IsLetter(w[0][0]))
            .ToList();
        Assert.Equal(4, elements.Count);
        // NOTE: The node words: two for V and D, four for Q. Node 0 is ground, on the battery and the substrate by design.
        var nodes = elements.SelectMany(e => e.Skip(1).Take(e[0][0] == 'Q' ? 4 : 2).Where(n => n != "0").Distinct().Select(n => (Node: n, Part: e[0])));
        var shared = nodes.GroupBy(x => x.Node).Where(g => g.Count() > 1).Select(g => $"{g.Key}: {string.Join(" ", g.Select(x => x.Part))}").ToList();
        Assert.True(shared.Count == 0, $"Shared nodes: {string.Join("; ", shared)}");

        var boxes = await page.Locator("rect.part[data-ref]").EvaluateAllAsync<string[]>(
            "rs => rs.map(r => [r.dataset.ref, r.getAttribute('x'), r.getAttribute('y'), r.getAttribute('width'), r.getAttribute('height')].join(' '))");
        var parsed = boxes.Select(b => b.Split(' ')).Select(b => (Ref: b[0], X: D(b[1]), Y: D(b[2]), W: D(b[3]), H: D(b[4]))).ToList();
        output.WriteLine(string.Join("\n", boxes));
        Assert.Equal(4, parsed.Count);
        var overlaps = (from a in parsed from b in parsed
                        where string.CompareOrdinal(a.Ref, b.Ref) < 0
                        where a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H
                        select $"{a.Ref}/{b.Ref}").ToList();
        Assert.True(overlaps.Count == 0, $"Overlapping parts: {string.Join(", ", overlaps)}");
    }

    static double D(string text) => double.Parse(text, CultureInfo.InvariantCulture);
}
