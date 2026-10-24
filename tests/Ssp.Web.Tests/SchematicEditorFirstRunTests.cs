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

    // #391: the Wire hint says the text of the "pin" step, so that step does not show with the Wire tool.
    [Fact]
    public void FirstVisitShowsTheStepsThatTheToolHintDoesNotSay()
    {
        var editor = Open();
        Assert.Equal(["place", "select"], Shown(editor));

        // With no tool the hint asks for a tool, so all three steps show.
        editor.Find("button.tool[data-tool=wire]").Click();
        Assert.Equal(["place", "pin", "select"], Shown(editor));
    }

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
        // The Wire hint says the "pin" step, so pick no tool to see that step.
        again.Find("button.tool[data-tool=wire]").Click();
        Assert.Equal(["pin"], Shown(again));
    }

    // #391: the Wire hint and the "pin" first step have the same text. The page must show the sentence once.
    [Fact]
    public void TheHintLineAndTheFirstStepsShowEachSentenceOnce()
    {
        var editor = Open();
        Assert.Equal("Tap a pin to see where to go.", editor.Find("p.schematic-hint").TextContent.Trim());

        foreach (var sentence in editor.FindAll("p.schematic-hint, ul.first-run-hints li")
                     .Select(e => e.TextContent)
                     .SelectMany(text => text.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                     .GroupBy(sentence => sentence))
        {
            Assert.Single(sentence);
        }
    }

    [Fact]
    public void AllDismissedShowsNoList()
    {
        stored = "place,pin,select";
        Assert.Empty(Open().FindAll("ul.first-run-hints"));
    }
}
