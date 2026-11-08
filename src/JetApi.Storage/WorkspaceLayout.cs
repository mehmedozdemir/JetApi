namespace JetApi.Storage;

/// <summary>File and directory names of the on-disk workspace format. See docs/file-format.md.</summary>
public static class WorkspaceLayout
{
    /// <summary>The current schema version written to every file.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Workspace manifest at the root of the workspace directory.</summary>
    public const string WorkspaceFileName = "jetapi.yaml";

    /// <summary>Directory holding one sub-directory per collection.</summary>
    public const string CollectionsDirectory = "collections";

    /// <summary>Directory holding one file per environment.</summary>
    public const string EnvironmentsDirectory = "environments";

    /// <summary>Manifest inside each collection directory.</summary>
    public const string CollectionFileName = "collection.yaml";

    /// <summary>Manifest inside each folder directory.</summary>
    public const string FolderFileName = "folder.yaml";

    /// <summary>Suffix of HTTP request files.</summary>
    public const string HttpRequestSuffix = ".http.yaml";

    /// <summary>Suffix of gRPC request files.</summary>
    public const string GrpcRequestSuffix = ".grpc.yaml";

    /// <summary>Suffix of environment files.</summary>
    public const string EnvironmentSuffix = ".yaml";

    /// <summary>Per-machine state (history, UI layout). Must be git-ignored.</summary>
    public const string LocalStateDirectory = ".jetapi";
}
