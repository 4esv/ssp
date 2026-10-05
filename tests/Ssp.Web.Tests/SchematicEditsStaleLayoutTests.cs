using System.Reflection;
using Bunit;
using Ssp.Web.Components;
using Ssp.Core.Layout;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Web.Schematic;
using LayoutDoc = Ssp.Core.Layout.Layout;
using Point = Ssp.Core.Layout.Point;

namespace Ssp.Web.Tests;

// NOTE: The netlist text changes from outside the schematic (#288). Each edit must meet a layout that agrees with the netlist.
public class SchematicEditsStaleLayoutTests : BunitContext
{
    const string Before = "* amp\nV1 in 0 1\nR1 in a 1k\nR2 a 0 1k\nQ1 a in 0 QNPN\n.model QNPN NPN (IS=1e-14 BF=200)\n.END\n";
    const string After = "* amp\nV1 in 0 1\nR1 in a 1k\nR2 a 0 1k\n.END\n";
    static readonly PartsTable Table = Ssp.Core.Parts.Parts.Parse(
        File.ReadAllText(Path.Combine(RepoPaths.Root, "models", "parts.toml")), "parts.toml");

    static LayoutDoc Stale()
    {
        var circuit = NetlistLoader.Load(Before);
        return AutoPlacer.Place(circuit, circuit.Directives);
    }

    static IEnumerable<MethodInfo> EditFunctions() => typeof(SchematicEdits).GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Where(m => m.GetParameters().Select(p => p.Name).Take(2).SequenceEqual(["netlist", "layout"]))
        .Where(m => m.ReturnType == typeof(SchematicChange) || m.ReturnType.FullName!.StartsWith("System.ValueTuple") && m.ReturnType.GetGenericArguments()[0] == typeof(SchematicChange));

    static object? Argument(ParameterInfo p, string netlist, LayoutDoc layout, PartMap parts, Clip clip) => (p.Name, p.ParameterType) switch
    {
        ("netlist", _) => netlist,
        ("layout", _) => layout,
        (_, var t) when t == typeof(PartMap) => parts,
        (_, var t) when t == typeof(Clip) => clip,
        // NOTE: A new transistor takes the reference Q1, the one that the stale layout still holds.
        ("kind", _) => "npn",
        ("reference", _) => "R1",
        ("references", _) => new[] { "R1" },
        ("value", _) => "2k",
        ("node", _) => "a",
        ("name", _) => "sig",
        (_, var t) when t == typeof(Point) => new Point(100, 100),
        (_, var t) when t == typeof(Spot) => new Spot(new Point(100, 100)),
        (_, var t) when t == typeof(PinRef) => new PinRef("R1", 0),
        (_, var t) when t == typeof(Direction) => Direction.Right,
        (_, var t) when t == typeof(bool) => true,
        (_, var t) when t == typeof(double) => 20.0,
        (_, var t) when t == typeof(int) => 0,
        (_, var t) when t == typeof(int?) => null,
        (_, var t) when t == typeof(string) => "R1",
        (_, var t) => throw new InvalidOperationException($"No argument for {p.Name} of {t} in a stale-layout test. Add one."),
    };

    static void Call(MethodInfo method, string netlist, LayoutDoc layout)
    {
        var circuit = NetlistLoader.Load(netlist);
        var parts = PartMap.Resolve(circuit, Table);
        var clip = SchematicEdits.Copy(netlist, layout, parts, ["R1"]);
        var args = method.GetParameters().Select(p =>
            p.Name == "kind" && method.Name == "PlaceJack" ? "jack-in"
            : p.Name == "value" && p.HasDefaultValue ? null
            : Argument(p, netlist, layout, parts, clip)).ToArray();
        try { method.Invoke(null, args); }
        catch (TargetInvocationException e) { throw new Xunit.Sdk.XunitException($"{method.Name} threw {e.InnerException}"); }
    }

    [Fact]
    public void The_edit_functions_are_listed_by_reflection()
    {
        var names = EditFunctions().Select(m => m.Name).ToHashSet();
        foreach (var expected in new[] { "Place", "PlaceAt", "Drag", "Delete", "Duplicate", "Rotate", "Flip", "Paste", "Move" })
            Assert.Contains(expected, names);
    }

    [Fact]
    public void Reconcile_drops_the_parts_that_the_netlist_lacks_and_keeps_the_others()
    {
        var stale = Stale();
        Assert.Contains(stale.Parts, p => p.Reference == "Q1");

        var layout = SchematicEdits.Reconcile(After, stale);

        Assert.DoesNotContain(layout.Parts, p => p.Reference == "Q1");
        Assert.Equal(stale.Parts.Where(p => p.Reference != "Q1"), layout.Parts);
        Assert.Same(layout, SchematicEdits.Reconcile(After, layout));
    }

    [Fact]
    public void Reconcile_keeps_the_layout_of_a_netlist_with_an_error()
    {
        var stale = Stale();
        Assert.Same(stale, SchematicEdits.Reconcile("* bad\nR1 a\n.END\n", stale));
    }

    [Fact]
    public void No_edit_function_throws_on_a_reconciled_layout()
    {
        var layout = SchematicEdits.Reconcile(After, Stale());
        foreach (var method in EditFunctions()) Call(method, After, layout);
    }

    // NOTE: The mutation check: remove the Reconcile call in SchematicEditor.Build and this test fails.
    [Fact]
    public void The_editor_reconciles_a_layout_when_the_netlist_text_changes_and_drops_the_selection()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var stale = Stale();
        var editor = Render<SchematicEditor>(p => p.Add(c => c.Netlist, Before).Add(c => c.Layout, stale));
        Assert.Contains(editor.Instance.Drawn.Parts, p => p.Reference == "Q1");
        editor.Find("rect[data-ref='Q1']").Click();

        editor.Render(p => p.Add(c => c.Netlist, After).Add(c => c.Layout, stale));

        Assert.DoesNotContain(editor.Instance.Drawn.Parts, p => p.Reference == "Q1");
        Assert.Empty(editor.FindAll("rect[data-ref='Q1']"));
        Assert.DoesNotContain("Q1 selected", editor.Markup);
    }
}
