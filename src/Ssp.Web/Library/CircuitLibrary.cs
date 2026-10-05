using System.Text.Json;
using Ssp.Web.Projects;

namespace Ssp.Web.Library;

/// <summary>One circuit from <c>circuits/library/</c>.</summary>
public sealed record LibraryCircuit(string FileName, string Title, string Netlist);

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
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(n =>
            {
                var netlist = Read(n)!;
                var fileName = n[Prefix.Length..];
                return new LibraryCircuit(fileName, Title(netlist) ?? fileName, netlist);
            })
            .ToList();
    }

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
