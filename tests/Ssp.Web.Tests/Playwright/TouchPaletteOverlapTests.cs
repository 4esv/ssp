using Ssp.Web.Sharing;

namespace Ssp.Web.Tests.Playwright;

// NOTE: On a phone with the Transistor fuzz loaded, a finger tap at the centre of a part selects the part and adds no part (#258). The
// issue guessed that the palette lay over the canvas. It does not: the palette is a 44 px strip above the canvas and nothing moves
// after a tap. The phantom part R1 in an earlier test came from the locator tap with Force, not from the app. The test keeps the proof.
public partial class TouchPlaywrightTests
{
    const string CoverSnapshot = """
        () => {
            const box = e => { if (!e) return null; const r = e.getBoundingClientRect(); return [Math.round(r.x), Math.round(r.y), Math.round(r.width), Math.round(r.height)]; };
            const under = [...document.querySelectorAll('.schematic-pins rect.part[data-ref]')].filter(e => /^R[0-9]+$/.test(e.dataset.ref)).map(e => {
                const r = e.getBoundingClientRect();
                const at = document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2);
                return { ref: e.dataset.ref, at: [Math.round(r.x + r.width / 2), Math.round(r.y + r.height / 2)], hit: at === e ? 'itself' : `${at?.tagName}.${at?.getAttribute('class')}`, visible: r.bottom > 0 && r.top < innerHeight };
            });
            return { palette: box(document.querySelector('.palette')), canvas: box(document.querySelector('.schematic-editor .schematic')), tools: box(document.querySelector('.schematic-tools')), actions: box(document.querySelector('.part-actions, .group-actions')), view: [innerWidth, innerHeight, scrollY], under };
        }
        """;

    [PlaywrightFact]
    public Task A_tap_on_a_part_never_lands_on_the_palette() => CoverCore().WaitAsync(Limit);

    async Task CoverCore()
    {
        var fuzz = File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "library", "fuzz-transistor-diode.cir"));
        await using var phone = await Phone.Open("#" + ShareCodec.Encode(fuzz));
        var page = phone.Page;
        await page.Locator(".schematic[data-view]").WaitForAsync();
        var failures = new List<string>();

        async Task Record(string when)
        {
            var s = await page.EvaluateAsync<System.Text.Json.JsonElement>(CoverSnapshot);
            output.WriteLine($"{when}: {s}");
            foreach (var u in s.GetProperty("under").EnumerateArray())
            {
                var hit = u.GetProperty("hit").GetString();
                if (hit != "itself") failures.Add($"{when}: {u.GetProperty("ref").GetString()} is covered by {hit}");
            }
        }

        await Record("at rest");
        await page.Locator("button[data-tool=select]").TapAsync();
        await Record("after Select");
        var spot = await page.EvaluateAsync<double[]?>("""
            () => { const e = document.querySelector('.schematic-pins rect.part[data-ref=R2]'); const r = e.getBoundingClientRect(); return [r.x + r.width / 2, r.y + r.height / 2]; }
            """);
        await page.Touchscreen.TapAsync((float)spot![0], (float)spot[1]);
        await Record("after tapping R2");
        foreach (var next in new[] { "R4", "R6" })
        {
            var at = await page.EvaluateAsync<double[]>($$"""
                () => { const r = document.querySelector('.schematic-pins rect.part[data-ref={{next}}]').getBoundingClientRect(); return [r.x + r.width / 2, r.y + r.height / 2]; }
                """);
            var hit = await page.EvaluateAsync<string>($"() => {{ const e = document.elementFromPoint({at[0]}, {at[1]}); return e.tagName + '.' + e.getAttribute('class') + '.' + (e.dataset.ref ?? ''); }}");
            await page.Touchscreen.TapAsync((float)at[0], (float)at[1]);
            var parts = await page.EvaluateAsync<string[]>("() => [...new Set([...document.querySelectorAll('.schematic-pins rect.part[data-ref]')].map(e => e.dataset.ref))].sort()");
            var selected = await page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('.schematic-pins rect.part.selected')].map(e => e.dataset.ref).sort()");
            var hint = await page.Locator(".schematic-hint").First.TextContentAsync();
            output.WriteLine($"tap {next} at {at[0]:0},{at[1]:0} on {hit}: selected [{string.Join(", ", selected)}], parts [{string.Join(", ", parts)}], hint {hint}");
            if (parts.Contains("R1")) failures.Add($"tap {next} added a part R1");
        }
        // NOTE: R5 has its own pin under its centre (a pin hit circle), so a tap there selects the pin. That is by design.
        var real = failures.Where(f => !f.Contains("R5 is covered by circle.pin-hit")).ToList();
        Assert.True(real.Count == 0, string.Join(" ", real));
        Assert.Equal(["R2", "R4", "R6"], await page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('.schematic-pins rect.part.selected')].map(e => e.dataset.ref).sort()"));
    }
}
