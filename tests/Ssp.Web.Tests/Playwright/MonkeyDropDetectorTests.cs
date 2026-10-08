using System.Reflection;

namespace Ssp.Web.Tests.Playwright;

// NOTE: #310: the monkey tells a server drop from an editor crash with MonkeyPlaywrightTests.IsDroppedDownload. The helper is private,
// so the test calls it by reflection, as SchematicEditsStaleLayoutTests does, rather than widening the class for a test.
public class MonkeyDropDetectorTests
{
    static readonly MethodInfo Detector =
        typeof(MonkeyPlaywrightTests).GetMethod("IsDroppedDownload", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("MonkeyPlaywrightTests no longer has the static IsDroppedDownload helper.");

    static bool Dropped(string failure) => (bool)Detector.Invoke(null, [failure])!;

    [Theory]
    [InlineData("seed 1: request failed: http://127.0.0.1:5099/_framework/dotnet.runtime.js net::ERR_CONNECTION_RESET")]
    [InlineData("seed 32: the editor did not load in 3 tries. request failed: http://127.0.0.1:5099/css/print.css net::ERR_CONNECTION_RESET")]
    public void A_reset_on_a_static_download_is_an_environment_drop(string failure) => Assert.True(Dropped(failure));

    [Theory]
    [InlineData("seed 77 (fuzz-transistor-diode.cir) failed after 1 actions: Argument_AddingDuplicateWithKey")]
    [InlineData("seed 77 (fuzz-transistor-diode.cir) failed after 1 actions: #blazor-error-ui is shown. console.error: System.ArgumentException: Argument_AddingDuplicateWithKey")]
    // NOTE: A dropped download that coincides with a crash must not turn the crash into an inconclusive seed.
    [InlineData("seed 77 (fuzz-transistor-diode.cir) failed after 1 actions: #blazor-error-ui is shown. request failed: http://127.0.0.1:5099/_framework/dotnet.wasm net::ERR_CONNECTION_RESET")]
    public void An_editor_crash_is_not_an_environment_drop(string failure) => Assert.False(Dropped(failure));
}
