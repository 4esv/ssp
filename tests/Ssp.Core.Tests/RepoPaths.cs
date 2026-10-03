namespace Ssp.Core.Tests;

/// <summary>
/// Finds paths in the source tree from the test output directory.
/// </summary>
internal static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ssp.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("ssp.sln not found above " + AppContext.BaseDirectory);
    }
}
