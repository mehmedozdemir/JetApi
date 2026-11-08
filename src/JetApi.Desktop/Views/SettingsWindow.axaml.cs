using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

namespace JetApi.Desktop.Views;

/// <summary>The settings window; changes apply and save immediately.</summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>Creates the window.</summary>
    public SettingsWindow()
    {
        InitializeComponent();

        // Decimal separators follow the interface language ("12.5" in English, "12,5" in Turkish).
        foreach (var number in this.GetLogicalDescendants().OfType<NumericUpDown>())
        {
            number.NumberFormat = JetApi.Desktop.Localization.Loc.Instance.Culture.NumberFormat;
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
