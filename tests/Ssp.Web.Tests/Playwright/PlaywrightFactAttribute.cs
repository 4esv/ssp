namespace Ssp.Web.Tests.Playwright;

/// <summary>Runs only when SSP_BASE_URL names a served copy of the published site.</summary>
public sealed class PlaywrightFactAttribute : FactAttribute
{
    public const string BaseUrlVariable = "SSP_BASE_URL";

    public PlaywrightFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(BaseUrlVariable)))
        {
            Skip = $"{BaseUrlVariable} is not set. Run scripts/playwright.sh.";
        }
    }
}
