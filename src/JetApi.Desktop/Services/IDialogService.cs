using JetApi.Core.Variables;

namespace JetApi.Desktop.Services;

/// <summary>What the user chose when a request has undefined variables.</summary>
public enum UndefinedVariablesChoice
{
    /// <summary>Do not send.</summary>
    Cancel,

    /// <summary>Send with the placeholders as typed.</summary>
    SendAnyway,

    /// <summary>Open the environment editor instead.</summary>
    OpenEnvironments,
}

/// <summary>What the user chose for unsaved requests before leaving a workspace.</summary>
public enum UnsavedChangesChoice
{
    /// <summary>Stay in the workspace.</summary>
    Cancel,

    /// <summary>Save the requests, then leave.</summary>
    Save,

    /// <summary>Leave without saving.</summary>
    Discard,
}

/// <summary>Dialogs and OS integration, abstracted so view models can be tested without a UI.</summary>
public interface IDialogService
{
    /// <summary>Lets the user pick a folder; <c>null</c> when cancelled.</summary>
    Task<string?> PickFolderAsync(string title);

    /// <summary>Lets the user pick an existing file; <c>null</c> when cancelled.</summary>
    Task<string?> PickFileAsync(string title);

    /// <summary>Lets the user choose where to save a file; <c>null</c> when cancelled.</summary>
    Task<string?> PickSaveFileAsync(string title, string suggestedName);

    /// <summary>Lets the user choose one or more <c>.proto</c> files; empty when cancelled.</summary>
    Task<IReadOnlyList<string>> PickProtoFilesAsync(string title);

    /// <summary>Lets the user choose one or more JSON or YAML files; empty when cancelled.</summary>
    Task<IReadOnlyList<string>> PickJsonFilesAsync(string title);

    /// <summary>Shows what an import brought in and what it could not carry over.</summary>
    Task ShowImportReportAsync(ImportReport report);

    /// <summary>Opens <paramref name="uri"/> in the default browser.</summary>
    Task OpenBrowserAsync(Uri uri);

    /// <summary>Shows the settings window.</summary>
    Task ShowSettingsAsync(ViewModels.SettingsViewModel settings);

    /// <summary>Asks for a single line of text; <c>null</c> when cancelled.</summary>
    Task<string?> PromptTextAsync(string title, string label, string initialValue);

    /// <summary>Asks for confirmation.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText, bool isDestructive);

    /// <summary>Warns about undefined variables before sending.</summary>
    Task<UndefinedVariablesChoice> ConfirmUndefinedVariablesAsync(IReadOnlyList<VariableIssue> issues, string? environmentName);

    /// <summary>Asks what to do with unsaved requests (by name) before leaving the workspace.</summary>
    Task<UnsavedChangesChoice> ConfirmUnsavedChangesAsync(IReadOnlyList<string> items);

    /// <summary>Shows an error message.</summary>
    Task ShowErrorAsync(string message);

    /// <summary>Text on the clipboard, or <c>null</c>.</summary>
    Task<string?> GetClipboardTextAsync();

    /// <summary>Shows a request as code.</summary>
    Task ShowCodeSnippetAsync(ViewModels.CodeSnippetViewModel snippet);

    /// <summary>Copies text to the clipboard.</summary>
    Task CopyToClipboardAsync(string text);

    /// <summary>Shows the release notes.</summary>
    Task ShowReleaseNotesAsync(ViewModels.ReleaseNotesViewModel notes);

    /// <summary>Shows what a newer release brings and offers to install it.</summary>
    Task ShowUpdateAsync(ViewModels.UpdateDialogViewModel update);
}
