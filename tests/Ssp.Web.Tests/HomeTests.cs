using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Web.Hosting;
using Ssp.Web.Pages;

namespace Ssp.Web.Tests;

public class HomeTests : BunitContext
{
    [Fact]
    public void HomeShowsEngineVersions()
    {
        Services.AddSingleton<ISimulationHost, InProcessSimulationHost>();

        var page = Render<Home>();

        Assert.Contains("SpiceSharp 3.2.3", page.Markup);
    }

    [Fact]
    public void HomeLinksToEditorAndGallery()
    {
        Services.AddSingleton<ISimulationHost, InProcessSimulationHost>();

        var page = Render<Home>();

        var hrefs = page.FindAll("a").Select(a => a.GetAttribute("href")).ToList();
        Assert.Contains("editor", hrefs);
        Assert.Contains("gallery", hrefs);
    }
}
