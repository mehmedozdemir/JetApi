using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using JetApi.Core.Variables;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Views.Dialogs;

namespace JetApi.Desktop.Services;

/// <summary>Avalonia implementation of <see cref="IDialogService"/>, owned by the main window.</summary>
public sealed class DialogService : IDialogService
{
    private readonly Func<Window?> _owner;

    /// <summary>Creates the service; <paramref name="owner"/> returns the window dialogs belong to.</summary>
    public DialogService(Func<Window?> owner)
    {
        _owner = owner;
    }

    /// <inheritdoc />
    public async Task<string?> PickFolderAsync(string title)
    {
        if (_owner() is not { } owner)
        {
            return null;
        }

        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false }).ConfigureAwait(true);
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    /// <inheritdoc />
    public async Task<string?> PickFileAsync(string title)
    {
        if (_owner() is not { } owner)
        {
            return null;
        }

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false }).ConfigureAwait(true);
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> PickJsonFilesAsync(string title)
    {
        if (_owner() is not { } owner)
        {
            return [];
        }

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("JSON, YAML") { Patterns = ["*.json", "*.yaml", "*.yml"] }, FilePickerFileTypes.All],
        }).ConfigureAwait(true);
        return [.. files.Select(f => f.TryGetLocalPath()).OfType<string>()];
    }

    /// <inheritdoc />
    public Task ShowImportReportAsync(ImportReport report) => ShowAsync(new ImportReportDialog(report), false);

    /// <inheritdoc />
    public Task ShowSettingsAsync(ViewModels.SettingsViewModel settings) => ShowAsync(new Views.SettingsWindow { DataContext = settings }, false);

    /// <inheritdoc />
    public async Task<string?> PickSaveFileAsync(string title, string suggestedName)
    {
        if (_owner() is not { } owner)
        {
            return null;
        }

        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = title, SuggestedFileName = suggestedName }).ConfigureAwait(true);
        return file?.TryGetLocalPath();
    }

    /// <inheritdoc />
    public Task<string?> PromptTextAsync(string title, string label, string initialValue) =>
        ShowAsync<string?>(new PromptDialog(title, label, initialValue), null);

    /// <inheritdoc />
    public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool isDestructive) =>
        ShowAsync(new ConfirmDialog(title, message, confirmText, isDestructive), false);

    /// <inheritdoc />
    public Task<UndefinedVariablesChoice> ConfirmUndefinedVariablesAsync(IReadOnlyList<VariableIssue> issues, string? environmentName) =>
        ShowAsync(new UndefinedVariablesDialog(issues, environmentName), UndefinedVariablesChoice.Cancel);

    /// <inheritdoc />
    public Task ShowErrorAsync(string message) =>
        ShowAsync(new ConfirmDialog(Loc.Get("Error_Title"), message, Loc.Get("Btn_Ok"), isDestructive: false, showCancel: false), false);

    /// <inheritdoc />
    public async Task CopyToClipboardAsync(string text)
    {
        if (_owner()?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text).ConfigureAwait(true);
        }
    }

    private async Task<T> ShowAsync<T>(Window dialog, T whenNoOwner)
    {
        if (_owner() is not { } owner)
        {
            return whenNoOwner;
        }

        var result = await dialog.ShowDialog<T?>(owner).ConfigureAwait(true);
        return result is null ? whenNoOwner : result;
    }
}
