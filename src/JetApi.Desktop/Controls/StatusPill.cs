using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Controls;

/// <summary>A status label ("201 Created", "0 · OK") colored by its <see cref="StatusTone"/>.</summary>
public sealed class StatusPill : Border
{
    /// <summary>The tone.</summary>
    public static readonly StyledProperty<StatusTone> ToneProperty = AvaloniaProperty.Register<StatusPill, StatusTone>(nameof(Tone));

    /// <summary>The text.</summary>
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<StatusPill, string?>(nameof(Text));

    private readonly TextBlock _text = new() { FontWeight = FontWeight.SemiBold, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
    private IDisposable? _background;
    private IDisposable? _foreground;

    /// <summary>Creates the pill.</summary>
    public StatusPill()
    {
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(8, 3);
        VerticalAlignment = VerticalAlignment.Center;
        Child = _text;
        ApplyTone();
    }

    /// <summary>The tone.</summary>
    public StatusTone Tone
    {
        get => GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
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
        if (change.Property == ToneProperty)
        {
            ApplyTone();
        }
        else if (change.Property == TextProperty)
        {
            _text.Text = Text;
            IsVisible = !string.IsNullOrEmpty(Text);
        }
    }

    private void ApplyTone()
    {
        _background?.Dispose();
        _foreground?.Dispose();
        _background = Bind(BackgroundProperty, this.GetResourceObservable($"Tone.{Tone}.Bg"));
        _foreground = _text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable($"Tone.{Tone}.Fg"));
    }
}
