using Ssp.Web.Docking;

namespace Ssp.Web.Tests;

public class DockModelTests
{
    static readonly string[] All = ["text", "schematic", "plots", "knobs"];

    // NOTE: text | (schematic over plots, knobs).
    static DockModel Start() => new(new DockSplit(SplitDirection.Row,
    [
        new DockTabs(["text"]),
        new DockSplit(SplitDirection.Column, [new DockTabs(["schematic"]), new DockTabs(["plots", "knobs"])]),
    ]));

    static void AssertEachPanelOnce(DockModel model)
    {
        var panels = model.Panels.Concat(model.Closed).ToList();
        Assert.Equal(All.Order(), panels.Order());
    }

    [Fact]
    public void StartTree()
    {
        var model = Start();

        Assert.Equal("row(0.5 tabs(text*), 0.5 column(0.5 tabs(schematic*), 0.5 tabs(plots*, knobs)))", model.ToString());
        AssertEachPanelOnce(model);
    }

    [Fact]
    public void DockToRootEdge()
    {
        var model = Start();

        model.Dock("knobs", DockEdge.Left);

        Assert.Equal("row(0.33 tabs(knobs*), 0.33 tabs(text*), 0.33 column(0.5 tabs(schematic*), 0.5 tabs(plots*)))", model.ToString());
        AssertEachPanelOnce(model);
    }

    [Fact]
    public void DockToGroupEdge()
    {
        var model = Start();

        model.Dock("knobs", DockEdge.Bottom, "text");

        Assert.Equal("row(0.5 column(0.5 tabs(text*), 0.5 tabs(knobs*)), 0.5 column(0.5 tabs(schematic*), 0.5 tabs(plots*)))", model.ToString());
        AssertEachPanelOnce(model);
    }

    [Fact]
    public void DockLastPanelOfGroupRemovesGroup()
    {
        var model = Start();

        model.Dock("text", DockEdge.Top);

        // NOTE: The row has one child after text leaves, so the column becomes the root.
        Assert.Equal("column(0.33 tabs(text*), 0.33 tabs(schematic*), 0.33 tabs(plots*, knobs))", model.ToString());
        AssertEachPanelOnce(model);
    }

    [Fact]
    public void MoveIntoTabGroup()
    {
        var model = Start();

        model.MoveToTabs("text", "schematic");

        Assert.Equal("column(0.5 tabs(schematic, text*), 0.5 tabs(plots*, knobs))", model.ToString());
        AssertEachPanelOnce(model);
    }

    [Fact]
    public void MoveToNextGroupWraps()
    {
        var model = Start();

        model.MoveToNextGroup("knobs");

        Assert.Equal("row(0.5 tabs(text*, knobs), 0.5 column(0.5 tabs(schematic*), 0.5 tabs(plots*)))", model.ToString());
        AssertEachPanelOnce(model);
    }

    [Fact]
    public void ResizeSplit()
    {
        var model = Start();

        model.Resize([], 0, 0.1);
        model.Resize([1], 0, -0.2);

        Assert.Equal("row(0.6 tabs(text*), 0.4 column(0.3 tabs(schematic*), 0.7 tabs(plots*, knobs)))", model.ToString());
        AssertEachPanelOnce(model);
    }

    [Fact]
    public void ResizeKeepsMinimumSize()
    {
        var model = Start();

        model.Resize([], 0, 5);

        Assert.Equal("row(0.95 tabs(text*), 0.05 column(0.5 tabs(schematic*), 0.5 tabs(plots*, knobs)))", model.ToString());
    }

    [Fact]
    public void ClosePanel()
    {
        var model = Start();

        model.Close("schematic");

        Assert.Equal("row(0.5 tabs(text*), 0.5 tabs(plots*, knobs)) closed(schematic)", model.ToString());
        AssertEachPanelOnce(model);
    }

    [Fact]
    public void OpenClosedPanel()
    {
        var model = Start();
        model.Close("schematic");

        model.Open("schematic");

        Assert.Equal("row(0.33 tabs(text*), 0.33 tabs(plots*, knobs), 0.33 tabs(schematic*))", model.ToString());
        AssertEachPanelOnce(model);
    }

    [Fact]
    public void SerializeThenParseGivesSameTree()
    {
        var model = Start();
        model.Dock("knobs", DockEdge.Bottom, "text");
        model.Resize([0], 0, 0.15);
        model.Close("plots");

        var json = model.Serialize();
        var parsed = DockModel.Parse(json, All);

        Assert.NotNull(parsed);
        Assert.Equal(model.ToString(), parsed.ToString());
        Assert.Equal(json, parsed.Serialize());
        AssertEachPanelOnce(parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{bad")]
    [InlineData("[]")]
    [InlineData("""{"version":0,"root":{"tabs":["text","schematic","plots","knobs"],"active":0},"closed":[]}""")]
    [InlineData("""{"version":1,"root":{"tabs":["text","schematic","plots"],"active":0},"closed":[]}""")]
    [InlineData("""{"version":1,"root":{"tabs":["text","schematic","plots","knobs","knobs"],"active":0},"closed":[]}""")]
    [InlineData("""{"version":1,"root":{"tabs":["text","schematic","plots","old"],"active":0},"closed":["knobs"]}""")]
    [InlineData("""{"version":1,"root":{"tabs":["text","schematic","plots","knobs"],"active":9},"closed":[]}""")]
    [InlineData("""{"version":1,"root":{"split":"row","children":[{"tabs":["text"],"active":0},{"tabs":["schematic","plots","knobs"],"active":0}],"sizes":[1]},"closed":[]}""")]
    [InlineData("""{"version":1,"root":{"split":"row","children":[{"tabs":["text"],"active":0},{"tabs":["schematic","plots","knobs"],"active":0}],"sizes":[1,-1]},"closed":[]}""")]
    [InlineData("""{"version":1,"root":{"split":"diagonal","children":[{"tabs":["text"],"active":0},{"tabs":["schematic","plots","knobs"],"active":0}],"sizes":[1,1]},"closed":[]}""")]
    public void BadOrOldJsonGivesNull(string? json)
    {
        Assert.Null(DockModel.Parse(json, All));
    }

    [Fact]
    public void GroupRectsCoverTheLayout()
    {
        var model = Start();

        var rects = model.GroupRects().ToDictionary(g => g.Group.ActivePanel, g => g.Rect);

        Assert.Equal(new DockRect(0, 0, 0.5, 1), rects["text"]);
        Assert.Equal(new DockRect(0.5, 0, 0.5, 0.5), rects["schematic"]);
        Assert.Equal(new DockRect(0.5, 0.5, 0.5, 0.5), rects["plots"]);
    }
}
