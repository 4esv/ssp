namespace Ssp.Cli.Tests;

public class VersionTests
{
    [Fact]
    public void VersionExitsZeroAndListsEngine()
    {
        using var output = new StringWriter();

        var exitCode = Program.Run(["--version"], output);

        Assert.Equal(0, exitCode);
        var text = output.ToString();
        Assert.StartsWith("ssp ", text);
        Assert.Contains("SpiceSharp 3.2.3", text);
    }
}
