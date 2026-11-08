using Avalonia.Controls;
using Avalonia.Interactivity;

namespace JetApi.Desktop.Views.Dialogs;

/// <summary>Asks for one line of text.</summary>
public sealed partial class PromptDialog : Window
{
    /// <summary>Required by the XAML loader.</summary>
    public PromptDialog()
    {
        InitializeComponent();
    }

    /// <summary>Creates the dialog.</summary>
    public PromptDialog(string title, string label, string initialValue)
        : this()
    {
        Title = title;
        TitleText.Text = title;
        LabelText.Text = label;
        Input.Text = initialValue;
        Opened += (_, _) =>
        {
            Input.Focus();
            Input.SelectAll();
        };
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(Input.Text);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
