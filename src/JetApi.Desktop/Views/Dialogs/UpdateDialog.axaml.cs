using Avalonia.Controls;
using Avalonia.Interactivity;
using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Views.Dialogs;

/// <summary>Shows a newer release and installs it.</summary>
public sealed partial class UpdateDialog : Window
{
    /// <summary>Creates the dialog; set an <see cref="UpdateDialogViewModel"/> as data context.</summary>
    public UpdateDialog()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        // Closing the window stops a running download.
        if (DataContext is UpdateDialogViewModel { IsDownloading: true } update)
        {
            update.CancelDownloadCommand.Execute(null);
        }
    }

    private void OnLater(object? sender, RoutedEventArgs e) => Close(false);
}
