using Avalonia.Controls;
using Avalonia.Interactivity;

namespace JetApi.Desktop.Views.Dialogs;

/// <summary>Shows a request as code (cURL, PowerShell, C#, JavaScript, Python).</summary>
public sealed partial class CodeSnippetDialog : Window
{
    /// <summary>Creates the dialog; set a <see cref="ViewModels.CodeSnippetViewModel"/> as data context.</summary>
    public CodeSnippetDialog()
    {
        InitializeComponent();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close(true);
}
