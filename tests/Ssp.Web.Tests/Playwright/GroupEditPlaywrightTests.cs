using Microsoft.Playwright;
using Ssp.Web.Sharing;
using Xunit.Abstractions;

namespace Ssp.Web.Tests.Playwright;

// NOTE: Several parts at once on the fuzz starter (#169): select R2, R4 and R6, copy, paste, and one Undo takes the paste back.
// The keys work, and so do the Select, Copy and Paste buttons for a finger.
[Collection(PlaywrightCollection.Name)]
public class GroupEditPlaywrightTests(ITestOutputHelper output)
{
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(4);

    [PlaywrightFact]
    public Task KeysCopyAndPasteThreePartsAndOneUndoTakesThemBack() => KeysCore().WaitAsync(Limit);

    [PlaywrightFact]
    public Task ButtonsCopyAndPasteOnAPhone() => ButtonsCore().WaitAsync(Limit);

    const string HiddenTools = """
        () => {
            const row = document.querySelector('.schematic-tools').getBoundingClientRect();
            return [...document.querySelectorAll('.schematic-tools button')].filter(b => b.offsetParent)
                .filter(b => { const r = b.getBoundingClientRect(); return r.right > row.right + 0.5 || r.left < row.left - 0.5; })
                .map(b => b.dataset.tool || b.dataset.action || b.className);
        }
        """;

    static Task<string[]> Parts(IPage page) =>
        page.EvaluateAsync<string[]>("() => [...new Set([...document.querySelectorAll('.schematic-pins rect.part[data-ref]')].map(e => e.dataset.ref))].sort()");

