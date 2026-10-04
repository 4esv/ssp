using System.Diagnostics;
using System.Text.Json;
using Xunit.Abstractions;

namespace Ssp.Web.Tests.Playwright;

/// <summary>Clicks Try on the preset chain with the bundled clip, through the real worker host of the published site.</summary>
/// <remarks>
/// NOTE: Before #148, the output buffer played but the status stayed "Rendering…". The test waits for the status, not the buffer.
/// Since #208 the status reads "Rendering 0.0 of 1.0 s…" while it renders, so the wait is for a status that does not start with "Rendering".
/// </remarks>
[Collection(PlaywrightCollection.Name)]
public class VirtualAmpLiveChainTests(ITestOutputHelper output)
{
    // NOTE: The render of the 2 s bundled clip took 18 to 20 s in Chromium on an Apple laptop. The limit includes the
    // start of the worker runtime and gives a slower CI runner a margin.
    const int LimitMs = 90_000;

    const string Status = "() => document.querySelector('.virtual-amp .clip-status')?.textContent.trim() ?? ''";

    [PlaywrightFact]
    public async Task TryOnThePresetChainFinishesWithinTheLimit()
    {
        await using var session = await ClipPlayerTests.Session.Start(output);
        await session.Page.Locator(".dock-tab[data-panel=amp]").ClickAsync();

        var watch = Stopwatch.StartNew();
        await session.Page.Locator(".virtual-amp button.amp-try").ClickAsync();
        await session.Page.WaitForFunctionAsync(
            $"() => {{ const s = ({Status})(); return s !== '' && !s.startsWith('Rendering') && !s.startsWith('Applying'); }}",
            null,
            new() { Timeout = LimitMs, PollingInterval = 100 });
        watch.Stop();

        var status = await session.Page.EvaluateAsync<string>(Status);
        var buffer = await session.Page.EvaluateAsync<JsonElement>(ClipPlayerTests.Output);
        output.WriteLine($"Try wall time: {watch.Elapsed.TotalSeconds:F1} s. Status: {status}. Buffer: {buffer}");
        Assert.DoesNotContain("failed", status);
        Assert.Equal(JsonValueKind.Object, buffer.ValueKind);
        Assert.True(buffer.GetProperty("length").GetInt32() > 0);
    }

    // NOTE: The pedal a user draws in the editor (2026-10-04): a transistor stage, a diode clipper, a volume pot, jack markers.
    // It is the goal case of the app: draw a fuzz, plug it into the amp, play. The limit is wide on purpose. The test prints the time.
    const string DrawnFuzz = "* schematic\n* ssp:output n9\n* ssp:input n2\nV1 0 n2 DC 0 AC 1 SINE(0 1 1k)\nC1 n2 n4 100n\n.model QNPN NPN (IS=1e-14 BF=200)\nQ1 n3 n4 n6 0 QNPN\nR1 n3 n5 2.2k\nV2 n5 0 DC 9\nC2 n3 n8 100n\n.model DGEN D (IS=1e-14 N=1.9)\nD1 n8 0 DGEN\nRV1_1 n8 n9 5k\nRV1_2 n9 0 5k\n* ssp:knob RV1 linear 0.5\nR2 n6 0 1k\nC3 n6 0 22u\nR3 n4 0 47k\nR4 n4 n5 100k\n";

    [PlaywrightFact]
    public async Task TryOnTheDrawnFuzzPlaysThroughTheAmp()
    {
        await using var session = await ClipPlayerTests.Session.Start(output);
        var netlist = session.Page.Locator("textarea").First;
        await netlist.FillAsync(DrawnFuzz);
        await session.Page.Locator(".dock-tab[data-panel=amp]").ClickAsync();
        Assert.Contains("Xpedal", await session.Page.Locator("textarea.amp-netlist").InputValueAsync());

        var watch = Stopwatch.StartNew();
        await session.Page.Locator(".virtual-amp button.amp-try").ClickAsync();
        await session.Page.WaitForFunctionAsync(
            $"() => {{ const s = ({Status})(); return s !== '' && !s.startsWith('Rendering') && !s.startsWith('Applying'); }}",
            null,
            new() { Timeout = 300_000, PollingInterval = 100 });
        watch.Stop();

        var status = await session.Page.EvaluateAsync<string>(Status);
        output.WriteLine($"Drawn fuzz Try wall time: {watch.Elapsed.TotalSeconds:F1} s. Status: {status}");
        Assert.DoesNotContain("failed", status);
        Assert.StartsWith("Playing", status.Replace("Rendered at", "Playing").Replace("x oversample.", ""));
    }

    [PlaywrightFact]
    public async Task CancelOnThePresetChainStopsTheRender()
    {
        await using var session = await ClipPlayerTests.Session.Start(output);
        await session.Page.Locator(".dock-tab[data-panel=amp]").ClickAsync();

        await session.Page.Locator(".virtual-amp button.amp-try").ClickAsync();
        await session.Page.WaitForFunctionAsync(
            $"() => /^Rendering [0-9.]+ of/.test(({Status})())",
            null,
            new() { Timeout = LimitMs, PollingInterval = 100 });
        var running = await session.Page.EvaluateAsync<string>(Status);

        var watch = Stopwatch.StartNew();
        await session.Page.Locator(".virtual-amp button.clip-cancel").ClickAsync();
        await session.Page.WaitForFunctionAsync(
            $"() => ({Status})() === 'Cancelled.'",
            null,
            new() { Timeout = 5_000, PollingInterval = 20 });
        watch.Stop();
        output.WriteLine($"While rendering: {running} After Cancel: {await session.Page.EvaluateAsync<string>(Status)} ({watch.ElapsedMilliseconds} ms)");
        Assert.Equal(0, await session.Page.Locator(".virtual-amp button.clip-cancel").CountAsync());
    }
}
