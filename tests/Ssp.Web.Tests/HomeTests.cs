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
}
