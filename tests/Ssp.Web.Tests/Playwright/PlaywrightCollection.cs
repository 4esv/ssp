namespace Ssp.Web.Tests.Playwright;

/// <summary>
/// The Playwright test classes run one at a time. Each one installs Chromium, and on Linux two installs at the same
/// time compete for the package lock.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PlaywrightCollection
{
    public const string Name = "Playwright";
}
