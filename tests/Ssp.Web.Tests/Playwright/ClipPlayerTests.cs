using System.Text.Json;
using Microsoft.Playwright;
using Ssp.Core.Audio;
using Xunit.Abstractions;

namespace Ssp.Web.Tests.Playwright;

/// <summary>Plays a clip through the circuit in the editor of the published site.</summary>
/// <remarks>
/// NOTE: The browser renders at about 0.29x real time (docs/benchmarks.md). The tests use a short upload where they can.
/// </remarks>
[Collection(PlaywrightCollection.Name)]
public class ClipPlayerTests(ITestOutputHelper output)
{
    internal const int UploadFrames = 11_025;
    internal const int UploadRate = 44_100;
    internal const string Output = "() => globalThis.sspClipBuffer ? { length: globalThis.sspClipBuffer.length, rate: globalThis.sspClipBuffer.sampleRate } : null";

    static int CabFrames => Wav.Read(File.OpenRead(Path.Combine(RepoPaths.Root, "models", "ir", "cab-1x12.wav"))).Channels[0].Length;

    [PlaywrightFact]
    public async Task RunPlaysTheBundledClipAfterTheAnalyses()
    {
        var clip = Wav.Read(File.OpenRead(Path.Combine(RepoPaths.Root, "src", "Ssp.Web", "Audio", "clip.wav")));
        await using var session = await Session.Start(output);

        await session.Page.Locator("textarea[aria-label=Netlist]").FillAsync(Clipper);
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Run", Exact = true }).ClickAsync();
        await Assertions.Expect(session.Page.Locator("table.voltages")).ToBeVisibleAsync(new() { Timeout = 120_000 });
        await session.Page.WaitForFunctionAsync("() => globalThis.sspClipBuffer", null, new() { Timeout = 180_000 });

        var buffer = await session.Page.EvaluateAsync<JsonElement>(Output);
        Assert.Equal(clip.Channels[0].Length + CabFrames - 1, buffer.GetProperty("length").GetInt32());
        Assert.Equal(clip.SampleRate, buffer.GetProperty("rate").GetInt32());
    }

    [PlaywrightFact]
    public async Task UploadedWavIsRenderedAndPlayed()
    {
        await using var session = await Session.Start(output);
        // NOTE: The netlist and the clip are tabs of one strip, so fill the netlist before the clip tab hides it.
        await session.Page.Locator("textarea[aria-label=Netlist]").FillAsync(Clipper);
        await session.Page.Locator(".dock-tab[data-panel=clip]").ClickAsync();

        await session.Page.Locator(".dock-panel[data-panel=clip] .clip-player input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = "sine.wav",
            MimeType = "audio/wav",
            Buffer = Sine(),
        });
        await session.Page.Locator(".dock-panel[data-panel=clip] .clip-player button.clip-play").ClickAsync();
        await session.Page.WaitForFunctionAsync("() => globalThis.sspClipBuffer", null, new() { Timeout = 180_000 });

        var buffer = await session.Page.EvaluateAsync<JsonElement>(Output);
        Assert.Equal(UploadFrames + CabFrames - 1, buffer.GetProperty("length").GetInt32());
        Assert.Equal(UploadRate, buffer.GetProperty("rate").GetInt32());
    }

    [PlaywrightFact]
    public async Task DownloadIsAWavFileThatWavReadAccepts()
    {
        await using var session = await Session.Start(output);
        // NOTE: The netlist and the clip are tabs of one strip, so fill the netlist before the clip tab hides it.
        await session.Page.Locator("textarea[aria-label=Netlist]").FillAsync(Clipper);
        await session.Page.Locator(".dock-tab[data-panel=clip]").ClickAsync();
        await session.Page.Locator(".dock-panel[data-panel=clip] .clip-player input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = "sine.wav",
            MimeType = "audio/wav",
            Buffer = Sine(),
        });
        await session.Page.Locator(".dock-panel[data-panel=clip] .clip-player button.clip-play").ClickAsync();
        await session.Page.WaitForFunctionAsync("() => globalThis.sspClipBuffer", null, new() { Timeout = 180_000 });

        var download = await session.Page.RunAndWaitForDownloadAsync(
            () => session.Page.Locator(".dock-panel[data-panel=clip] .clip-player .clip-download").ClickAsync());
        var path = await download.PathAsync();
        var wav = Wav.Read(File.OpenRead(path!));

        Assert.EndsWith(".wav", download.SuggestedFilename);
        Assert.Equal(UploadRate, wav.SampleRate);
        Assert.Equal(UploadFrames + CabFrames - 1, wav.Channels[0].Length);
        Assert.Contains(wav.Channels[0], sample => sample != 0);
    }

    static string Clipper => File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", "clipper-bjt-si.cir"));

    internal static byte[] Sine()
    {
        var samples = new double[UploadFrames];
        for (var i = 0; i < samples.Length; i++) samples[i] = 0.3 * Math.Sin(2 * Math.PI * 440 * i / UploadRate);
        using var stream = new MemoryStream();
        Wav.Write(stream, new WavData(UploadRate, [samples]), 16);
        return stream.ToArray();
    }

    internal sealed class Session : IAsyncDisposable
    {
        readonly IPlaywright playwright;
        readonly IBrowser browser;
        public IPage Page { get; }

        Session(IPlaywright playwright, IBrowser browser, IPage page) => (this.playwright, this.browser, Page) = (playwright, browser, page);

        public static async Task<Session> Start(ITestOutputHelper output, string[]? browserArgs = null)
        {
            var withDeps = Environment.GetEnvironmentVariable("SSP_PLAYWRIGHT_WITH_DEPS") == "1";
            var install = withDeps ? new[] { "install", "--with-deps", "chromium" } : new[] { "install", "chromium" };
            Assert.Equal(0, Microsoft.Playwright.Program.Main(install));

            var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
            var browser = await playwright.Chromium.LaunchAsync(new() { Args = browserArgs });
            var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1280, Height = 900 } });
            page.Console += (_, message) => output.WriteLine("console: " + message.Text);
            page.PageError += (_, error) => output.WriteLine("page error: " + error);
            await OpenEditor(page);
            return new Session(playwright, browser, page);
        }

        /// <summary>Opens the editor of the served site and waits for the dock layout.</summary>
        public static async Task OpenEditor(IPage page)
        {
            var baseUrl = Environment.GetEnvironmentVariable(PlaywrightFactAttribute.BaseUrlVariable)!;

            // NOTE: The test server has no fallback to index.html, so the test serves it for the editor route.
            await page.RouteAsync(baseUrl + "editor", async route =>
                await route.FulfillAsync(new() { Response = await route.FetchAsync(new() { Url = baseUrl }) }));
            await page.GotoAsync(baseUrl + "editor");

            // NOTE: The test server of scripts/playwright.sh has a short listen queue. It can reset a request in the burst of the
            // first page load, and the runtime then does not start. One reload gets the files from a quiet server.
            try
            {
                await page.Locator(".dock-layout").WaitForAsync(new() { Timeout = 30_000 });
            }
            catch (TimeoutException)
            {
                await page.ReloadAsync();
                await page.Locator(".dock-layout").WaitForAsync(new() { Timeout = 60_000 });
            }
        }

        public async ValueTask DisposeAsync()
        {
            await browser.DisposeAsync();
            playwright.Dispose();
        }
    }
}
