namespace JetApi.Desktop.Services;

/// <summary>Reads the current Git branch without running git.</summary>
public static class GitInfo
{
    /// <summary>The branch checked out in the repository containing <paramref name="directory"/>, or <c>null</c>.</summary>
    public static string? TryGetBranch(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            var gitPath = Path.Combine(current.FullName, ".git");
            var headPath = Directory.Exists(gitPath) ? Path.Combine(gitPath, "HEAD") : ResolveWorktreeHead(gitPath);
            if (headPath is null || !File.Exists(headPath))
            {
                continue;
            }

            try
            {
                var head = File.ReadAllText(headPath).Trim();
                const string prefix = "ref: refs/heads/";
                return head.StartsWith(prefix, StringComparison.Ordinal) ? head[prefix.Length..] : head[..Math.Min(7, head.Length)];
            }
            catch (IOException)
            {
                return null;
            }
        }

        return null;
    }

    // In worktrees and submodules ".git" is a file containing "gitdir: <path>".
    private static string? ResolveWorktreeHead(string gitFile)
    {
        if (!File.Exists(gitFile))
        {
            return null;
        }

        var content = File.ReadAllText(gitFile).Trim();
        return content.StartsWith("gitdir:", StringComparison.Ordinal)
            ? Path.Combine(Path.GetFullPath(content["gitdir:".Length..].Trim(), Path.GetDirectoryName(gitFile)!), "HEAD")
            : null;
    }
}
