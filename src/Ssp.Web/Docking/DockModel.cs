namespace Ssp.Web.Docking;

public enum DockEdge { Left, Right, Top, Bottom }

public enum SplitDirection { Row, Column }

public abstract class DockNode
{
}

public sealed class DockTabs(IEnumerable<string> panels, int active = 0) : DockNode
{
    public List<string> Panels { get; } = panels.ToList();

    public int Active { get; set; } = active;

    public string ActivePanel => Panels[Active];
}

public sealed class DockSplit(SplitDirection direction, IEnumerable<DockNode> children, IEnumerable<double>? sizes = null) : DockNode
{
    public SplitDirection Direction { get; } = direction;

    public List<DockNode> Children { get; } = children.ToList();

    public List<double> Sizes { get; } = sizes?.ToList() ?? [];
}

public readonly record struct DockRect(double Left, double Top, double Width, double Height);

public sealed class DockModel(DockNode? root, IEnumerable<string>? closed = null)
{
    public DockNode? Root { get; private set; } = root;

    public List<string> Closed { get; } = closed?.ToList() ?? [];

    public IReadOnlyList<string> Panels => throw new NotImplementedException();

    public void Dock(string panel, DockEdge edge, string? target = null) => throw new NotImplementedException();

    public void MoveToTabs(string panel, string target) => throw new NotImplementedException();

    public void MoveToNextGroup(string panel) => throw new NotImplementedException();

    public void Resize(IReadOnlyList<int> path, int handle, double delta) => throw new NotImplementedException();

    public void Close(string panel) => throw new NotImplementedException();

    public void Open(string panel) => throw new NotImplementedException();

    public IEnumerable<(DockTabs Group, DockRect Rect)> GroupRects() => throw new NotImplementedException();

    public string Serialize() => throw new NotImplementedException();

    public static DockModel? Parse(string? json, IReadOnlyCollection<string> panels) => throw new NotImplementedException();
}
