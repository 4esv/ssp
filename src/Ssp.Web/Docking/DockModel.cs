using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Ssp.Web.Docking;

/// <summary>An edge of the layout or of a tab group.</summary>
public enum DockEdge { Left, Right, Top, Bottom }

/// <summary>A row puts its children side by side. A column puts them one over the other.</summary>
public enum SplitDirection { Row, Column }

/// <summary>A node of the dock tree: a <see cref="DockSplit"/> or a <see cref="DockTabs"/>.</summary>
public abstract class DockNode
{
}

/// <summary>A group of panels that show one at a time, with a tab for each panel.</summary>
public sealed class DockTabs(IEnumerable<string> panels, int active = 0) : DockNode
{
    public List<string> Panels { get; } = panels.ToList();

    /// <summary>The index of the panel that shows.</summary>
    public int Active { get; set; } = active;

    public string ActivePanel => Panels[Active];
}

/// <summary>A row or a column of nodes. Each size is a fraction of the split, and the sizes add up to 1.</summary>
public sealed class DockSplit : DockNode
{
    /// <param name="sizes">The sizes of the children. Without sizes, the children get equal sizes.</param>
    public DockSplit(SplitDirection direction, IEnumerable<DockNode> children, IEnumerable<double>? sizes = null)
    {
        Direction = direction;
        Children = children.ToList();
        Sizes = sizes?.ToList() ?? Enumerable.Repeat(1.0 / Children.Count, Children.Count).ToList();
    }

    public SplitDirection Direction { get; }

    public List<DockNode> Children { get; }

    public List<double> Sizes { get; }
}

/// <summary>A rectangle in fractions of the whole layout.</summary>
public readonly record struct DockRect(double Left, double Top, double Width, double Height);

/// <summary>The border between child <see cref="Index"/> and the next child of the split at <see cref="Path"/>.</summary>
/// <param name="Split">The rectangle of the split.</param>
/// <param name="Position">The place of the border, as a fraction of the whole layout.</param>
public sealed record DockHandle(IReadOnlyList<int> Path, int Index, SplitDirection Direction, DockRect Split, double Position);

/// <summary>
/// A tree of splits and tab groups, and the panels that are closed. It has no UI code.
/// </summary>
/// <remarks>
/// NOTE: Each operation keeps the tree normal: no empty tab group, no split with one child, and no split in a split
/// of the same direction.
/// </remarks>
public sealed class DockModel
{
    /// <summary>The version of the JSON form. <see cref="Parse"/> gives null for each other version.</summary>
    public const int Version = 1;

    /// <summary>The smallest size that a resize leaves for a child of a split.</summary>
    public const double MinSize = 0.05;

    public DockModel(DockNode? root, IEnumerable<string>? closed = null)
    {
        Root = root is null ? null : Normalize(root);
        Closed = closed?.ToList() ?? [];
    }

    public DockNode? Root { get; private set; }

    public List<string> Closed { get; }

    /// <summary>The panels in the tree, in tree order.</summary>
    public IReadOnlyList<string> Panels => Groups.SelectMany(g => g.Panels).ToList();

    /// <summary>The tab groups, in tree order.</summary>
    public IReadOnlyList<DockTabs> Groups
    {
        get
        {
            var groups = new List<DockTabs>();
            Collect(Root, groups);
            return groups;
        }
    }

    public DockTabs? GroupOf(string panel) => Groups.FirstOrDefault(g => g.Panels.Contains(panel));

    public void Activate(string panel)
    {
        var group = GroupOf(panel) ?? throw new ArgumentException($"The panel '{panel}' is not in the tree.", nameof(panel));
        group.Active = group.Panels.IndexOf(panel);
    }

