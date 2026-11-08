using Avalonia.Controls;
using Avalonia.Input;
using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Views;

/// <summary>The Ctrl+K command palette.</summary>
public sealed partial class CommandPaletteView : UserControl
{
    /// <summary>Creates the view.</summary>
    public CommandPaletteView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => QueryBox.Focus();
    }

    private CommandPaletteViewModel? ViewModel => DataContext as CommandPaletteViewModel;

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                ViewModel?.MoveCommand.Execute(1);
                e.Handled = true;
                break;
            case Key.Up:
                ViewModel?.MoveCommand.Execute(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                ViewModel?.ExecuteSelectedCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                ViewModel?.CloseCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnBackgroundPressed(object? sender, PointerPressedEventArgs e) => ViewModel?.CloseCommand.Execute(null);

    private void OnCardPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;
}
