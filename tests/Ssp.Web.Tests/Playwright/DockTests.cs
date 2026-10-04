using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Ssp.Web.Tests.Playwright;

[Collection(PlaywrightCollection.Name)]
public class DockTests
{
    // NOTE: A Playwright drag in Chromium can wait for ever, and a test timeout does not stop it. Every test has a limit.
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(3);

    [PlaywrightFact]
    public Task AltShiftArrowDocksPanelAtLeftEdge() => AltShiftArrowDocksPanelAtLeftEdgeCore().WaitAsync(Limit);

    static async Task AltShiftArrowDocksPanelAtLeftEdgeCore()
    {
        var withDeps = Environment.GetEnvironmentVariable("SSP_PLAYWRIGHT_WITH_DEPS") == "1";
        var install = withDeps ? new[] { "install", "--with-deps", "chromium" } : new[] { "install", "chromium" };
        Assert.Equal(0, Program.Main(install));

        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1280, Height = 900 } });
        page.SetDefaultTimeout(30_000);

        await ClipPlayerTests.Session.OpenEditor(page);

        var tab = page.Locator(".dock-tab[data-panel=knobs]");
        await tab.WaitForAsync(new() { Timeout = 60_000 });
        var text = page.Locator(".dock-panel[data-panel=text]");
        var before = (await text.BoundingBoxAsync())!;

        await tab.FocusAsync();
        await page.Keyboard.PressAsync("Alt+Shift+ArrowLeft");

        var layout = page.Locator(".dock-layout");
        await Assertions.Expect(layout).ToHaveAttributeAsync("data-tree", new Regex(@"^row\(0\.33 tabs\(knobs\*\), "));
        await Assertions.Expect(tab).ToBeFocusedAsync();
        var knobs = (await page.Locator(".dock-panel[data-panel=knobs]").BoundingBoxAsync())!;
        var after = (await text.BoundingBoxAsync())!;
        Assert.True(knobs.X < after.X, $"Knobs is at x = {knobs.X}, the netlist is at x = {after.X}.");
        Assert.True(after.X > before.X, $"The netlist was at x = {before.X} and is at x = {after.X}.");

        var tree = await layout.GetAttributeAsync("data-tree");
        await page.ReloadAsync();
        await Assertions.Expect(layout).ToHaveAttributeAsync("data-tree", tree!, new() { Timeout = 60_000 });
    }

    [PlaywrightFact]
    public Task DragMovesPanelToNewGroupInChromium() => DragMovesPanelToNewGroup("chromium").WaitAsync(Limit);

    [PlaywrightFact]
    public Task DragMovesPanelToNewGroupInFirefox() => DragMovesPanelToNewGroup("firefox").WaitAsync(Limit);

    static async Task DragMovesPanelToNewGroup(string name)
    {
        var withDeps = Environment.GetEnvironmentVariable("SSP_PLAYWRIGHT_WITH_DEPS") == "1";
        var install = withDeps ? new[] { "install", "--with-deps", name } : new[] { "install", name };
        Assert.Equal(0, Program.Main(install));

        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        var type = name == "firefox" ? playwright.Firefox : playwright.Chromium;
        await using var browser = await type.LaunchAsync();
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1280, Height = 900 } });
        page.SetDefaultTimeout(30_000);

        await ClipPlayerTests.Session.OpenEditor(page);

        var tab = page.Locator(".dock-tab[data-panel=knobs]");
        await tab.WaitForAsync(new() { Timeout = 60_000 });
        var layout = page.Locator(".dock-layout");
        await Assertions.Expect(layout).ToHaveAttributeAsync("data-tree", new Regex(@"tabs\(knobs\*, calculators, compare[,)]"));

        // NOTE: The right zone of the schematic group docks the dragged panel as a new group.
        var target = page.Locator(".dock-panel[data-panel=schematic]");
        var box = (await target.BoundingBoxAsync())!;
        var from = (await tab.BoundingBoxAsync())!;
        var x = box.X + box.Width - 10;
        var y = box.Y + box.Height / 2;
        await page.Mouse.MoveAsync(from.X + from.Width / 2, from.Y + from.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x, y, new() { Steps = 10 }).WaitAsync(TimeSpan.FromSeconds(45));

        // NOTE: The drop zones show only after the drag starts and the layout draws again. A drop before that lands on the
        // panel and does nothing. On a slow runner a one-move drag did that (#225), so the drop waits for the zone.
        await page.WaitForFunctionAsync(
            "({ x, y }) => document.elementFromPoint(x, y)?.matches('.dock-zone.right') ?? false", new { x = (double)x, y = (double)y });
        await page.Mouse.MoveAsync(x - 1, y);
        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.UpAsync().WaitAsync(TimeSpan.FromSeconds(45));

        await Assertions.Expect(layout).ToHaveAttributeAsync("data-tree", new Regex(@"tabs\(schematic\*\), 0\.\d+ tabs\(knobs\*\)"));
        await Assertions.Expect(layout).Not.ToHaveAttributeAsync("data-tree", new Regex(@"tabs\(knobs\*, calculators"));
    }
}
