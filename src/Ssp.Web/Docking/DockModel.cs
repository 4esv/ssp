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

/// <summary>A panel in its own window above the tree. The rectangle is in fractions of the whole layout.</summary>
public sealed class DockFloat(string panel, DockRect rect)
{
    public string Panel { get; } = panel;

    public DockRect Rect { get; set; } = rect;
}

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

    /// <summary>The smallest width and height of a floating window, as a fraction of the layout.</summary>
    public const double MinFloat = 0.1;

    const double FloatWidth = 0.3;
    const double FloatHeight = 0.4;

    public DockModel(DockNode? root, IEnumerable<string>? closed = null, IEnumerable<DockFloat>? floating = null)
    {
        Root = root is null ? null : Normalize(root);
        Closed = closed?.ToList() ?? [];
        Floating = floating?.ToList() ?? [];
    }

    /// <summary>The floating windows. The last one is on top.</summary>
    public List<DockFloat> Floating { get; }

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

    /// <summary>
    /// Takes the panel out of its group and puts it in a floating window. The window starts at the corner of the layout
    /// where it covers least of the group of <paramref name="avoid"/>, or at <paramref name="at"/> when given.
    /// </summary>
    public void Float(string panel, string? avoid = null, DockRect? at = null)
    {
        if (Floating.Any(f => f.Panel == panel))
        {
            return;
        }

        var rect = at ?? Start(avoid);
        Detach(panel);
        Floating.Add(new DockFloat(panel, Fit(rect)));
    }

    /// <summary>Moves the floating window of the panel to the left and top fractions. It stays inside the layout.</summary>
    public void MoveFloating(string panel, double left, double top)
    {
        var floating = FloatOf(panel);
        floating.Rect = Fit(floating.Rect with { Left = left, Top = top });
    }

    /// <summary>Gives the floating window of the panel a size. It stays inside the layout.</summary>
    public void ResizeFloating(string panel, double width, double height)
    {
        var floating = FloatOf(panel);
        var rect = floating.Rect;
        floating.Rect = Fit(rect with { Width = width, Height = height });
    }

    /// <summary>Puts the floating window of the panel on top of the others.</summary>
    public void Raise(string panel)
    {
        var floating = FloatOf(panel);
        if (Floating[^1] != floating)
        {
            Floating.Remove(floating);
            Floating.Add(floating);
        }
    }

    /// <summary>Takes the panel out of the tree or out of its window. <see cref="Open"/> puts it back.</summary>
    public void Close(string panel)
    {
        if (GroupOf(panel) is null && !Floating.Any(f => f.Panel == panel))
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

            writer.WriteStartArray("floating");
            foreach (var floating in Floating)
            {
                writer.WriteStartObject();
                writer.WriteString("panel", floating.Panel);
                writer.WriteNumber("left", floating.Rect.Left);
                writer.WriteNumber("top", floating.Rect.Top);
                writer.WriteNumber("width", floating.Rect.Width);
                writer.WriteNumber("height", floating.Rect.Height);
                writer.WriteEndObject();
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
            var floating = top.TryGetProperty("floating", out var windows)
                ? windows.EnumerateArray().Select(ReadFloat).ToList()
                : [];
            var model = new DockModel(root.ValueKind == JsonValueKind.Null ? null : Read(root), closed, floating);
            var all = model.Panels.Concat(model.Closed).Concat(model.Floating.Select(f => f.Panel)).ToList();
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

        if (Floating.Count > 0)
        {
            text.Append(" floating(").AppendJoin(", ", Floating.Select(f => f.Panel)).Append(')');
        }

        return text.ToString();
    }

    DockFloat FloatOf(string panel) =>
        Floating.FirstOrDefault(f => f.Panel == panel) ?? throw new ArgumentException($"The panel '{panel}' is not floating.", nameof(panel));

    /// <summary>Keeps the rectangle inside the layout, and at least <see cref="MinFloat"/> wide and high.</summary>
    static DockRect Fit(DockRect rect)
    {
        var width = Math.Clamp(double.IsFinite(rect.Width) ? rect.Width : FloatWidth, MinFloat, 1);
        var height = Math.Clamp(double.IsFinite(rect.Height) ? rect.Height : FloatHeight, MinFloat, 1);
        var left = Math.Clamp(double.IsFinite(rect.Left) ? rect.Left : 0, 0, 1 - width);
        var top = Math.Clamp(double.IsFinite(rect.Top) ? rect.Top : 0, 0, 1 - height);
        return new DockRect(left, top, width, height);
    }

    /// <summary>
    /// Picks where a new window covers none of the group of <paramref name="avoid"/>: beside it or above or below it, with
    /// the largest room. When the group fills the layout, the corner where it covers least.
    /// </summary>
    DockRect Start(string? avoid)
    {
        var target = avoid is null ? null : GroupOf(avoid);
        if (target is null)
        {
            return new DockRect(1 - FloatWidth, 0.1, FloatWidth, FloatHeight);
        }

        var area = GroupRects().First(g => g.Group == target).Rect;
        var right = area.Left + area.Width;
        var bottom = area.Top + area.Height;
        var strips = new[]
        {
            new DockRect(right, 0.1, Math.Min(FloatWidth, 1 - right), FloatHeight),
            new DockRect(Math.Max(0, area.Left - FloatWidth), 0.1, Math.Min(FloatWidth, area.Left), FloatHeight),
            new DockRect(area.Left, bottom, FloatWidth, Math.Min(FloatHeight, 1 - bottom)),
            new DockRect(area.Left, Math.Max(0, area.Top - FloatHeight), FloatWidth, Math.Min(FloatHeight, area.Top)),
        };
        var free = strips.Where(r => r.Width >= MinFloat && r.Height >= MinFloat).Select(Fit)
            .Where(r => Overlap(r, area) < 1e-9).ToList();
        if (free.Count > 0)
        {
            return free.MaxBy(r => r.Width * r.Height);
        }

        var corners = new[]
        {
            new DockRect(1 - FloatWidth, 0.1, FloatWidth, FloatHeight),
            new DockRect(1 - FloatWidth, 1 - FloatHeight - 0.1, FloatWidth, FloatHeight),
            new DockRect(0, 0.1, FloatWidth, FloatHeight),
            new DockRect(0, 1 - FloatHeight - 0.1, FloatWidth, FloatHeight),
        };
        return corners.MinBy(c => Overlap(c, area));
    }

    static double Overlap(DockRect a, DockRect b)
    {
        var width = Math.Min(a.Left + a.Width, b.Left + b.Width) - Math.Max(a.Left, b.Left);
        var height = Math.Min(a.Top + a.Height, b.Top + b.Height) - Math.Max(a.Top, b.Top);
        return width > 0 && height > 0 ? width * height : 0;
    }

    void Detach(string panel)
    {
        Closed.Remove(panel);
        Floating.RemoveAll(f => f.Panel == panel);
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

    static DockFloat ReadFloat(JsonElement element)
    {
        var panel = ReadString(element.GetProperty("panel"));
        var rect = new DockRect(
            element.GetProperty("left").GetDouble(), element.GetProperty("top").GetDouble(),
            element.GetProperty("width").GetDouble(), element.GetProperty("height").GetDouble());
        if (!double.IsFinite(rect.Left) || !double.IsFinite(rect.Top) || !double.IsFinite(rect.Width) || !double.IsFinite(rect.Height)
            || rect.Width <= 0 || rect.Height <= 0)
        {
            throw new FormatException("A floating window has a bad rectangle.");
        }

        return new DockFloat(panel, Fit(rect));
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
