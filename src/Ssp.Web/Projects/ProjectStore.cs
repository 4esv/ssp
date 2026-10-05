using System.Text.Json;
using Microsoft.JSInterop;

namespace Ssp.Web.Projects;

/// <summary>A store of text by key. In the browser this is <c>localStorage</c>.</summary>
public interface IKeyValueStore
{
    ValueTask<string?> Get(string key);

    ValueTask Set(string key, string value);

    ValueTask Remove(string key);
}

/// <summary>The browser <c>localStorage</c>.</summary>
/// <remarks>NOTE: In WebAssembly the calls are in process, so the editor opens the saved project before its first render.</remarks>
public sealed class LocalStorage(IJSRuntime js) : IKeyValueStore
{
    public ValueTask<string?> Get(string key) => js is IJSInProcessRuntime local
        ? ValueTask.FromResult(local.Invoke<string?>("localStorage.getItem", key))
        : js.InvokeAsync<string?>("localStorage.getItem", key);

    public ValueTask Set(string key, string value) => Call("localStorage.setItem", key, value);

    public ValueTask Remove(string key) => Call("localStorage.removeItem", key);

    ValueTask Call(string identifier, params object[] args)
    {
        if (js is not IJSInProcessRuntime local) return js.InvokeVoidAsync(identifier, args);
        local.InvokeVoid(identifier, args);
        return ValueTask.CompletedTask;
    }
}

/// <summary>A saved circuit: the netlist and the text of its layout file (see docs/file-format.md).</summary>
public sealed record Project(string Id, string Name, string Netlist, string? Layout, DateTimeOffset Saved);

/// <summary>
/// The projects in browser storage. Each project is one entry. An index holds the ids, and one more key holds the id
/// of the open project. A corrupt entry or a corrupt index is skipped, not fatal.
/// </summary>
/// <remarks>NOTE: All keys start with <c>ssp.project</c>, so they stay apart from the saved dock layout.</remarks>
public sealed class ProjectStore(IKeyValueStore storage, TimeProvider time)
{
    public const string IndexKey = "ssp.projects";
    public const string OpenKey = "ssp.projects.open";

    public static string EntryKey(string id) => "ssp.project." + id;

    sealed record Entry(string Name, string Netlist, string? Layout, DateTimeOffset Saved);

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A new project that is not saved yet.</summary>
    public static Project New(string name, string netlist, string? layout) =>
        new(Guid.NewGuid().ToString("N")[..12], name, netlist, layout, default);

    /// <summary>The projects that read correctly, the last saved first.</summary>
    public async Task<IReadOnlyList<Project>> List()
    {
        var projects = new List<Project>();
        foreach (var id in await Ids())
        {
            if (await Load(id) is { } project) projects.Add(project);
        }
        return projects.OrderByDescending(p => p.Saved).ToList();
    }

    /// <summary>The project, or null if it is missing or corrupt.</summary>
    public async Task<Project?> Load(string id)
    {
        try
        {
            var entry = JsonSerializer.Deserialize<Entry>(await storage.Get(EntryKey(id)) ?? "null", Json);
            return entry is { Name: not null, Netlist: not null } ? new Project(id, entry.Name, entry.Netlist, entry.Layout, entry.Saved) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes the project and gives it with the time of the save.</summary>
    public async Task<Project> Save(Project project)
    {
        var saved = project with { Saved = time.GetUtcNow() };
        await Write(saved);
        return saved;
    }

    /// <summary>Changes the name only. The netlist, the layout and the time of the save stay.</summary>
    public async Task<Project?> Rename(string id, string name)
    {
        if (await Load(id) is not { } project) return null;
        var renamed = project with { Name = name };
        await Write(renamed);
        return renamed;
    }

    /// <summary>Saves a copy with a new id and the name "&lt;name&gt; copy".</summary>
    public async Task<Project?> Duplicate(string id)
    {
        if (await Load(id) is not { } project) return null;
        return await Save(New(project.Name + " copy", project.Netlist, project.Layout));
    }

    public async Task Delete(string id)
    {
        await storage.Remove(EntryKey(id));
        var ids = await Ids();
        if (ids.Remove(id)) await storage.Set(IndexKey, JsonSerializer.Serialize(ids));
        if (await OpenId() == id) await storage.Remove(OpenKey);
    }

    public async Task<string?> OpenId() => await storage.Get(OpenKey);

    public async Task SetOpen(string id)
    {
        if (await OpenId() != id) await storage.Set(OpenKey, id);
    }

    async Task Write(Project project)
    {
        await storage.Set(EntryKey(project.Id), JsonSerializer.Serialize(new Entry(project.Name, project.Netlist, project.Layout, project.Saved), Json));
        var ids = await Ids();
        if (!ids.Contains(project.Id))
        {
            ids.Add(project.Id);
            await storage.Set(IndexKey, JsonSerializer.Serialize(ids));
        }
    }

    async Task<List<string>> Ids()
    {
        try
        {
            return JsonSerializer.Deserialize<List<string?>>(await storage.Get(IndexKey) ?? "[]")?.OfType<string>().ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
