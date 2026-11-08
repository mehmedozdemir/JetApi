namespace JetApi.Storage.Tests;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jetapi-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Write(string relativePath, string content)
    {
        var full = Combine(relativePath.Split('/'));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public string Read(string relativePath) => File.ReadAllText(Combine(relativePath.Split('/')));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup of a temp directory.
        }
    }
}
