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
    // NOTE: The editor redraws the schematic 100 ms after the last key in the netlist box, so a test that types moves the clock.
    readonly ManualTimeProvider time = new();

    public SchematicExportTests() => Services.AddSingleton<TimeProvider>(time);

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
        time.Advance(TimeSpan.FromMilliseconds(100));

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

    [Fact]
    public void PdfDownloadIsAPdfWithATitleBlock()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var save = JSInterop.SetupVoid("save", _ => true);
        var export = Render<SchematicExport>(p => p.Add(c => c.Netlist, Fixture("divider-basic.cir")));

        export.Find("button.pdf-download").Click();

        var call = Assert.Single(save.Invocations);
        Assert.Equal("schematic.pdf", call.Arguments[0]);
        var href = (string)call.Arguments[1]!;
        Assert.StartsWith("data:application/pdf;base64,", href);
        var pdf = Encoding.Latin1.GetString(Convert.FromBase64String(href["data:application/pdf;base64,".Length..]));
        Assert.StartsWith("%PDF-", pdf);
        Assert.Contains("(Resistor divider) Tj", pdf);
    }

    [Fact]
    public void CsvDownloadIsThePartsList()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var save = JSInterop.SetupVoid("save", _ => true);
        var export = Render<SchematicExport>(p => p.Add(c => c.Netlist, Fixture("divider-basic.cir")));

        export.Find("button.parts-csv").Click();

        var call = Assert.Single(save.Invocations);
        Assert.Equal("parts.csv", call.Arguments[0]);
        var href = (string)call.Arguments[1]!;
        Assert.StartsWith("data:text/csv;base64,", href);
        var csv = Encoding.UTF8.GetString(Convert.FromBase64String(href["data:text/csv;base64,".Length..]));
        Assert.StartsWith("qty,refs,kind,value,footprint,buy link\n", csv);
        Assert.Contains("R1", csv);
    }

    [Fact]
    public void PartsShowOnScreenWithALinkOnEachRow()
    {
        var export = Render<SchematicExport>(p => p.Add(c => c.Netlist, Fixture("divider-basic.cir")));
        Assert.Empty(export.FindAll("table.parts-list"));

        export.Find("button.parts-toggle").Click();

        var rows = export.FindAll("table.parts-list tbody tr");
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.StartsWith("https://", r.QuerySelector("a")!.GetAttribute("href")));
    }

    [Fact]
    public void NoSchematicGivesNoPartsOrPdf()
    {
        var export = Render<SchematicExport>(p => p.Add(c => c.Netlist, "R1 a\n.END\n"));

        Assert.Empty(export.FindAll("button.pdf-download, button.parts-csv, button.parts-toggle"));
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

        await page.Locator("button.export-toggle").ClickAsync();
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
