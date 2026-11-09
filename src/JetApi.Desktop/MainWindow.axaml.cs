using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using JetApi.Desktop.ViewModels;
using JetApi.Desktop.Views;

namespace JetApi.Desktop;

/// <summary>The main window. Registers the keyboard shortcuts (Cmd on macOS, Ctrl elsewhere).</summary>
public sealed partial class MainWindow : Window
{
    /// <summary>Creates the window.</summary>
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnShortcut, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(Controls.SplitPane.ShareChangedEvent, (_, e) => ViewModel?.SetRequestShare(e.IsStacked, e.FirstShare));
    }

    private bool _closeConfirmed;

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    /// <summary>Asks about unsaved requests before the window closes.</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnClosing(e);
        if (e.Cancel || _closeConfirmed || ViewModel?.Workspace is not { HasUnsavedChanges: true } workspace)
        {
            return;
        }

        e.Cancel = true;
        _ = ConfirmCloseAsync(workspace);
    }

    private async Task ConfirmCloseAsync(WorkspaceViewModel workspace)
    {
        if (await workspace.PrepareToLeaveAsync().ConfigureAwait(true))
        {
            _closeConfirmed = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(Close);
        }
    }

    private void OnShortcut(object? sender, KeyEventArgs e)
    {
        var command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.Key == Key.OemComma && e.KeyModifiers.HasFlag(command) && ViewModel is { } main)
        {
            main.OpenSettings();
            e.Handled = true;
            return;
        }

        if (ViewModel?.Workspace is not { } workspace)
        {
            return;
        }

        if (e.Key == Key.Escape && workspace.Palette is not null)
        {
            workspace.Palette = null;
            e.Handled = true;
            return;
        }

        if (!e.KeyModifiers.HasFlag(command))
        {
            return;
        }

        if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            ViewModel?.ToggleResponsePane();
            e.Handled = true;
            return;
        }

        e.Handled = e.Key switch
        {
            Key.Enter => Run(() => workspace.SelectedTab?.SendCommand.Execute(null)),
            Key.S => Run(() => workspace.SelectedTab?.SaveCommand.Execute(null)),
            Key.K or Key.P => Run(workspace.OpenPalette),
            Key.N => Run(() => workspace.NewHttpRequestCommand.Execute(null)),
            Key.E => Run(workspace.ShowEnvironments),
            Key.W => Run(() => workspace.CloseTabCommand.Execute(null)),
            Key.L => Run(() => this.FindDescendantOfType<WorkspaceView>()?.FocusUrl()),
            _ => false,
        };
    }

    private static bool Run(Action action)
    {
        action();
        return true;
    }
}
