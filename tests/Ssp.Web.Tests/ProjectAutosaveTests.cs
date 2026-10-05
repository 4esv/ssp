using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Ssp.Core.Layout;
using Ssp.Web.Components;
using Ssp.Web.Hosting;
using Ssp.Web.Pages;
using Ssp.Web.Projects;
using Ssp.Web.Schematic;
using Ssp.Web.Sharing;
using LayoutDoc = Ssp.Core.Layout.Layout;

namespace Ssp.Web.Tests;

public class ProjectAutosaveTests : BunitContext
{
    static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);

    readonly ManualTimeProvider time = new();
    readonly MemoryStore storage = new();

    public ProjectAutosaveTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<TimeProvider>(time);
        Services.AddSingleton<ISimulationHost, InProcessSimulationHost>();
        Services.AddSingleton<IJSRuntime>(new BrowserStorage(JSInterop.JSRuntime, storage));
    }

    /// <summary>Gives localStorage from a <see cref="MemoryStore"/>. Each other call goes to the bUnit runtime.</summary>
    sealed class BrowserStorage(IJSRuntime inner, MemoryStore store) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            switch (identifier)
            {
                case "localStorage.getItem": return (TValue)(object)(await store.Get((string)args![0]!))!;
                case "localStorage.setItem": await store.Set((string)args![0]!, (string)args[1]!); return default!;
                case "localStorage.removeItem": await store.Remove((string)args![0]!); return default!;
                default: return await inner.InvokeAsync<TValue>(identifier, cancellationToken, args);
            }
        }
    }

    static string Fixture(string name) => File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", name));

    static readonly LayoutDoc HandLayout = new(
        [new PartPlacement("V1", 0, 0, 0, false), new PartPlacement("R1", 120, 40, 90, false), new PartPlacement("R2", 240, 40, 270, true)],
        [new WireRoute("out", [new Point(120, 80), new Point(240, 80)])]);

    IEnumerable<string> EntryWrites() => storage.Writes.Where(k => k.StartsWith("ssp.project.", StringComparison.Ordinal));

    async Task Draw(IRenderedComponent<Editor> page, string netlist, LayoutDoc layout)
    {
        var editor = page.FindComponent<SchematicEditor>();
        await page.InvokeAsync(() => editor.Instance.Changed.InvokeAsync(new SchematicChange(netlist, layout)));
    }

    async Task<IRenderedComponent<Editor>> Reload()
    {
        await DisposeComponentsAsync();
        return Render<Editor>();
    }

    [Fact]
    public async Task EditThenReloadGivesTheSameCircuitAndLayout()
    {
        var page = Render<Editor>();
        await Draw(page, Fixture("divider-basic.cir"), HandLayout);
        time.Advance(Debounce);

        var again = await Reload();

        again.WaitForAssertion(() => Assert.Equal(Fixture("divider-basic.cir"), again.Find("textarea").GetAttribute("value")));
        Assert.Equal(HandLayout, again.FindComponent<SchematicEditor>().Instance.Layout);
    }

    [Fact]
    public async Task TypingThenReloadGivesTheSameText()
    {
        var page = Render<Editor>();
        page.Find("textarea").Input(Fixture("divider-basic.cir"));
        time.Advance(Debounce);

        var again = await Reload();

        again.WaitForAssertion(() => Assert.Equal(Fixture("divider-basic.cir"), again.Find("textarea").GetAttribute("value")));
    }

    [Fact]
    public async Task OneWriteForABurstOfEdits()
    {
        var page = Render<Editor>();
        var text = Fixture("divider-basic.cir");
        await page.InvokeAsync(() => page.Find("textarea").Input("* a\n" + text));
        time.Advance(TimeSpan.FromMilliseconds(300));
        await page.InvokeAsync(() => page.Find("textarea").Input("* ab\n" + text));
        time.Advance(TimeSpan.FromMilliseconds(300));
        await page.InvokeAsync(() => page.Find("textarea").Input("* abc\n" + text));
        time.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Empty(EntryWrites());

        time.Advance(Debounce);

        page.WaitForAssertion(() => Assert.Single(EntryWrites()));
        time.Advance(Debounce);
        Assert.Single(EntryWrites());
    }

    [Fact]
    public async Task ABurstThatEndsAtTheSavedTextWritesNothing()
    {
        var page = Render<Editor>();
        var text = Fixture("divider-basic.cir");
        await page.InvokeAsync(() => page.Find("textarea").Input(text));
        time.Advance(Debounce);
        page.WaitForAssertion(() => Assert.Single(EntryWrites()));

        await page.InvokeAsync(() => page.Find("textarea").Input("* a\n" + text));
        await page.InvokeAsync(() => page.Find("textarea").Input(text));
        time.Advance(Debounce);

        Assert.Single(EntryWrites());
    }

    [Fact]
    public async Task ASharedLinkOpensAsANewProjectAndKeepsTheOpenOne()
    {
        var page = Render<Editor>();
        await Draw(page, Fixture("divider-basic.cir"), HandLayout);
        time.Advance(Debounce);
        page.WaitForAssertion(() => Assert.Single(EntryWrites()));
        var open = storage.Items[ProjectStore.OpenKey];
        var saved = storage.Items[ProjectStore.EntryKey(open)];

        var shared = Fixture("rc-lowpass.cir");
        Services.GetRequiredService<NavigationManager>().NavigateTo("editor#" + ShareCodec.Encode(shared));
        var linked = await Reload();
        Assert.Equal(shared, linked.Find("textarea").GetAttribute("value"));
        time.Advance(Debounce);
        Assert.Equal(open, storage.Items[ProjectStore.OpenKey]);
        Assert.Equal(saved, storage.Items[ProjectStore.EntryKey(open)]);

        linked.Find("textarea").Input("* mine\n" + shared);
        time.Advance(Debounce);

        linked.WaitForAssertion(() => Assert.Equal(2, EntryWrites().Distinct().Count()));
        Assert.Equal(saved, storage.Items[ProjectStore.EntryKey(open)]);
    }

    [Fact]
    public void ACorruptOpenProjectIsNotFatal()
    {
        storage.Items[ProjectStore.OpenKey] = "broken";
        storage.Items[ProjectStore.EntryKey("broken")] = "{\"netlist\":";

        var page = Render<Editor>();

        Assert.Contains("R1", page.Find("textarea").GetAttribute("value"));
    }

    [Fact]
    public void TheProjectListOpensRenamesDuplicatesAndDeletes()
    {
        var page = Render<Editor>();
        page.Find("textarea").Input(Fixture("divider-basic.cir"));
        time.Advance(Debounce);
        page.Find(".project-name").Change("Divider");
        page.Find("button.projects-toggle").Click();
        page.WaitForAssertion(() => Assert.Equal(["Divider"], page.FindAll(".project-list li .project-open").Select(b => b.TextContent.Trim())));

        page.Find(".project-list li .project-duplicate").Click();
        page.WaitForAssertion(() => Assert.Equal(["Divider", "Divider copy"], page.FindAll(".project-list li .project-open").Select(b => b.TextContent.Trim()).Order()));

        page.FindAll(".project-list li").Single(li => li.TextContent.Contains("copy")).QuerySelector(".project-delete")!.Click();
        page.WaitForAssertion(() => Assert.Equal(["Divider"], page.FindAll(".project-list li .project-open").Select(b => b.TextContent.Trim())));
    }
}
