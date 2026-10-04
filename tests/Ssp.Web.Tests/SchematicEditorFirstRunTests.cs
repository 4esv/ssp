using Bunit;
using Ssp.Web.Components;
using Ssp.Web.Schematic;

namespace Ssp.Web.Tests;

public class SchematicEditorFirstRunTests : BunitContext
{
    const string Key = "ssp.hints";
    const string TwoResistors = "* two resistors\nV1 in 0 1\nR1 in a 1k\nR2 b 0 1k\n.END\n";

    string? stored;

    IRenderedComponent<SchematicEditor> Open()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<string?>("localStorage.getItem", Key).SetResult(stored);
        JSInterop.SetupVoid("localStorage.setItem", i => i.Arguments[0] as string == Key).SetVoidResult();
        return Render<SchematicEditor>(p => p
            .Add(c => c.Netlist, TwoResistors)
            .Add(c => c.Changed, (SchematicChange _) => { }));
    }

    static string[] Shown(IRenderedComponent<SchematicEditor> e) => e.FindAll("ul.first-run-hints li").Select(l => l.GetAttribute("data-hint")!).ToArray();

    void Save() => stored = (string?)JSInterop.Invocations
        .Where(i => i.Identifier == "localStorage.setItem").Last().Arguments[1];

    [Fact]
    public void FirstVisitShowsThreeHints() =>
        Assert.Equal(["place", "pin", "select"], Shown(Open()));

    [Fact]
    public void ADoneHintIsDismissedAndStaysDismissedAfterReload()
    {
        var editor = Open();
        editor.Find("circle.pin[data-ref=\"R1\"][data-pin=\"0\"]").Click();
        Assert.Equal(["place", "select"], Shown(editor));
        Save();

        Assert.Equal(["place", "select"], Shown(Open()));
    }

    [Fact]
    public void EachHintGoesWhenItsActionIsDone()
    {
        var editor = Open();
        editor.Find("rect.part[data-ref=\"R1\"]").Click();
        Assert.Equal(["place", "pin"], Shown(editor));
        editor.Find("button.palette-item[data-kind=resistor]").Click();
        Assert.Equal(["pin"], Shown(editor));
        Save();

        var again = Open();
        Assert.Equal(["pin"], Shown(again));
    }

    [Fact]
    public void AllDismissedShowsNoList()
    {
        stored = "place,pin,select";
        Assert.Empty(Open().FindAll("ul.first-run-hints"));
    }
}
