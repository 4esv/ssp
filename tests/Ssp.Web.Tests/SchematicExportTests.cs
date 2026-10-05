using System.Text;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Ssp.Web.Components;
using Ssp.Web.Hosting;
using Ssp.Web.Pages;
using Ssp.Web.Tests.Playwright;

namespace Ssp.Web.Tests;

public class SchematicExportTests : BunitContext
{
    public SchematicExportTests() => Services.AddSingleton<TimeProvider>(new ManualTimeProvider());

    const string DataPrefix = "data:image/svg+xml;base64,";

    static string Fixture(string name) => File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", name));

    // NOTE: The download is a data URL, so the file content is the decoded link target.
    static string FileContent(string href)
    {
        Assert.StartsWith(DataPrefix, href);
        return Encoding.UTF8.GetString(Convert.FromBase64String(href[DataPrefix.Length..]));
    }

    [Fact]
    public void SvgDownloadIsTheSchematic()
    {
        Services.AddSingleton<ISimulationHost, InProcessSimulationHost>();
        var page = Render<Editor>();
        page.Find("textarea").Input(Fixture("divider-basic.cir"));

        JSInterop.Mode = JSRuntimeMode.Loose;
        var save = JSInterop.SetupVoid("save", _ => true);
        page.Find("button.svg-download").Click();

        var call = Assert.Single(save.Invocations);
        Assert.Equal("schematic.svg", call.Arguments[0]);
        var file = FileContent((string)call.Arguments[1]!);
        Assert.Equal(SchematicView.Svg(Fixture("divider-basic.cir")), file);
        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\"", file);
    }

    [Fact]
    public void NoSchematicGivesNoDownload()
    {
        var export = Render<SchematicExport>(p => p.Add(c => c.Netlist, "R1 a\n.END\n"));

        Assert.Empty(export.FindAll("button.svg-download"));
    }

    [PlaywrightFact]
    public async Task DownloadedSvgOpensInBrowser()
    {
        var baseUrl = Environment.GetEnvironmentVariable(PlaywrightFactAttribute.BaseUrlVariable)!;
        Assert.Equal(0, Program.Main(["install", "chromium"]));

        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        // NOTE: A static server has no fallback for app routes, so load the root and navigate in the app.
        await page.GotoAsync(baseUrl);
        await Assertions.Expect(page.Locator("pre")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await page.EvaluateAsync("Blazor.navigateTo('editor')");
        await page.Locator("textarea").FillAsync(Fixture("divider-basic.cir"));

        var download = await page.RunAndWaitForDownloadAsync(() => page.Locator("button.svg-download").ClickAsync());
        Assert.Equal("schematic.svg", download.SuggestedFilename);
        // NOTE: The browser picks the content type from the file extension.
        var path = Path.Combine(Path.GetTempPath(), $"ssp-{Guid.NewGuid():N}.svg");
        await download.SaveAsAsync(path);

        // NOTE: A browser shows an XML error as a parsererror element in place of the SVG.
        var errors = new List<string>();
        var view = await browser.NewPageAsync();
        view.Console += (_, m) => { if (m.Type == "error") errors.Add(m.Text); };
        await view.GotoAsync("file://" + path, new() { WaitUntil = WaitUntilState.Load });
        Assert.Equal("svg", await view.EvaluateAsync<string>("document.documentElement.localName"));
        Assert.Equal(0, await view.Locator("parsererror").CountAsync());
        Assert.Equal(3, await view.Locator("g[data-ref]").CountAsync());
        Assert.Empty(errors);
        File.Delete(path);
    }
}
