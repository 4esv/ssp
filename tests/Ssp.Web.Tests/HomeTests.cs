using Bunit;
using Ssp.Web.Pages;

namespace Ssp.Web.Tests;

public class HomeTests : BunitContext
{
    [Fact]
    public void HomeShowsEngineVersions()
    {
        var page = Render<Home>();

        Assert.Contains("SpiceSharp 3.2.3", page.Markup);
    }
}
