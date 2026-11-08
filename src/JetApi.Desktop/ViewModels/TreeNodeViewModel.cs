using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using JetApi.Core.Model;

namespace JetApi.Desktop.ViewModels;

/// <summary>Kinds of nodes in the collection tree.</summary>
public enum TreeNodeKind
{
    /// <summary>A collection.</summary>
    Collection,

    /// <summary>A folder.</summary>
    Folder,

    /// <summary>An HTTP request.</summary>
    Http,

    /// <summary>A gRPC request.</summary>
    Grpc,
}

/// <summary>A node of the sidebar tree.</summary>
public sealed partial class TreeNodeViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>Creates a node.</summary>
    public TreeNodeViewModel(TreeNodeKind kind, string collectionKey, IReadOnlyList<string> path, string name, string badge = "")
    {
        Kind = kind;
        CollectionKey = collectionKey;
        Path = path;
        Name = name;
        Badge = badge;
    }

    /// <summary>Node kind.</summary>
    public TreeNodeKind Kind { get; }

    /// <summary>Collection key.</summary>
    public string CollectionKey { get; }

    /// <summary>Path from the collection root; empty for the collection itself.</summary>
    public IReadOnlyList<string> Path { get; }

    /// <summary>Display name.</summary>
    public string Name { get; }

    /// <summary>Method or "gRPC" for requests.</summary>
    public string Badge { get; }

    /// <summary>Child nodes.</summary>
    public ObservableCollection<TreeNodeViewModel> Children { get; } = [];

    /// <summary>Collections and folders contain other nodes.</summary>
    public bool IsContainer => Kind is TreeNodeKind.Collection or TreeNodeKind.Folder;

    /// <summary>Requests can be opened.</summary>
    public bool IsRequest => !IsContainer;

    /// <summary>Is a collection.</summary>
    public bool IsCollection => Kind == TreeNodeKind.Collection;

    /// <summary>Is a folder.</summary>
    public bool IsFolder => Kind == TreeNodeKind.Folder;

    /// <summary>Identity used to keep expansion state across rebuilds.</summary>
    public string Identity => $"{CollectionKey}/{string.Join('/', Path)}";

    /// <summary>Builds the tree of a collection.</summary>
    public static TreeNodeViewModel FromCollection(ApiCollection collection, ISet<string> expanded)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(expanded);

        var root = new TreeNodeViewModel(TreeNodeKind.Collection, collection.Key, [], collection.Name);
        root.IsExpanded = expanded.Contains(root.Identity);
        AddChildren(root, collection.Key, collection.Children, [], expanded);
        return root;
    }

    /// <summary>Applies a case-insensitive filter; containers stay visible when a descendant matches.</summary>
    public bool ApplyFilter(string filter)
    {
        var selfMatches = filter.Length == 0 || Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase) || Badge.Contains(filter, StringComparison.OrdinalIgnoreCase);
        var childMatches = false;
        foreach (var child in Children)
        {
            childMatches |= child.ApplyFilter(selfMatches && IsContainer && filter.Length > 0 ? string.Empty : filter);
        }

        IsVisible = selfMatches || childMatches;
        if (filter.Length > 0 && childMatches)
        {
            IsExpanded = true;
        }

        return IsVisible;
    }

    private static void AddChildren(TreeNodeViewModel parent, string collectionKey, IReadOnlyList<CollectionNode> children, IReadOnlyList<string> prefix, ISet<string> expanded)
    {
        foreach (var child in children)
        {
            IReadOnlyList<string> path = [.. prefix, child.Key];
            TreeNodeViewModel node = child switch
            {
                Folder => new TreeNodeViewModel(TreeNodeKind.Folder, collectionKey, path, child.Name),
                GrpcRequestDefinition => new TreeNodeViewModel(TreeNodeKind.Grpc, collectionKey, path, child.Name, "gRPC"),
                HttpRequestDefinition http => new TreeNodeViewModel(TreeNodeKind.Http, collectionKey, path, child.Name, http.Method.Length > 6 ? http.Method[..6] : http.Method),
                _ => new TreeNodeViewModel(TreeNodeKind.Http, collectionKey, path, child.Name),
            };
            node.IsExpanded = expanded.Contains(node.Identity);
            parent.Children.Add(node);
            if (child is Folder folder)
            {
                AddChildren(node, collectionKey, folder.Children, path, expanded);
            }
        }
    }
}
