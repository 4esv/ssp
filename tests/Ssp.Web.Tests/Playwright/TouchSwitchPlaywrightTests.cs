using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Ssp.Web.Tests.Playwright;

// NOTE: An SPDT placed with a finger on a new circuit (#266). The divider around it is typed, then a tap on the lever
// sets the position and the live run gives a new voltage at the common.
public partial class TouchPlaywrightTests
{
    [PlaywrightFact]
    public Task A_finger_places_an_spdt_and_a_tap_on_it_changes_the_divider_voltage() => SwitchCore().WaitAsync(Limit);

    async Task SwitchCore()
    {
        await using var phone = await Phone.Open();
        var page = phone.Page;

        async Task Tap(ILocator target, string name)
        {
            await target.ScrollIntoViewIfNeededAsync();
            var box = (await target.BoundingBoxAsync())!;
            output.WriteLine($"tap {name}: {box.Width:0.#} x {box.Height:0.#}");
            Assert.True(box.Width >= Finger - 0.5 && box.Height >= Finger - 0.5, $"{name} is {box.Width:0.#} x {box.Height:0.#} px");
            await page.Touchscreen.TapAsync((float)(box.X + box.Width / 2), (float)(box.Y + box.Height / 2));
        }

        await Tap(page.Locator("button[data-action=new]"), "New");
        await Tap(page.Locator(".palette-item[data-kind=switch-2]"), "palette SPDT");
        await page.Locator("g.switch[data-ref=RSW1]").WaitForAsync(new() { State = WaitForSelectorState.Attached });

        var placed = await Netlist(page);
        output.WriteLine(placed);
        var throws = SwitchLine().Matches(placed).Select(m => (Common: m.Groups[2].Value, Throw: m.Groups[3].Value)).ToList();
        Assert.Equal(2, throws.Count);
        var common = throws[0].Common;
        // NOTE: 10k over 10k at throw 1, 30k over 10k at throw 2. A run needs an output node.
        var divider = $"V1 vin 0 DC 9\nR1 vin {throws[0].Throw} 10k\nR2 vin {throws[1].Throw} 30k\nR3 {common} 0 10k\n* ssp:output {common}\n";
        var netlist = Regex.Replace(placed, @"^\.end\s*$", divider + ".end", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        if (netlist == placed) netlist = placed.TrimEnd('\n') + "\n" + divider;
        await page.Locator("textarea[aria-label=Netlist]").EvaluateAsync(
            "(ta, text) => { ta.value = text; ta.dispatchEvent(new Event('input', { bubbles: true })); ta.dispatchEvent(new Event('change', { bubbles: true })); }", netlist);

        async Task<double> Volts(string position)
        {
            await page.Locator($"g.switch[data-ref=RSW1][data-position=\"{position}\"]").WaitForAsync();
            var label = page.Locator($"svg.schematic-pins text.net-voltage[data-node=\"{common}\"]:not(.stale)");
            await label.WaitForAsync(new() { Timeout = 180_000, State = WaitForSelectorState.Attached });
            var text = await label.TextContentAsync() ?? "";
            output.WriteLine($"position {position}: {common} = {text}");
            return double.Parse(Regex.Match(text, @"-?[\d.]+").Value, CultureInfo.InvariantCulture);
        }

        var one = await Volts("1");
        await Tap(page.Locator("g.switch[data-ref=RSW1] circle.switch-hit"), "lever");
        // NOTE: The live run follows the tap after a moment, so the test reads the label until it changes.
        var two = await Volts("2");
        for (var i = 0; i < 120 && Math.Abs(two - one) < 1e-9; i++)
        {
            await page.WaitForTimeoutAsync(500);
            two = await Volts("2");
        }

        Assert.Contains("* ssp:switch RSW1 2", await Netlist(page));
        Assert.Equal(4.5, one, 2);
        Assert.Equal(2.25, two, 2);
    }

    [GeneratedRegex(@"^(RSW1_\d)\s+(\S+)\s+(\S+)", RegexOptions.Multiline)]
    private static partial Regex SwitchLine();
}
