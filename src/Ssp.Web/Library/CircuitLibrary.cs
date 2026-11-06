using System.Collections.Concurrent;
using System.Text.Json;
using Ssp.Core.Netlist;
using Ssp.Web.Projects;
using Ssp.Web.Schematic;

namespace Ssp.Web.Library;

/// <summary>One circuit from <c>circuits/library/</c>: the netlist and the text of its layout file, if it has one.</summary>
public sealed record LibraryCircuit(string FileName, string Title, string Netlist, string? Layout = null);

/// <summary>One ready-made stage from <c>circuits/blocks/</c>: the netlist and the text of its layout file, if it has one.</summary>
public sealed record LibraryBlock(string FileName, string Title, string Netlist, string? Layout);

/// <summary>Reads the circuits from <c>circuits/library/</c> and the blocks from <c>circuits/blocks/</c>. The build embeds them in this assembly.</summary>
public static class CircuitLibrary
{
    const string Prefix = "library/";
    const string BlockPrefix = "blocks/";
    const string TitleTag = "* ssp:title ";

    public static IReadOnlyList<LibraryCircuit> All { get; } = Load();

    public static IReadOnlyList<LibraryBlock> Blocks { get; } = LoadBlocks();

    static List<LibraryCircuit> Load()
    {
        var assembly = typeof(CircuitLibrary).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".cir", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(n =>
            {
                var netlist = Read(n)!;
                var fileName = n[Prefix.Length..];
                return new LibraryCircuit(fileName, Title(netlist) ?? fileName, netlist, Read(n[..^".cir".Length] + ".layout.toml"));
            })
            .ToList();
    }

    /// <summary>
    /// The text of the layout file of the library circuit with this netlist, or with the same parts and nodes, or null. The
    /// gallery opens a library circuit in the editor by its netlist, so the editor finds the hand layout by the netlist.
    /// </summary>
    // NOTE: #449: a shared link of a library circuit after a value edit has the parts and nodes of the library circuit, so it
    // gets the hand layout too. Without it the editor places the parts from nothing, which takes minutes in the browser for a
    // big pedal. The text check comes first, and only a library circuit with the same part names is loaded to compare shapes.
    public static string? LayoutOf(string netlist)
    {
        if (All.FirstOrDefault(c => c.Layout is not null && Same(c.Netlist, netlist)) is { } same) return same.Layout;
        var references = References(netlist);
        var candidates = All.Where(c => c.Layout is not null && References(c.Netlist).SetEquals(references)).ToList();
        if (candidates.Count == 0) return null;
        var circuit = NetlistLoader.Load(netlist);
        if (circuit.Diagnostics.Any(d => d.Severity == Severity.Error)) return null;
        var shape = AutoPlacer.Shape(circuit);
        return candidates.FirstOrDefault(c => Shapes.GetOrAdd(c.FileName, _ => AutoPlacer.Shape(NetlistLoader.Load(c.Netlist))) == shape)?.Layout;
    }

    static readonly ConcurrentDictionary<string, string> Shapes = new(StringComparer.Ordinal);

    // The first word of each line that is not a comment, a directive or a continuation: the part names of the netlist.
    static HashSet<string> References(string netlist) => netlist.Split('\n')
        .Select(l => l.Trim())
        .Where(l => l.Length > 0 && l[0] is not ('*' or '.' or '+' or ';'))
        .Select(l => l.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries)[0])
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    // NOTE: A Windows checkout, the embedded file and a share link can differ in line endings and in trailing spaces.
    static bool Same(string a, string b) => Normal(a) == Normal(b);

    static string Normal(string text) =>
        string.Join('\n', text.ReplaceLineEndings("\n").Split('\n').Select(l => l.TrimEnd())).TrimEnd();

    static List<LibraryBlock> LoadBlocks() => typeof(CircuitLibrary).Assembly.GetManifestResourceNames()
        .Where(n => n.StartsWith(BlockPrefix, StringComparison.Ordinal) && n.EndsWith(".cir", StringComparison.Ordinal))
        .Order(StringComparer.Ordinal)
        .Select(n =>
        {
            var netlist = Read(n)!;
            var fileName = n[BlockPrefix.Length..];
            return new LibraryBlock(fileName, Title(netlist) ?? fileName, netlist, Read(n[..^".cir".Length] + ".layout.toml"));
        })
        .ToList();

    static string? Read(string name)
    {
        using var stream = typeof(CircuitLibrary).Assembly.GetManifestResourceStream(name);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    static string? Title(string netlist) => netlist.Split('\n')
        .Select(l => l.TrimEnd('\r'))
        .FirstOrDefault(l => l.StartsWith(TitleTag, StringComparison.OrdinalIgnoreCase))?[TitleTag.Length..].Trim();
}

/// <summary>A block that the user saved from a selection: a netlist and the text of its layout file.</summary>
public sealed record SavedBlock(string Id, string Name, string Netlist, string? Layout, DateTimeOffset Saved);

/// <summary>
/// My Blocks in browser storage. Each block is one entry, and an index holds the ids. A corrupt entry or a corrupt index
/// is skipped, not fatal.
/// </summary>
/// <remarks>NOTE: The keys start with <c>ssp.block</c>, so they stay apart from the projects and the dock layout.</remarks>
public sealed class BlockStore(IKeyValueStore storage, TimeProvider? time = null)
{
    public const string IndexKey = "ssp.blocks";

    public static string EntryKey(string id) => "ssp.block." + id;

    sealed record Entry(string Name, string Netlist, string? Layout, DateTimeOffset Saved);

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The blocks that read correctly, the last saved first.</summary>
    public async ValueTask<IReadOnlyList<SavedBlock>> List()
    {
        var blocks = new List<SavedBlock>();
        foreach (var id in await Ids())
        {
            try
            {
                var entry = JsonSerializer.Deserialize<Entry>(await storage.Get(EntryKey(id)) ?? "null", Json);
                if (entry is { Name: not null, Netlist: not null }) blocks.Add(new SavedBlock(id, entry.Name, entry.Netlist, entry.Layout, entry.Saved));
            }
            catch (JsonException)
            {
            }
        }
        return blocks.OrderByDescending(b => b.Saved).ToList();
    }

    /// <summary>Writes a new block and adds it to the index.</summary>
    public async ValueTask<SavedBlock> Save(string name, string netlist, string? layout)
    {
        var block = new SavedBlock(Guid.NewGuid().ToString("N")[..12], name, netlist, layout, (time ?? TimeProvider.System).GetUtcNow());
        await storage.Set(EntryKey(block.Id), JsonSerializer.Serialize(new Entry(block.Name, block.Netlist, block.Layout, block.Saved), Json));
        var ids = await Ids();
        ids.Add(block.Id);
        await storage.Set(IndexKey, JsonSerializer.Serialize(ids));
        return block;
    }

    async ValueTask<List<string>> Ids()
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(await storage.Get(IndexKey) ?? "[]")?.Where(id => id is not null).ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
