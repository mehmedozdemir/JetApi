using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace JetApi.Desktop.Controls;

/// <summary>An icon followed by text, for buttons, links, menu-like rows and notes.</summary>
public sealed class IconLabel : StackPanel
{
    /// <summary>The icon geometry (an <c>Icon.*</c> resource).</summary>
    public static readonly StyledProperty<Geometry?> IconProperty = AvaloniaProperty.Register<IconLabel, Geometry?>(nameof(Icon));

    /// <summary>The text.</summary>
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<IconLabel, string?>(nameof(Text));

    private readonly Icon _icon = new() { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Creates the label.</summary>
    public IconLabel()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 6;
        Children.Add(_icon);
        Children.Add(_text);
    }

    /// <summary>The icon geometry.</summary>
    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>The text.</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IconProperty)
        {
            _icon.Data = Icon;
            _icon.IsVisible = Icon is not null;
        }
        else if (change.Property == TextProperty)
        {
            _text.Text = Text;
        }
    }
}
