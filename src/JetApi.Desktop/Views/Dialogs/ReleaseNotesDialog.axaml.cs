using Avalonia.Controls;
using Avalonia.Interactivity;
using JetApi.Desktop.Services;

namespace JetApi.Desktop.Views.Dialogs;

/// <summary>Release notes of each version.</summary>
public sealed partial class ReleaseNotesDialog : Window
{
    /// <summary>Creates the dialog; set a <see cref="ViewModels.ReleaseNotesViewModel"/> as data context.</summary>
    public ReleaseNotesDialog()
    {
        InitializeComponent();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close(true);

    private async void OnAllReleases(object? sender, RoutedEventArgs e) => await Launcher.LaunchUriAsync(AppInfo.ReleasesUrl).ConfigureAwait(true);
}
