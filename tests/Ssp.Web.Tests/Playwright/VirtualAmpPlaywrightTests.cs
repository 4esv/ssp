using System.Text.Json;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Ssp.Web.Tests.Playwright;

/// <summary>Clicks Try in the virtual amp panel of the published site.</summary>
[Collection(PlaywrightCollection.Name)]
public class VirtualAmpPlaywrightTests(ITestOutputHelper output)
{
    [PlaywrightFact]
    public async Task TryRendersAnOutputBufferForThePresetChain()
    {
        await using var session = await ClipPlayerTests.Session.Start(output);
        await session.Page.Locator(".dock-tab[data-panel=amp]").ClickAsync();

        await session.Page.Locator(".virtual-amp .clip-player input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = "sine.wav",
            MimeType = "audio/wav",
            Buffer = ClipPlayerTests.Sine(),
        });
        await session.Page.Locator(".virtual-amp button.amp-try").ClickAsync();
        await session.Page.WaitForFunctionAsync("() => globalThis.sspClipBuffer", null, new() { Timeout = 240_000 });

        var buffer = await session.Page.EvaluateAsync<JsonElement>(ClipPlayerTests.Output);
        Assert.True(buffer.GetProperty("length").GetInt32() >= ClipPlayerTests.UploadFrames);
        Assert.Equal(ClipPlayerTests.UploadRate, buffer.GetProperty("rate").GetInt32());
    }
}
