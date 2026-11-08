namespace JetApi.Core.Model;

/// <summary>
/// Immutable edits of a collection tree. Nodes are addressed by their key path from the collection
/// root, e.g. <c>["admin", "create-user"]</c>; an empty path addresses the collection itself.
/// Every edit returns a new collection and leaves the original untouched.
/// </summary>
public static class CollectionTree
{
    /// <summary>Finds the node at <paramref name="path"/>, or <c>null</c>.</summary>
    public static CollectionNode? Find(ApiCollection collection, IReadOnlyList<string> path)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(path);

        IReadOnlyList<CollectionNode> children = collection.Children;
        CollectionNode? node = null;
        foreach (var key in path)
        {
            node = children.FirstOrDefault(child => child.Key == key);
            if (node is null)
            {
                return null;
            }

            children = node is Folder folder ? folder.Children : [];
        }

        return node;
    }

    /// <summary>The folders that contain the node at <paramref name="path"/>, closest first.</summary>
    public static IReadOnlyList<Folder> Ancestors(ApiCollection collection, IReadOnlyList<string> path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var ancestors = new List<Folder>();
        for (var length = path.Count - 1; length > 0; length--)
        {
            if (Find(collection, [.. path.Take(length)]) is Folder folder)
            {
                ancestors.Add(folder);
            }
        }

        return ancestors;
    }

    /// <summary>The children of the collection (empty path) or of the folder at <paramref name="parentPath"/>.</summary>
    /// <exception cref="ArgumentException">The path does not address a folder.</exception>
    public static IReadOnlyList<CollectionNode> ChildrenOf(ApiCollection collection, IReadOnlyList<string> parentPath)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(parentPath);

        if (parentPath.Count == 0)
        {
            return collection.Children;
        }

        return Find(collection, parentPath) is Folder folder
            ? folder.Children
            : throw new ArgumentException($"'{string.Join('/', parentPath)}' is not a folder.", nameof(parentPath));
    }

    /// <summary>Replaces the node at <paramref name="path"/> with <paramref name="replacement"/>, keeping its position.</summary>
    public static ApiCollection Replace(ApiCollection collection, IReadOnlyList<string> path, CollectionNode replacement)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(replacement);
        if (path.Count == 0)
        {
            throw new ArgumentException("The path must address a node, not the collection.", nameof(path));
        }

        return UpdateChildren(collection, [.. path.SkipLast(1)], children =>
        {
            var index = IndexOf(children, path[^1]);
            var siblings = children.ToList();
            siblings[index] = replacement;
            return siblings;
        });
    }

    /// <summary>Adds <paramref name="node"/> to the collection or folder at <paramref name="parentPath"/>.</summary>
    /// <param name="collection">The collection.</param>
    /// <param name="parentPath">Empty for the collection root.</param>
    /// <param name="node">The new node; its key must be unique among its new siblings.</param>
    /// <param name="index">Position, or <c>null</c> to append.</param>
    public static ApiCollection Add(ApiCollection collection, IReadOnlyList<string> parentPath, CollectionNode node, int? index = null)
    {
        ArgumentNullException.ThrowIfNull(node);

        return UpdateChildren(collection, parentPath, children =>
        {
            if (children.Any(child => child.Key.Equals(node.Key, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"The key '{node.Key}' is already used in this folder.", nameof(node));
            }

            var siblings = children.ToList();
            siblings.Insert(Math.Clamp(index ?? siblings.Count, 0, siblings.Count), node);
            return siblings;
        });
    }

    /// <summary>Removes the node at <paramref name="path"/>.</summary>
    public static ApiCollection Remove(ApiCollection collection, IReadOnlyList<string> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count == 0)
        {
            throw new ArgumentException("The path must address a node, not the collection.", nameof(path));
        }

        return UpdateChildren(collection, [.. path.SkipLast(1)], children =>
        {
            var index = IndexOf(children, path[^1]);
            var siblings = children.ToList();
            siblings.RemoveAt(index);
            return siblings;
        });
    }

    /// <summary>Moves the node at <paramref name="path"/> into <paramref name="newParentPath"/> at <paramref name="index"/>.</summary>
    public static ApiCollection Move(ApiCollection collection, IReadOnlyList<string> path, IReadOnlyList<string> newParentPath, int? index = null)
    {
        ArgumentNullException.ThrowIfNull(newParentPath);

        var node = Find(collection, path) ?? throw new ArgumentException($"'{string.Join('/', path)}' was not found.", nameof(path));
        if (newParentPath.Count >= path.Count && newParentPath.Take(path.Count).SequenceEqual(path))
        {
            throw new ArgumentException("A folder cannot be moved into itself.", nameof(newParentPath));
        }

        return Add(Remove(collection, path), newParentPath, node, index);
    }

    /// <summary>All requests in the collection with their paths, in display order.</summary>
    public static IEnumerable<(IReadOnlyList<string> Path, RequestNode Request)> EnumerateRequests(ApiCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        return Enumerate(collection.Children, []);
    }

    private static IEnumerable<(IReadOnlyList<string> Path, RequestNode Request)> Enumerate(IReadOnlyList<CollectionNode> children, IReadOnlyList<string> prefix)
    {
        foreach (var child in children)
        {
            IReadOnlyList<string> path = [.. prefix, child.Key];
            switch (child)
            {
                case RequestNode request:
                    yield return (path, request);
                    break;
                case Folder folder:
                    foreach (var nested in Enumerate(folder.Children, path))
                    {
                        yield return nested;
                    }

                    break;
            }
        }
    }

    private static ApiCollection UpdateChildren(
        ApiCollection collection,
        IReadOnlyList<string> parentPath,
        Func<IReadOnlyList<CollectionNode>, IReadOnlyList<CollectionNode>> update)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(parentPath);

        return collection with { Children = UpdateNested(collection.Children, parentPath, 0, update) };
    }

    private static IReadOnlyList<CollectionNode> UpdateNested(
        IReadOnlyList<CollectionNode> children,
        IReadOnlyList<string> parentPath,
        int depth,
        Func<IReadOnlyList<CollectionNode>, IReadOnlyList<CollectionNode>> update)
    {
        if (depth == parentPath.Count)
        {
            return update(children);
        }

        var index = IndexOf(children, parentPath[depth]);
        if (children[index] is not Folder folder)
        {
            throw new ArgumentException($"'{string.Join('/', parentPath.Take(depth + 1))}' is not a folder.", nameof(parentPath));
        }

        var siblings = children.ToList();
        siblings[index] = folder with { Children = UpdateNested(folder.Children, parentPath, depth + 1, update) };
        return siblings;
    }

    private static int IndexOf(IReadOnlyList<CollectionNode> children, string key)
    {
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i].Key == key)
            {
                return i;
            }
        }

        throw new ArgumentException($"No node with key '{key}'.", nameof(key));
    }
}
