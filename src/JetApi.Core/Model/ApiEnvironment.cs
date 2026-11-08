namespace JetApi.Core.Model;

/// <summary>A named set of variables, e.g. <c>dev</c>, <c>test</c>, <c>prod</c>.</summary>
public sealed record ApiEnvironment
{
    /// <summary>Stable key; also the file name.</summary>
    public required string Key { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Environment variables.</summary>
    public IReadOnlyList<Variable> Variables { get; init; } = [];
}
