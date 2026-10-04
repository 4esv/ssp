using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Web.Hosting;
using Ssp.Web.Pages;
using Ssp.Web.Sharing;

namespace Ssp.Web.Tests;

public class EditorStarterTests : BunitContext
{
    static string Fixture(string name) => File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", name));

    IRenderedComponent<Editor> Open(string? hash = null)
    {
        Services.AddSingleton<ISimulationHost, InProcessSimulationHost>();
        if (hash is not null)
        {
            Services.GetRequiredService<BunitNavigationManager>().NavigateTo("editor#" + hash);
        }
        return Render<Editor>();
    }

    [Fact]
    public void OpensWithTheStarterWhenThereIsNoSharedCircuit()
    {
        var page = Open();

        var box = page.Find("textarea").GetAttribute("value") ?? page.Find("textarea").TextContent;
        Assert.NotEmpty(box.Trim());
        Assert.Equal(Fixture("rc-lowpass.cir").ReplaceLineEndings("\n"), box.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void SharedCircuitWinsOverTheStarter()
    {
        var shared = Fixture("divider-basic.cir");

        var page = Open(ShareCodec.Encode(shared));

        var box = page.Find("textarea").GetAttribute("value") ?? page.Find("textarea").TextContent;
        Assert.Equal(shared.ReplaceLineEndings("\n"), box.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void RunOnTheStarterGivesAResult()
    {
        var page = Open();

        page.Find("button.run").Click();

        Assert.NotEmpty(page.FindAll("table.voltages tbody tr"));
    }
}
