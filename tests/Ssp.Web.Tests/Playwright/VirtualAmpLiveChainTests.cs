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
