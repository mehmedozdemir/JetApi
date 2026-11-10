using JetApi.Core.Model;

namespace JetApi.Desktop.ViewModels;

/// <summary>Where a dragged tree node lands relative to the row under the pointer.</summary>
public enum DropPosition
{
    /// <summary>Not a valid drop.</summary>
    None,

    /// <summary>Above the row, as its previous sibling.</summary>
    Before,

    /// <summary>Into the folder or collection, as its last child.</summary>
    Inside,

    /// <summary>Below the row, as its next sibling.</summary>
    After,
}

/// <summary>Where a moved node goes: a folder of a collection, before a sibling (or at the end when <c>null</c>).</summary>
/// <param name="CollectionKey">Target collection.</param>
/// <param name="ParentPath">Target folder; empty for the collection root.</param>
/// <param name="BeforeKey">The sibling it goes before; <c>null</c> for the end.</param>
public sealed record DropPlan(string CollectionKey, IReadOnlyList<string> ParentPath, string? BeforeKey);

/// <summary>
/// Drag and drop rules of the collection tree, kept free of the UI. Folders and requests move anywhere in any
/// collection (not into themselves); collections are reordered among collections.
/// </summary>
public static class TreeDrop
{
    /// <summary>
    /// The drop position for a pointer at <paramref name="fraction"/> (0 top, 1 bottom) of <paramref name="target"/>'s row.
    /// Folders take the middle half as "inside"; an expanded folder takes its lower part too, because the line under it
    /// sits above its first child.
    /// </summary>
    public static DropPosition PositionAt(TreeNodeViewModel source, TreeNodeViewModel target, double fraction)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        var position = (source.IsCollection, target.Kind) switch
        {
            (true, TreeNodeKind.Collection) => fraction < 0.5 ? DropPosition.Before : DropPosition.After,
            (true, _) => DropPosition.None,
            (false, TreeNodeKind.Collection) => DropPosition.Inside,
            (false, TreeNodeKind.Folder) when fraction < 0.25 => DropPosition.Before,
            (false, TreeNodeKind.Folder) when fraction > 0.75 && !(target.IsExpanded && target.Children.Count > 0) => DropPosition.After,
            (false, TreeNodeKind.Folder) => DropPosition.Inside,
            _ => fraction < 0.5 ? DropPosition.Before : DropPosition.After,
        };
        return CanDrop(source, target, position) ? position : DropPosition.None;
    }

    /// <summary>Whether <paramref name="source"/> may be dropped at <paramref name="position"/> of <paramref name="target"/>.</summary>
    public static bool CanDrop(TreeNodeViewModel source, TreeNodeViewModel target, DropPosition position)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        if (position == DropPosition.None || source.Identity == target.Identity)
        {
            return false;
        }

        if (source.IsCollection)
        {
            return target.IsCollection && position != DropPosition.Inside;
        }

        if (position == DropPosition.Inside ? !target.IsContainer : target.IsCollection)
        {
            return false;
        }

        // Not into itself or one of its own folders.
        return !(target.CollectionKey == source.CollectionKey && target.Path.Count > source.Path.Count && target.Path.Take(source.Path.Count).SequenceEqual(source.Path));
    }

    /// <summary>Where the node goes, or <c>null</c> when the drop is not allowed.</summary>
    public static DropPlan? Plan(Workspace workspace, TreeNodeViewModel source, TreeNodeViewModel target, DropPosition position)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (!CanDrop(source, target, position))
        {
            return null;
        }

        if (source.IsCollection)
        {
            var keys = workspace.Collections.Select(c => c.Key).Where(k => k != source.CollectionKey).ToList();
            return new DropPlan(target.CollectionKey, [], position == DropPosition.Before ? target.CollectionKey : NextAfter(keys, target.CollectionKey));
        }

        if (position == DropPosition.Inside)
        {
            return new DropPlan(target.CollectionKey, target.Path, null);
        }

        if (workspace.FindCollection(target.CollectionKey) is not { } collection)
        {
            return null;
        }

        var parent = target.Path.SkipLast(1).ToList();
        var siblings = CollectionTree.ChildrenOf(collection, parent).Select(c => c.Key)
            .Where(k => !(source.CollectionKey == target.CollectionKey && source.Path.SkipLast(1).SequenceEqual(parent) && k == source.Path[^1]))
            .ToList();
        return new DropPlan(target.CollectionKey, parent, position == DropPosition.Before ? target.Path[^1] : NextAfter(siblings, target.Path[^1]));
    }

    /// <summary>Moving one step up (<paramref name="down"/> false) or down among the siblings; <c>null</c> at the edge.</summary>
    public static DropPlan? Step(Workspace workspace, TreeNodeViewModel node, bool down)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(node);
        if (node.IsCollection)
        {
            var keys = workspace.Collections.Select(c => c.Key).ToList();
            var index = keys.IndexOf(node.CollectionKey);
            var neighbour = index + (down ? 1 : -1);
            if (index < 0 || neighbour < 0 || neighbour >= keys.Count)
            {
                return null;
            }

            keys.RemoveAt(index);
            return new DropPlan(node.CollectionKey, [], down ? NextAfter(keys, keys[neighbour - 1]) : keys[neighbour]);
        }

        if (workspace.FindCollection(node.CollectionKey) is not { } collection)
        {
            return null;
        }

        var parent = node.Path.SkipLast(1).ToList();
        var siblings = CollectionTree.ChildrenOf(collection, parent).Select(c => c.Key).ToList();
        var at = siblings.IndexOf(node.Path[^1]);
        var to = at + (down ? 1 : -1);
        if (at < 0 || to < 0 || to >= siblings.Count)
        {
            return null;
        }

        var neighbourKey = siblings[to];
        siblings.RemoveAt(at);
        return new DropPlan(node.CollectionKey, parent, down ? NextAfter(siblings, neighbourKey) : neighbourKey);
    }

    private static string? NextAfter(List<string> keys, string key)
    {
        var index = keys.IndexOf(key);
        return index >= 0 && index + 1 < keys.Count ? keys[index + 1] : null;
    }
}
