using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using JetApi.Desktop.Services;
using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Controls;

/// <summary>
/// A palette choice in the settings: a small preview in the palette's own colors (its surface with "Aa" in its text color
/// and dots in its accent and two method colors), then its name. Its data context is a <c>SettingsOption&lt;string&gt;</c>
/// holding the palette id.
/// </summary>
public sealed class PaletteChip : StackPanel
{
    /// <summary>Creates the chip.</summary>
    public PaletteChip()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 10;
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Children.Clear();
        if (DataContext is not SettingsOption<string> option)
        {
            return;
        }

        var swatch = Appearance.Swatch(option.Value);
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        foreach (var brush in new[] { swatch.Accent, swatch.First, swatch.Second })
        {
            dots.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = brush });
        }

        var preview = new Border
        {
            Background = swatch.Surface,
            BorderBrush = swatch.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = "Aa", Foreground = swatch.Text, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                    dots,
                },
            },
        };
        Children.Add(preview);
        Children.Add(new TextBlock { Text = option.Label, VerticalAlignment = VerticalAlignment.Center });
    }
}
