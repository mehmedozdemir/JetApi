using Avalonia.Controls;
using Avalonia.Interactivity;

namespace JetApi.Desktop.Views.Dialogs;

/// <summary>A yes/no question, or a message with a single button.</summary>
public sealed partial class ConfirmDialog : Window
{
    /// <summary>Required by the XAML loader.</summary>
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>Creates the dialog.</summary>
    /// <param name="title">Title.</param>
    /// <param name="message">Message.</param>
    /// <param name="confirmText">Text of the confirming button.</param>
    /// <param name="isDestructive">Styles the confirming button as destructive.</param>
    /// <param name="showCancel">False for a message with only one button.</param>
    public ConfirmDialog(string title, string message, string confirmText, bool isDestructive, bool showCancel = true)
        : this()
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.IsVisible = showCancel;
        if (isDestructive)
        {
            ConfirmButton.Classes.Remove("primary");
            ConfirmButton.Classes.Add("danger");
            ConfirmButton.IsDefault = false;
            Opened += (_, _) => CancelButton.Focus();
        }
        else
        {
            Opened += (_, _) => ConfirmButton.Focus();
        }
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
