namespace Ssp.Web.Library;

/// <summary>One circuit from <c>circuits/library/</c>.</summary>
public sealed record LibraryCircuit(string FileName, string Title, string Netlist);

/// <summary>Reads the circuits from <c>circuits/library/</c>. The build embeds them in this assembly.</summary>
public static class CircuitLibrary
{
    const string Prefix = "library/";
    const string TitleTag = "* ssp:title ";

    public static IReadOnlyList<LibraryCircuit> All { get; } = Load();

    static List<LibraryCircuit> Load()
    {
        var assembly = typeof(CircuitLibrary).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(n =>
            {
                using var reader = new StreamReader(assembly.GetManifestResourceStream(n)!);
                var netlist = reader.ReadToEnd();
                var fileName = n[Prefix.Length..];
                return new LibraryCircuit(fileName, Title(netlist) ?? fileName, netlist);
            })
            .ToList();
    }

    static string? Title(string netlist) => netlist.Split('\n')
        .Select(l => l.TrimEnd('\r'))
        .FirstOrDefault(l => l.StartsWith(TitleTag, StringComparison.OrdinalIgnoreCase))?[TitleTag.Length..].Trim();
}
