namespace JetApi.Storage;

/// <summary>A problem found while loading a workspace. The affected file is skipped; the rest still loads.</summary>
/// <param name="FilePath">Path relative to the workspace root, with forward slashes.</param>
/// <param name="Message">Human-readable description.</param>
public sealed record StorageDiagnostic(string FilePath, string Message);

/// <summary>Thrown when a model cannot be written, e.g. because a key is not a safe file name.</summary>
public sealed class WorkspaceStorageException : Exception
{
    /// <summary>Creates the exception.</summary>
    public WorkspaceStorageException()
    {
    }

    /// <summary>Creates the exception.</summary>
    public WorkspaceStorageException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public WorkspaceStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