    /// <summary>Docks the panel at an edge of the layout, or at an edge of the group of <paramref name="target"/>.</summary>
    public void Dock(string panel, DockEdge edge, string? target = null)
    {
        var direction = edge is DockEdge.Left or DockEdge.Right ? SplitDirection.Row : SplitDirection.Column;
        var before = edge is DockEdge.Left or DockEdge.Top;
        var group = GroupOf(panel);
        DockTabs? targetGroup = null;
        if (target is not null)
        {
            targetGroup = GroupOf(target) ?? throw new ArgumentException($"The panel '{target}' is not in the tree.", nameof(target));
            if (targetGroup == group && group.Panels.Count == 1)
            {
                return;
            }
        }
        else if (group is not null && Root == group)
        {
            return;
        }

        Detach(panel);
        var fresh = new DockTabs([panel]);
        if (Root is null)
        {
            Root = fresh;
            return;
        }

        var anchor = targetGroup ?? Root;
        var parent = ParentOf(anchor);
        if (parent is not null && parent.Direction == direction)
        {
            // NOTE: The new group takes half of the anchor's share.
            var index = parent.Children.IndexOf(anchor);
            var half = parent.Sizes[index] / 2;
            parent.Sizes[index] = half;
            var at = before ? index : index + 1;
            parent.Children.Insert(at, fresh);
            parent.Sizes.Insert(at, half);
        }
        else if (anchor == Root && Root is DockSplit split && split.Direction == direction)
        {
            // NOTE: At the edge of the layout, the new group takes an equal share.
            var share = 1.0 / (split.Children.Count + 1);
            for (var i = 0; i < split.Sizes.Count; i++)
            {
                split.Sizes[i] *= 1 - share;
            }

            split.Children.Insert(before ? 0 : split.Children.Count, fresh);
            split.Sizes.Insert(before ? 0 : split.Sizes.Count, share);
        }
        else
        {
            Replace(anchor, new DockSplit(direction, before ? [fresh, anchor] : [anchor, fresh], [0.5, 0.5]));
        }
    }

    /// <summary>Moves the panel into the tab group of <paramref name="target"/>, and shows it.</summary>
    public void MoveToTabs(string panel, string target)
    {
        var targetGroup = GroupOf(target) ?? throw new ArgumentException($"The panel '{target}' is not in the tree.", nameof(target));
        if (targetGroup.Panels.Contains(panel))
        {
            return;
        }

        Detach(panel);
        targetGroup.Panels.Add(panel);
        targetGroup.Active = targetGroup.Panels.Count - 1;
    }

    /// <summary>Moves the panel into the next tab group in tree order. After the last group, it goes to the first.</summary>
    public void MoveToNextGroup(string panel)
    {
        var groups = Groups;
        var index = groups.ToList().FindIndex(g => g.Panels.Contains(panel));
        if (index < 0 || groups.Count < 2)
        {
            return;
        }

        MoveToTabs(panel, groups[(index + 1) % groups.Count].Panels[0]);
    }

    /// <summary>
    /// Moves the border after child <paramref name="handle"/> of the split at <paramref name="path"/> by
    /// <paramref name="delta"/>, a fraction of the split. Each of the two children keeps at least <see cref="MinSize"/>.
    /// </summary>
    public void Resize(IReadOnlyList<int> path, int handle, double delta)
    {
        if (NodeAt(path) is not DockSplit split || handle < 0 || handle + 1 >= split.Children.Count)
        {
            throw new ArgumentException("The path does not give a split with that border.", nameof(path));
        }

        var total = split.Sizes[handle] + split.Sizes[handle + 1];
        var size = Math.Clamp(split.Sizes[handle] + delta, MinSize, total - MinSize);
        split.Sizes[handle] = size;
        split.Sizes[handle + 1] = total - size;
    }

    /// <summary>Takes the panel out of the tree. <see cref="Open"/> puts it back.</summary>
    public void Close(string panel)
    {
        if (GroupOf(panel) is null)
        {
            return;
        }

        Detach(panel);
        Closed.Add(panel);
    }

    /// <summary>Puts a closed panel back at the right edge of the layout.</summary>
    public void Open(string panel) => Dock(panel, DockEdge.Right);

    /// <summary>Gives each tab group and its rectangle, in tree order.</summary>
    public IEnumerable<(DockTabs Group, DockRect Rect)> GroupRects()
    {
        var groups = new List<(DockTabs, DockRect)>();
        Walk(Root, new DockRect(0, 0, 1, 1), [], groups, []);
        return groups;
    }

    /// <summary>Gives each border between two children of a split.</summary>
    public IEnumerable<DockHandle> Handles()
    {
        var handles = new List<DockHandle>();
        Walk(Root, new DockRect(0, 0, 1, 1), [], [], handles);
        return handles;
    }