    static Task<string[]> Selected(IPage page) =>
        page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('.schematic-pins rect.part.selected')].map(e => e.dataset.ref).sort()");

    async Task KeysCore()
    {
        await using var session = await Session.Open();
        var page = await session.Editor(1456, 797);
        var before = await Parts(page);
        // NOTE: The tool row scrolls. At this size New was out of view before #169, so the test prints the tools out of view and does not fail on them.
        output.WriteLine($"1456x797: tools out of view [{string.Join(", ", await page.EvaluateAsync<string[]>(HiddenTools))}]");

        output.WriteLine("what is at each target: " + await page.EvaluateAsync<string>("""
            () => ['R2', 'R4', 'R6'].map(ref => {
                const e = document.querySelector(`.schematic-pins rect.part[data-ref=${ref}]`);
                const r = e.getBoundingClientRect();
                const at = document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2);
                return `${ref}@${Math.round(r.x + r.width / 2)},${Math.round(r.y + r.height / 2)} -> ${at.tagName}.${at.getAttribute('class')}.${at.dataset.ref ?? ''}.${at.textContent?.slice(0, 12) ?? ''}`;
            }).join(' | ')
            """));
        await page.Locator(".schematic-pins rect.part[data-ref=R2]").ClickAsync(new() { Force = true });
        await page.Locator(".schematic-pins rect.part[data-ref=R4]").ClickAsync(new() { Force = true, Modifiers = [KeyboardModifier.Shift] });
        await page.Locator(".schematic-pins rect.part[data-ref=R6]").ClickAsync(new() { Force = true, Modifiers = [KeyboardModifier.ControlOrMeta] });
        Assert.Equal(["R2", "R4", "R6"], await Selected(page));

        await page.Keyboard.PressAsync("ControlOrMeta+c");
        await page.Keyboard.PressAsync("ControlOrMeta+v");
        await Assertions.Expect(page.Locator(".schematic-pins rect.part[data-ref=R7]")).ToBeAttachedAsync();
        var pasted = await Parts(page);
        var added = pasted.Except(before).ToArray();
        output.WriteLine($"1456x797: pasted {string.Join(", ", added)}; selected {string.Join(", ", await Selected(page))}; hint: {await page.Locator(".schematic-hint").TextContentAsync()}");
        Assert.Equal(["R1", "R3", "R7"], added);
        Assert.Equal(["R1", "R3", "R7"], await Selected(page));

        await page.Keyboard.PressAsync("ControlOrMeta+z");
        await Assertions.Expect(page.Locator(".schematic-pins rect.part[data-ref=R7]")).ToHaveCountAsync(0);
        Assert.Equal(before, await Parts(page));

        // NOTE: Shift and a mouse drag draw a box. It selects the parts it touches and moves nothing.
        var r2 = (await page.Locator(".schematic-pins rect.part[data-ref=R2]").BoundingBoxAsync())!;
        var r4 = (await page.Locator(".schematic-pins rect.part[data-ref=R4]").BoundingBoxAsync())!;
        await page.Keyboard.DownAsync("Shift");
        await page.Mouse.MoveAsync(Math.Min(r2.X, r4.X) - 3, Math.Min(r2.Y, r4.Y) - 30);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(Math.Max(r2.X + r2.Width, r4.X + r4.Width) + 3, Math.Max(r2.Y + r2.Height, r4.Y + r4.Height) + 3, new() { Steps = 8 });
        await Assertions.Expect(page.Locator(".select-box")).ToHaveCountAsync(1);
        await page.Mouse.UpAsync();
        await page.Keyboard.UpAsync("Shift");
        var boxed = await Selected(page);
        output.WriteLine($"1456x797: the box selects {string.Join(", ", boxed)}");
        Assert.Contains("R2", boxed);
        Assert.Contains("R4", boxed);
        Assert.Equal(before, await Parts(page));

        // NOTE: Ctrl+A then Ctrl+X cuts every part in one step, and one Undo brings them all back.
        await page.Locator(".schematic").FocusAsync();
        await page.Keyboard.PressAsync("ControlOrMeta+a");
        Assert.Equal(before, (await Selected(page)).Distinct().ToArray());
        await page.Keyboard.PressAsync("ControlOrMeta+x");
        await Assertions.Expect(page.Locator(".schematic-pins rect.part")).ToHaveCountAsync(0);
        await page.Keyboard.PressAsync("ControlOrMeta+z");
        await Assertions.Expect(page.Locator(".schematic-pins rect.part[data-ref=R2]")).ToBeAttachedAsync();
        Assert.Equal(before, await Parts(page));
    }

    async Task ButtonsCore()
    {
        await using var session = await Session.Open();
        var page = await session.Editor(390, 844, touch: true);
        var before = await Parts(page);

        // NOTE: At 390 px the palette and the action row lie over part of the canvas, and they move after each tap. A tap on a covered
        // part hits them, so before each tap the test takes a resistor whose centre is the part itself and taps that point.
        await page.Locator("button[data-tool=select]").TapAsync();
        for (var i = 0; i < 3; i++)
        {
            var spot = await page.EvaluateAsync<double[]?>("""
                () => {
                    const chosen = new Set([...document.querySelectorAll('.schematic-pins rect.part.selected')].map(e => e.dataset.ref));
                    for (const e of [...document.querySelectorAll('.schematic-pins rect.part[data-ref^=R]')].sort((a, b) => a.dataset.ref.localeCompare(b.dataset.ref))) {
                        if (!/^R[0-9]+$/.test(e.dataset.ref) || chosen.has(e.dataset.ref)) continue;
                        const r = e.getBoundingClientRect();
                        const x = r.x + r.width / 2, y = r.y + r.height / 2;
                        if (document.elementFromPoint(x, y) === e) return [x, y];
                    }
                    return null;
                }
                """);
            Assert.NotNull(spot);
            await page.Touchscreen.TapAsync((float)spot![0], (float)spot[1]);
        }
        var chosen = await Selected(page);
        output.WriteLine($"390x844: selected by taps [{string.Join(", ", chosen)}]");
        Assert.Equal(3, chosen.Length);
        await page.Locator(".part-action[data-action=copy]").TapAsync();
        await page.Locator("button[data-action=paste]").TapAsync();
        await page.WaitForFunctionAsync("n => document.querySelectorAll('.schematic-pins rect.part[data-ref]').length >= n", before.Length + 3);
        Assert.Equal(3, (await Parts(page)).Except(before).Count());

        var sizes = await page.EvaluateAsync<double[][]>("() => ['select', 'copy', 'cut', 'paste'].map(k => document.querySelector(`[data-tool=${k}], [data-action=${k}]`).getBoundingClientRect()).map(r => [r.width, r.height])");
        output.WriteLine($"390x844 touch: buttons {string.Join(", ", sizes.Select(b => $"{b[0]:0}x{b[1]:0}"))}");
        Assert.All(sizes, b => Assert.True(b[0] >= 44 && b[1] >= 44, $"A button is {b[0]:0}x{b[1]:0} px."));

        await page.Locator("button.undo").TapAsync();
        await page.WaitForFunctionAsync("n => document.querySelectorAll('.schematic-pins rect.part[data-ref]').length === n", before.Length);
        Assert.Equal(before, await Parts(page));
    }

    sealed class Session : IAsyncDisposable
    {
        IPlaywright playwright = null!;
        IBrowser browser = null!;
        string baseUrl = "";
        string hash = "";

        public static async Task<Session> Open()
        {
            Assert.Equal(0, Microsoft.Playwright.Program.Main(["install", "chromium"]));
            var session = new Session
            {
                baseUrl = Environment.GetEnvironmentVariable(PlaywrightFactAttribute.BaseUrlVariable)!,
                hash = ShareCodec.Encode(File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "library", "fuzz-transistor-diode.cir"))),
                playwright = await Microsoft.Playwright.Playwright.CreateAsync(),
            };
            session.browser = await session.playwright.Chromium.LaunchAsync();
            return session;
        }

        public async Task<IPage> Editor(int width, int height, bool touch = false)
        {
            var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = width, Height = height }, HasTouch = touch, IsMobile = touch });
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
