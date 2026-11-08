using System.Diagnostics.CodeAnalysis;

namespace JetApi.Core.Model;

/// <summary>A collection of requests, typically one per API or service.</summary>
[SuppressMessage("Naming", "CA1711", Justification = "'Collection' is the domain term (a collection of API requests), not a .NET collection type.")]
public sealed record ApiCollection
{
    /// <summary>Stable key; also the collection's directory name.</summary>
    public required string Key { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Optional Markdown documentation.</summary>
    public string? Description { get; init; }

    /// <summary>Top-level folders and requests, in display order.</summary>
    public IReadOnlyList<CollectionNode> Children { get; init; } = [];

    /// <summary>Default auth for every request that inherits.</summary>
    public AuthConfig Auth { get; init; } = AuthConfig.None;

    /// <summary>Collection variables; lower precedence than environment variables.</summary>
    public IReadOnlyList<Variable> Variables { get; init; } = [];

    /// <summary>Proto schema used by gRPC requests in this collection.</summary>
    public ProtoSettings Proto { get; init; } = ProtoSettings.Empty;
}
