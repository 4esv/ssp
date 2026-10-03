namespace Ssp.Core.Tests;

/// <summary>
/// Reads circuit fixtures from circuits/fixtures.
/// </summary>
public static class Fixtures
{
    public static string Directory { get; } = Path.Combine(RepoPaths.Root, "circuits", "fixtures");

    public static string Read(string name) => File.ReadAllText(Path.Combine(Directory, name));
}
