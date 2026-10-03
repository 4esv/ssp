namespace Ssp.Web.Tests;

/// <summary>
/// Compares text to a golden file in tests/Ssp.Web.Tests/Golden/&lt;name&gt;.
/// Set UPDATE_GOLDEN=1 to rewrite the golden file with the actual text.
/// </summary>
public static class Golden
{
    public static string Directory { get; } = Path.Combine(RepoPaths.Root, "tests", "Ssp.Web.Tests", "Golden");

    public static void Assert(string name, string actual)
    {
        var path = Path.Combine(Directory, name);
        var normalized = Normalize(actual);

        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(path, normalized);
            return;
        }

        if (!File.Exists(path))
        {
            throw new Xunit.Sdk.XunitException($"Golden file {path} does not exist. Run the tests with UPDATE_GOLDEN=1 to create it.");
        }

        Xunit.Assert.Equal(Normalize(File.ReadAllText(path)), normalized);
    }

    // NOTE: Git can check out files with CRLF on Windows. Compare with LF only.
    private static string Normalize(string text) => text.Replace("\r\n", "\n");
}
