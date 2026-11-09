using Avalonia.Controls;
using Avalonia.Interactivity;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;

namespace JetApi.Desktop.Views.Dialogs;

/// <summary>Asks what to do with unsaved requests before leaving the workspace: save, discard or stay.</summary>
public sealed partial class UnsavedChangesDialog : Window
{
    private const int MaxListed = 6;

    /// <summary>Required by the XAML loader.</summary>
    public UnsavedChangesDialog()
    {
        InitializeComponent();
    }

    /// <summary>Creates the dialog for the given request names.</summary>
    public UnsavedChangesDialog(IReadOnlyList<string> items)
        : this()
    {
        ArgumentNullException.ThrowIfNull(items);
        MessageText.Text = Loc.Format("Unsaved_Message", items.Count);
        var listed = items.Take(MaxListed).Select(i => "• " + i).ToList();
        if (items.Count > MaxListed)
        {
            listed.Add(Loc.Format("Unsaved_More", items.Count - MaxListed));
        }

        ItemsText.Text = string.Join(Environment.NewLine, listed);
        Opened += (_, _) => SaveButton.Focus();
    }

    private void OnSave(object? sender, RoutedEventArgs e) => Close(UnsavedChangesChoice.Save);

    private void OnDiscard(object? sender, RoutedEventArgs e) => Close(UnsavedChangesChoice.Discard);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(UnsavedChangesChoice.Cancel);
}
