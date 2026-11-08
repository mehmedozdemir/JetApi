using JetApi.Core.Model;

namespace JetApi.Import;

/// <summary>How much an import note matters.</summary>
public enum ImportIssueSeverity
{
    /// <summary>Converted, but differently (for example GraphQL sent as a JSON body).</summary>
    Info,

    /// <summary>Not carried over, or needs the user's attention (scripts, unsupported auth, missing files).</summary>
    Warning,
}

/// <summary>Something the import report tells the user.</summary>
/// <param name="Location">Where in the source: "Users / Create user", or empty for the whole file.</param>
/// <param name="Message">What happened, in the UI language.</param>
/// <param name="Severity">How much it matters.</param>
public sealed record ImportIssue(string Location, string Message, ImportIssueSeverity Severity = ImportIssueSeverity.Warning)
{
    /// <summary>Needs the user's attention.</summary>
    public bool IsWarning => Severity == ImportIssueSeverity.Warning;
}

/// <summary>What a file looks like.</summary>
public enum ImportKind
{
    /// <summary>Not something JetApi can import.</summary>
    Unknown,

    /// <summary>A Postman collection (v2.0 or v2.1).</summary>
    PostmanCollection,

    /// <summary>A Postman environment.</summary>
    PostmanEnvironment,

    /// <summary>An OpenAPI 3.x or Swagger 2.0 document (JSON or YAML).</summary>
    OpenApi,
}

/// <summary>
/// The outcome of an import: the converted value and the notes for the report, or an error that explains why
/// nothing could be imported. User-input problems never throw (see architecture.md).
/// </summary>
/// <typeparam name="T">The imported value.</typeparam>
public sealed record ImportResult<T>
    where T : class
{
    /// <summary>The converted value; <c>null</c> when <see cref="Error"/> is set.</summary>
    public T? Value { get; init; }

    /// <summary>Why nothing was imported.</summary>
    public string? Error { get; init; }

    /// <summary>Notes for the import report.</summary>
    public IReadOnlyList<ImportIssue> Issues { get; init; } = [];

    /// <summary>Something was imported.</summary>
    public bool Succeeded => Value is not null;
}

/// <summary>Creates import results.</summary>
public static class ImportResult
{
    /// <summary>A failed import with a message for the user.</summary>
    public static ImportResult<T> Failure<T>(string error)
        where T : class => new() { Error = error };
}

/// <summary>An imported Postman collection with counts for the report.</summary>
/// <param name="Collection">The collection, with keys that do not clash with the workspace.</param>
/// <param name="RequestCount">Requests imported.</param>
/// <param name="FolderCount">Folders imported.</param>
public sealed record CollectionImport(ApiCollection Collection, int RequestCount, int FolderCount);