    public string Serialize()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WritePropertyName("root");
            if (Root is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                Write(writer, Root);
            }

            writer.WriteStartArray("closed");
            foreach (var panel in Closed)
            {
                writer.WriteStringValue(panel);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Reads a model from <see cref="Serialize"/>. Gives null for bad JSON, for another version, and when the panels
    /// in the tree and the closed panels are not each of <paramref name="panels"/> once.
    /// </summary>
    public static DockModel? Parse(string? json, IReadOnlyCollection<string> panels)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var top = document.RootElement;
            if (top.ValueKind != JsonValueKind.Object || top.GetProperty("version").GetInt32() != Version)
            {
                return null;
            }

            var root = top.GetProperty("root");
            var closed = top.GetProperty("closed").EnumerateArray().Select(ReadString).ToList();
            var model = new DockModel(root.ValueKind == JsonValueKind.Null ? null : Read(root), closed);
            var all = model.Panels.Concat(model.Closed).ToList();
            var known = panels.ToHashSet(StringComparer.Ordinal);
            if (all.Count != known.Count || all.Distinct(StringComparer.Ordinal).Count() != all.Count || !all.All(known.Contains))
            {
                return null;
            }

            return model;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Gives the tree in a short form, for example <c>row(0.5 tabs(text*), 0.5 tabs(plots*, knobs))</c>.</summary>
    /// <remarks>NOTE: A star marks the panel that shows in each group.</remarks>
    public override string ToString()
    {
        var text = new StringBuilder();
        if (Root is null)
        {
            text.Append("empty");
        }
        else
        {
            Describe(Root, text);
        }

        if (Closed.Count > 0)
        {
            text.Append(" closed(").AppendJoin(", ", Closed).Append(')');
        }

        return text.ToString();
    }

    void Detach(string panel)
    {
        Closed.Remove(panel);
        var group = GroupOf(panel);
        if (group is null)
        {
            return;
        }

        var index = group.Panels.IndexOf(panel);
        group.Panels.RemoveAt(index);
        if (group.Active > index || group.Active >= group.Panels.Count)
        {
            group.Active = Math.Max(0, group.Active - 1);
        }

        Root = Root is null ? null : Normalize(Root);
    }

    /// <summary>Takes out empty groups, takes the single child out of a split, and merges splits of one direction.</summary>
    static DockNode? Normalize(DockNode node)
    {
        if (node is DockTabs tabs)
        {
            return tabs.Panels.Count == 0 ? null : tabs;
        }

        var split = (DockSplit)node;
        var children = new List<DockNode>();
        var sizes = new List<double>();
        for (var i = 0; i < split.Children.Count; i++)
        {
            switch (Normalize(split.Children[i]))
            {
                case null:
                    break;
                case DockSplit inner when inner.Direction == split.Direction:
                    children.AddRange(inner.Children);
                    sizes.AddRange(inner.Sizes.Select(s => s * split.Sizes[i]));
                    break;
                case var child:
                    children.Add(child);
                    sizes.Add(split.Sizes[i]);
                    break;
            }
        }

        if (children.Count <= 1)
        {
            return children.SingleOrDefault();
        }

        var total = sizes.Sum();
        if (Math.Abs(total - 1) > 1e-9)
        {
            sizes = sizes.Select(s => s / total).ToList();
        }

        split.Children.Clear();
        split.Children.AddRange(children);
        split.Sizes.Clear();
        split.Sizes.AddRange(sizes);
        return split;
    }

    DockSplit? ParentOf(DockNode node) => Splits(Root).FirstOrDefault(s => s.Children.Contains(node));

    static IEnumerable<DockSplit> Splits(DockNode? node) => node is DockSplit split
        ? split.Children.SelectMany(Splits).Prepend(split)
        : [];

    void Replace(DockNode old, DockNode replacement)
    {
        if (ParentOf(old) is { } parent)
        {
            parent.Children[parent.Children.IndexOf(old)] = replacement;
        }
        else
        {
            Root = replacement;
        }
    }

    DockNode? NodeAt(IReadOnlyList<int> path)
    {
        var node = Root;
        foreach (var index in path)
        {
            if (node is not DockSplit split || index < 0 || index >= split.Children.Count)
            {
                return null;
            }

            node = split.Children[index];
        }

        return node;
    }

    static void Collect(DockNode? node, List<DockTabs> groups)
    {
        switch (node)
        {
            case DockTabs tabs:
                groups.Add(tabs);
                break;
            case DockSplit split:
                foreach (var child in split.Children)
                {
                    Collect(child, groups);
                }

                break;
        }
    }

    static void Walk(DockNode? node, DockRect rect, List<int> path, List<(DockTabs, DockRect)> groups, List<DockHandle> handles)
    {
        switch (node)
        {
            case DockTabs tabs:
                groups.Add((tabs, rect));
                break;
            case DockSplit split:
                var row = split.Direction == SplitDirection.Row;
                var offset = 0.0;
                for (var i = 0; i < split.Children.Count; i++)
                {
                    if (i > 0)
                    {
                        var position = row ? rect.Left + (offset * rect.Width) : rect.Top + (offset * rect.Height);
                        handles.Add(new DockHandle(path.ToList(), i - 1, split.Direction, rect, position));
                    }

                    var size = split.Sizes[i];
                    var child = row
                        ? new DockRect(rect.Left + (offset * rect.Width), rect.Top, size * rect.Width, rect.Height)
                        : new DockRect(rect.Left, rect.Top + (offset * rect.Height), rect.Width, size * rect.Height);
                    path.Add(i);
                    Walk(split.Children[i], child, path, groups, handles);
                    path.RemoveAt(path.Count - 1);
                    offset += size;
                }

                break;
        }
    }

    static void Write(Utf8JsonWriter writer, DockNode node)
    {
        writer.WriteStartObject();
        if (node is DockTabs tabs)
        {
            writer.WriteStartArray("tabs");
            foreach (var panel in tabs.Panels)
            {
                writer.WriteStringValue(panel);
            }

            writer.WriteEndArray();
            writer.WriteNumber("active", tabs.Active);
        }
        else
        {
            var split = (DockSplit)node;
            writer.WriteString("split", split.Direction == SplitDirection.Row ? "row" : "column");
            writer.WriteStartArray("children");
            foreach (var child in split.Children)
            {
                Write(writer, child);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("sizes");
            foreach (var size in split.Sizes)
            {
                writer.WriteNumberValue(size);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    static DockNode Read(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("A dock node is not an object.");
        }

        if (element.TryGetProperty("tabs", out var tabs))
        {
            var panels = tabs.EnumerateArray().Select(ReadString).ToList();
            var active = element.GetProperty("active").GetInt32();
            if (panels.Count == 0 || active < 0 || active >= panels.Count)
            {
                throw new FormatException("A tab group is empty or shows no panel.");
            }

            return new DockTabs(panels, active);
        }

        var direction = ReadString(element.GetProperty("split")) switch
        {
            "row" => SplitDirection.Row,
            "column" => SplitDirection.Column,
            _ => throw new FormatException("A split is not a row or a column."),
        };
        var children = element.GetProperty("children").EnumerateArray().Select(Read).ToList();
        var sizes = element.GetProperty("sizes").EnumerateArray().Select(s => s.GetDouble()).ToList();
        if (children.Count < 2 || sizes.Count != children.Count || sizes.Any(s => !double.IsFinite(s) || s <= 0))
        {
            throw new FormatException("A split has fewer than two children, or a bad size.");
        }

        return new DockSplit(direction, children, sizes);
    }

    static string ReadString(JsonElement element) =>
        element.GetString() ?? throw new FormatException("A panel name is null.");

    static void Describe(DockNode node, StringBuilder text)
    {
        if (node is DockTabs tabs)
        {
            text.Append("tabs(").AppendJoin(", ", tabs.Panels.Select((p, i) => i == tabs.Active ? p + "*" : p)).Append(')');
            return;
        }

        var split = (DockSplit)node;
        text.Append(split.Direction == SplitDirection.Row ? "row(" : "column(");
        for (var i = 0; i < split.Children.Count; i++)
        {
            if (i > 0)
            {
                text.Append(", ");
            }

            text.Append(split.Sizes[i].ToString("0.##", CultureInfo.InvariantCulture)).Append(' ');
            Describe(split.Children[i], text);
        }

        text.Append(')');
    }
}
