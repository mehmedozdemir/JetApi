namespace JetApi.Core.Model;

/// <summary>
/// The root of everything a team shares: collections, environments and global variables.
/// A workspace maps to one directory, which is usually a Git repository.
/// </summary>
public sealed record Workspace
{
    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Optional Markdown documentation.</summary>
    public string? Description { get; init; }

    /// <summary>Collections in display order.</summary>
    public IReadOnlyList<ApiCollection> Collections { get; init; } = [];

    /// <summary>Environments in display order.</summary>
    public IReadOnlyList<ApiEnvironment> Environments { get; init; } = [];

    /// <summary>Workspace-wide variables; lowest precedence.</summary>
    public IReadOnlyList<Variable> GlobalVariables { get; init; } = [];
}
