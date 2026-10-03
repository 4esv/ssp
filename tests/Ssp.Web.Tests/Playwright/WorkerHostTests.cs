using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Ssp.Web.Tests.Playwright;

/// <summary>
/// Renders in the Web Worker of the published site and clicks the page during the render.
/// </summary>
/// <remarks>
/// NOTE: The browser renders at about 0.29x real time (docs/benchmarks.md), so 3 s of audio is a render of about 10 s.
/// </remarks>
[Collection(PlaywrightCollection.Name)]
public class WorkerHostTests(ITestOutputHelper output)
{
    private const int Fs = 44_100;
    private const int Seconds = 3;

    // A blocked main thread answers the click only after the render, about 10 s later.
    private const double MaxClickResponseMs = 500;

    // The input is made in the page, so the test does not send 132 300 numbers through Playwright.
    private const string StartRender = """
        async ([netlist, fs, seconds]) => {
          const client = await import('./js/simulation-worker-client.js');
          const input = new Float64Array(fs * seconds);
          for (let i = 0; i < input.length; i++) input[i] = 0.3 * Math.sin(2 * Math.PI * 440 * i / fs);
          globalThis.sspClick = null;
          document.addEventListener('click', () => { globalThis.sspClick ??= performance.now(); });
          const render = globalThis.sspRender = { start: performance.now(), done: null, json: null, error: null };
          client.render(netlist, input, fs, 1).then(
            json => { render.json = json; render.done = performance.now(); },
            error => { render.error = String(error); render.done = performance.now(); });
        }
        """;

    [PlaywrightFact]
    public async Task PageRespondsToClickDuringRender()
    {
        var baseUrl = Environment.GetEnvironmentVariable(PlaywrightFactAttribute.BaseUrlVariable)!;
        var netlist = File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", "clipper-bjt-si.cir"));
        var withDeps = Environment.GetEnvironmentVariable("SSP_PLAYWRIGHT_WITH_DEPS") == "1";
        var install = withDeps ? new[] { "install", "--with-deps", "chromium" } : new[] { "install", "chromium" };
        Assert.Equal(0, Microsoft.Playwright.Program.Main(install));

        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        page.Console += (_, message) => output.WriteLine("console: " + message.Text);
        page.PageError += (_, error) => output.WriteLine("page error: " + error);
        page.Worker += (_, worker) => worker.Console += (_, message) => output.WriteLine("worker console: " + message.Text);
        await page.GotoAsync(baseUrl);
        await Assertions.Expect(page.Locator("pre")).ToContainTextAsync("SpiceSharp", new() { Timeout = 60_000 });

        await page.EvaluateAsync(StartRender, new object[] { netlist, Fs, Seconds });
        await Task.Delay(2_000);
        var before = await page.EvaluateAsync<double>("() => performance.now()");
        await page.Mouse.ClickAsync(10, 10);
        var during = await page.EvaluateAsync<JsonElement>("() => ({ click: globalThis.sspClick, done: globalThis.sspRender.done })");

        Assert.Equal(JsonValueKind.Number, during.GetProperty("click").ValueKind);
        Assert.Equal(JsonValueKind.Null, during.GetProperty("done").ValueKind);
        var response = during.GetProperty("click").GetDouble() - before;

        await page.WaitForFunctionAsync("() => globalThis.sspRender.done !== null", null, new() { Timeout = 180_000 });
        var render = await page.EvaluateAsync<JsonElement>("() => globalThis.sspRender");
        Assert.Equal(JsonValueKind.Null, render.GetProperty("error").ValueKind);
        var json = render.GetProperty("json").GetString()!;
        var wall = render.GetProperty("done").GetDouble() - render.GetProperty("start").GetDouble();
        var samples = JsonDocument.Parse(json).RootElement.EnumerateArray().Select(s => s.GetDouble()).ToArray();

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"render of {Seconds} s of audio: {wall:F0} ms"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"click response during the render: {response:F1} ms"));
        output.WriteLine($"request payload: netlist {Encoding.UTF8.GetByteCount(netlist)} bytes, input {Fs * Seconds * sizeof(double)} bytes");
        output.WriteLine($"response payload: {Encoding.UTF8.GetByteCount(json)} bytes of JSON");

        Assert.True(response < MaxClickResponseMs, $"The page answered the click after {response} ms.");
        Assert.Equal(Fs * Seconds, samples.Length);
        Assert.All(samples, sample => Assert.True(double.IsFinite(sample)));
    }
}
