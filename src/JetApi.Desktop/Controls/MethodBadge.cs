using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace JetApi.Desktop.Controls;

/// <summary>A request method ("GET", "gRPC", ...) in its theme color.</summary>
public sealed class MethodBadge : TextBlock
{
    private IDisposable? _foreground;

    /// <summary>Applies the default look.</summary>
    public MethodBadge()
    {
        FontSize = 10.5;
        FontWeight = FontWeight.SemiBold;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        this[!FontFamilyProperty] = this.GetResourceObservable("App.MonoFont").ToBinding();
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(TextBlock);

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            var key = Text switch
            {
                "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "gRPC" => "Method." + Text,
                "DEL" => "Method.DELETE",
                _ => "Method.Other",
            };
            _foreground?.Dispose();
            _foreground = Bind(ForegroundProperty, this.GetResourceObservable(key));
        }
    }
}
