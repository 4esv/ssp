using Ssp.Web.Projects;

namespace Ssp.Web.Tests;

/// <summary>A browser storage in memory. It keeps the key of each write.</summary>
public sealed class MemoryStore : IKeyValueStore
{
    public readonly Dictionary<string, string> Items = [];
    public readonly List<string> Writes = [];

    public ValueTask<string?> Get(string key) => ValueTask.FromResult(Items.TryGetValue(key, out var v) ? v : null);

    public ValueTask Set(string key, string value)
    {
        Writes.Add(key);
        Items[key] = value;
        return ValueTask.CompletedTask;
    }

    public ValueTask Remove(string key)
    {
        Items.Remove(key);
        return ValueTask.CompletedTask;
    }
}

public class ProjectStoreTests
{
    const string Layout = "[[part]]\nref = \"R1\"\nx = 1.0\ny = 2.0\nrotation = 0\nflip = false\n\n";

    readonly MemoryStore storage = new();
    readonly ManualTimeProvider time = new();
    ProjectStore Store() => new(storage, time);

    [Fact]
    public async Task SaveThenLoadGivesTheSameCircuitAndLayout()
    {
        var project = ProjectStore.New("Fuzz", "R1 a 0 1k\n", Layout);
        await Store().Save(project);

        var loaded = await Store().Load(project.Id);

        Assert.NotNull(loaded);
        Assert.Equal(("Fuzz", "R1 a 0 1k\n", Layout), (loaded.Name, loaded.Netlist, loaded.Layout));
    }

    [Fact]
    public async Task RenameChangesTheNameOnly()
    {
        var store = Store();
        var project = ProjectStore.New("Fuzz", "R1 a 0 1k\n", Layout);
        await store.Save(project);
        var before = await store.Load(project.Id);

        await store.Rename(project.Id, "Big fuzz");

        var after = await store.Load(project.Id);
        Assert.Equal(before! with { Name = "Big fuzz" }, after);
        Assert.Single(await store.List());
    }

    [Fact]
    public async Task DuplicateChangesTheNameOnly()
    {
        var store = Store();
        var project = ProjectStore.New("Fuzz", "R1 a 0 1k\n", Layout);
        await store.Save(project);

        var copy = await store.Duplicate(project.Id);

        Assert.NotNull(copy);
        Assert.NotEqual(project.Id, copy.Id);
        Assert.Equal("Fuzz copy", copy.Name);
        Assert.Equal((project.Netlist, project.Layout), (copy.Netlist, copy.Layout));
        Assert.Equal(["Fuzz", "Fuzz copy"], (await store.List()).Select(p => p.Name).Order());
        Assert.Equal("Fuzz", (await store.Load(project.Id))!.Name);
    }

    [Fact]
    public async Task ACorruptEntryIsSkipped()
    {
        var store = Store();
        var good = ProjectStore.New("Good", "R1 a 0 1k\n", null);
        var bad = ProjectStore.New("Bad", "R1 a 0 1k\n", null);
        await store.Save(good);
        await store.Save(bad);
        storage.Items[ProjectStore.EntryKey(bad.Id)] = "{not json";
        storage.Items[ProjectStore.EntryKey("gone")] = "{\"name\":null}";
        storage.Items[ProjectStore.IndexKey] = storage.Items[ProjectStore.IndexKey].Replace("]", ",\"gone\",\"missing\"]");

        var list = await store.List();

        Assert.Equal(["Good"], list.Select(p => p.Name));
        Assert.Null(await store.Load(bad.Id));
    }

    [Fact]
    public async Task ACorruptIndexIsSkipped()
    {
        storage.Items[ProjectStore.IndexKey] = "[[[";

        Assert.Empty(await Store().List());
        await Store().Save(ProjectStore.New("Fresh", "R1 a 0 1k\n", null));
        Assert.Equal(["Fresh"], (await Store().List()).Select(p => p.Name));
    }

    [Fact]
    public async Task DeleteRemovesTheEntryAndTheOpenMark()
    {
        var store = Store();
        var project = ProjectStore.New("Fuzz", "R1 a 0 1k\n", null);
        await store.Save(project);
        await store.SetOpen(project.Id);

        await store.Delete(project.Id);

        Assert.Empty(await store.List());
        Assert.Null(await store.OpenId());
        Assert.DoesNotContain(storage.Items.Keys, k => k == ProjectStore.EntryKey(project.Id));
    }

    [Fact]
    public async Task ProjectKeysStayApartFromTheDockLayout()
    {
        storage.Items["ssp.dock.editor.v2"] = "dock";
        var store = Store();
        var project = ProjectStore.New("Fuzz", "R1 a 0 1k\n", null);
        await store.Save(project);
        await store.SetOpen(project.Id);
        await store.Delete(project.Id);

        Assert.Equal("dock", storage.Items["ssp.dock.editor.v2"]);
        Assert.All(storage.Items.Keys.Where(k => k != "ssp.dock.editor.v2"), k => Assert.StartsWith("ssp.project", k));
    }
}
