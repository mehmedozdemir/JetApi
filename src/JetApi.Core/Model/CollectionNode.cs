namespace JetApi.Core.Model;

/// <summary>A node in a collection tree: a folder or a request.</summary>
public abstract record CollectionNode
{
    /// <summary>Stable identifier among siblings; also the file or directory name. See <see cref="NodeKey"/>.</summary>
    public required string Key { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Optional Markdown documentation.</summary>
    public string? Description { get; init; }

    /// <summary>Postman scripts kept from an import; not run (ADR-0010).</summary>
    public ScriptSet Scripts { get; init; } = ScriptSet.None;
}

/// <summary>A folder that groups requests and other folders, and can override auth.</summary>
public sealed record Folder : CollectionNode
{
    /// <summary>Child folders and requests, in display order.</summary>
    public IReadOnlyList<CollectionNode> Children { get; init; } = [];

    /// <summary>Auth applied to children that inherit.</summary>
    public AuthConfig Auth { get; init; } = AuthConfig.Inherit;
}

/// <summary>Base type of all executable requests.</summary>
public abstract record RequestNode : CollectionNode
{
    /// <summary>Request authentication.</summary>
    public AuthConfig Auth { get; init; } = AuthConfig.Inherit;

    /// <summary>Assertions run on every response (the <c>tests</c> section of the file).</summary>
    public IReadOnlyList<ResponseCheck> Checks { get; init; } = [];

    /// <summary>Values taken from every response into variables (the <c>extract</c> section of the file).</summary>
    public IReadOnlyList<Extraction> Extractions { get; init; } = [];
}
