using Bunit;

namespace Ssp.Web.Tests;

/// <summary>The flag that says the user has seen the help panel. A test that is not about the panel sets it, so the panel stays shut at the first load.</summary>
public static class HelpSeen
{
    public const string Key = "ssp.help.seen";

    /// <summary>For Playwright: a script that runs before each page load.</summary>
    public const string Script = "try { localStorage.setItem('" + Key + "', '1'); } catch (e) { }";

    public static void Mark(this BunitJSInterop js) => js.Setup<string?>("localStorage.getItem", Key).SetResult("1");
}
