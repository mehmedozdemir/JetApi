using JetApi.Core.Model;

namespace JetApi.Desktop.Services;

/// <summary>What adding picked proto files changed.</summary>
/// <param name="Settings">The new proto settings.</param>
/// <param name="AddedFiles">Proto file entries that were added (already listed files are skipped).</param>
/// <param name="OutsideWorkspace">At least one file lies outside the workspace, so its path is absolute and will not work on other machines.</param>
public sealed record ProtoPathsResult(ProtoSettings Settings, IReadOnlyList<string> AddedFiles, bool OutsideWorkspace);

/// <summary>Turns proto files picked on disk into import paths and file entries of a collection.</summary>
public static class ProtoPaths
{
    /// <summary>
    /// Adds <paramref name="files"/> (full paths) to <paramref name="settings"/>. A file under an existing import path is
    /// listed relative to it; otherwise its folder becomes a new import path (relative to the workspace when inside it).
    /// When no import path was set (the workspace root is used), the root is kept as <c>.</c> before a new one is added.
    /// </summary>
    public static ProtoPathsResult AddFiles(ProtoSettings settings, string workspaceRoot, IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        ArgumentNullException.ThrowIfNull(files);

        var root = Path.GetFullPath(workspaceRoot);
        var importPaths = settings.ImportPaths.ToList();
        var protoFiles = settings.Files.ToList();
        var added = new List<string>();
        var outside = false;

        foreach (var file in files.Select(Path.GetFullPath))
        {
            var directories = importPaths.Count == 0 ? ["."] : importPaths;
            var containing = directories
                .Select(entry => (Entry: entry, Full: Path.GetFullPath(entry, root)))
                .Where(d => IsUnder(file, d.Full))
                .OrderByDescending(d => d.Full.Length)
                .Select(d => d.Full)
                .FirstOrDefault();

            if (containing is null)
            {
                var folder = Path.GetDirectoryName(file)!;
                if (importPaths.Count == 0)
                {
                    importPaths.Add(".");
                }

                var entry = IsUnder(folder, root) ? Normalize(Path.GetRelativePath(root, folder)) : Normalize(folder);
                outside |= !IsUnder(folder, root);
                importPaths.Add(entry);
                containing = folder;
            }

            var relative = Normalize(Path.GetRelativePath(containing, file));
            if (!protoFiles.Contains(relative, StringComparer.Ordinal))
            {
                protoFiles.Add(relative);
                added.Add(relative);
            }
        }

        return new ProtoPathsResult(new ProtoSettings(importPaths, protoFiles), added, outside);
    }

    /// <summary>The import path entry for a folder picked on disk: relative to the workspace when inside it.</summary>
    public static string ImportPathFor(string workspaceRoot, string folder)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var full = Path.GetFullPath(folder);
        return IsUnder(full, root) ? Normalize(Path.GetRelativePath(root, full)) : Normalize(full);
    }

    private static bool IsUnder(string path, string directory)
    {
        var relative = Path.GetRelativePath(directory, path);
        return relative == "." || (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative));
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
