using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Playwright;
using Ssp.Core.Audio;
using Xunit.Abstractions;

namespace Ssp.Web.Tests.Playwright;

/// <summary>Starts the live monitor in the editor of the published site, with a fake microphone.</summary>
[Collection(PlaywrightCollection.Name)]
public class LiveMonitorTests(ITestOutputHelper output)
{
    const int Rate = 44_100;

    [PlaywrightFact]
    public async Task MonitorPlaysANonEmptyOutputAndShowsTheLatency()
    {
        var wav = Path.Combine(Path.GetTempPath(), $"ssp-monitor-{Guid.NewGuid():N}.wav");
        WriteSine(wav);
        try
        {
            await using var session = await ClipPlayerTests.Session.Start(output,
            [
                "--use-fake-ui-for-media-stream",
                "--use-fake-device-for-media-stream",
                "--use-file-for-fake-audio-capture=" + wav,
                "--autoplay-policy=no-user-gesture-required",
            ]);
            // NOTE: The netlist is a tab of the side strip. Fill it before the monitor tab opens.
            await session.Page.Locator("textarea[aria-label=Netlist]").FillAsync(
                File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", "clipper-bjt-si.cir")));
            await session.Page.Locator(".dock-tab[data-panel=monitor]").ClickAsync();

            var panel = session.Page.Locator(".dock-panel[data-panel=monitor] .live-monitor");
            await Assertions.Expect(panel.Locator(".monitor-note")).ToHaveTextAsync("monitor, not real time");
            // NOTE: The audio context is inside live-monitor.js. The test keeps a reference, so a failure can show its state.
            await session.Page.EvaluateAsync(
                "() => { const A = globalThis.AudioContext; globalThis.AudioContext = class extends A { constructor(...a) { super(...a); globalThis.sspContext = this; } }; }");
            await panel.Locator("button.monitor-start").ClickAsync();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // NOTE: A failed chunk is silence and the session restarts (#234). After 20 failed chunks in a row the monitor
                // stops and says so, and the test stops waiting then.
                var end = await session.Page.WaitForFunctionAsync(
                    """
                    () => {
                        const o = globalThis.sspMonitorOutput;
                        if (o && o.nonzero > 0) return 'output';
                        const status = document.querySelector('.live-monitor .monitor-status')?.textContent ?? '';
                        return status.startsWith('The monitor stopped') ? 'stopped' : false;
                    }
                    """, null, new() { Timeout = 180_000, PollingInterval = 100 });
                Assert.True(await end.JsonValueAsync<string>() == "output", "The monitor stopped before it gave a non-zero output.");
            }
            finally
            {
                output.WriteLine($"After {watch.Elapsed.TotalSeconds:F1} s: " + await session.Page.EvaluateAsync<string>(
                    "() => [...document.querySelectorAll('.live-monitor p')].map(p => p.textContent.trim()).join(' | ') + ' | output ' + JSON.stringify(globalThis.sspMonitorOutput)"
                    + " + ' | context ' + globalThis.sspContext?.state + ' at ' + globalThis.sspContext?.currentTime + ' s, ' + globalThis.sspContext?.sampleRate + ' Hz'"));
            }

            var state = await session.Page.EvaluateAsync<JsonElement>(
                "() => ({ length: globalThis.sspMonitorOutput.length, nonzero: globalThis.sspMonitorOutput.nonzero })");
            Assert.True(state.GetProperty("length").GetInt32() > 0);
            Assert.True(state.GetProperty("nonzero").GetInt32() > 0);
            await Assertions.Expect(panel.Locator(".monitor-latency")).ToHaveTextAsync(new Regex(@"^Latency: \d+(\.\d+)? ms$"));
        }
        finally
        {
            File.Delete(wav);
        }
    }

    static void WriteSine(string path)
    {
        var samples = new double[Rate];
        for (var i = 0; i < samples.Length; i++) samples[i] = 0.3 * Math.Sin(2 * Math.PI * 440 * i / Rate);
        using var stream = File.Create(path);
        Wav.Write(stream, new WavData(Rate, [samples]), 16);
    }
}
