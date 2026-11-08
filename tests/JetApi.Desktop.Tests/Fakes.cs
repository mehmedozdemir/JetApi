using JetApi.Core.Variables;
using JetApi.Desktop.Services;
using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Tests;

internal sealed class FakeDialogs : IDialogService
{
    public Queue<string?> Prompts { get; } = new();

    public bool ConfirmResult { get; set; } = true;

    public UndefinedVariablesChoice UndefinedChoice { get; set; } = UndefinedVariablesChoice.SendAnyway;

    public List<string> Errors { get; } = [];

    public List<IReadOnlyList<VariableIssue>> UndefinedPrompts { get; } = [];

    public string? Clipboard { get; private set; }

    public string? FolderToPick { get; set; }

    public Task<string?> PickFolderAsync(string title) => Task.FromResult(FolderToPick);

    public Task<string?> PickFileAsync(string title) => Task.FromResult<string?>(null);

    public Task<string?> PickSaveFileAsync(string title, string suggestedName) => Task.FromResult<string?>(null);

    public Task<string?> PromptTextAsync(string title, string label, string initialValue) =>
        Task.FromResult(Prompts.Count > 0 ? Prompts.Dequeue() : initialValue);

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool isDestructive) => Task.FromResult(ConfirmResult);

    public Task<UndefinedVariablesChoice> ConfirmUndefinedVariablesAsync(IReadOnlyList<VariableIssue> issues, string? environmentName)
    {
        UndefinedPrompts.Add(issues);
        return Task.FromResult(UndefinedChoice);
    }

    public Task ShowErrorAsync(string message)
    {
        Errors.Add(message);
        return Task.CompletedTask;
    }

    public Task CopyToClipboardAsync(string text)
    {
        Clipboard = text;
        return Task.CompletedTask;
    }
}

internal sealed class FakeWorkspaceHost : IWorkspaceHost
{
    public AppSettings Settings { get; set; } = new();

    public int CloseCount { get; private set; }

    public void CloseWorkspace() => CloseCount++;

    public void ToggleTheme()
    {
    }

    public void ToggleLanguage()
    {
    }

    public void Post(Action action) => action();
}

internal sealed class TempWorkspace : IDisposable
{
    private TempWorkspace(string root, WorkspaceSession session)
    {
        Root = root;
        Session = session;
    }

    public string Root { get; }

    public WorkspaceSession Session { get; }

    public static async Task<TempWorkspace> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "jetapi-desktop-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var session = await WorkspaceSession.CreateAsync(root, "Test Workspace");
        return new TempWorkspace(root, session);
    }

    public WorkspaceViewModel CreateViewModel(FakeDialogs dialogs, RequestRunner? runner = null, FakeWorkspaceHost? host = null) =>
        new(Session, runner ?? new RequestRunner(new ProtoSchemaCache()), dialogs, host ?? new FakeWorkspaceHost(), watchFiles: false);

    public string Read(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }
}
