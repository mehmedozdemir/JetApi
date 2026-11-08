using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace JetApi.Desktop.Controls;

/// <summary>A request method ("GET", "gRPC", ...) in its theme color.</summary>
public sealed class MethodBadge : TextBlock
{
    private IDisposable? _foreground;

    /// <summary>Applies the default look.</summary>
    public MethodBadge()
    {
        FontSize = 11;
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
            _foreground?.Dispose();
            _foreground = Bind(ForegroundProperty, this.GetResourceObservable(ResourceKey(Text)));
        }
    }

    /// <summary>The color token of a method: "Method.POST", or "Method.Other" for custom ones.</summary>
    public static string ResourceKey(string? method) => method?.Trim().ToUpperInvariant() switch
    {
        "GET" or "POST" or "PUT" or "PATCH" or "DELETE" => "Method." + method.Trim().ToUpperInvariant(),
        "GRPC" => "Method.gRPC",
        "DEL" => "Method.DELETE",
        _ => "Method.Other",
    };
}

/// <summary>Colors any control's text by an HTTP method, following the theme (used by the editable method box).</summary>
public static class MethodColor
{
    /// <summary>The method whose color the control's foreground takes.</summary>
    public static readonly AttachedProperty<string?> MethodProperty =
        AvaloniaProperty.RegisterAttached<TemplatedControl, string?>("Method", typeof(MethodColor));

    private static readonly AttachedProperty<IDisposable?> SubscriptionProperty =
        AvaloniaProperty.RegisterAttached<TemplatedControl, IDisposable?>("Subscription", typeof(MethodColor));

    static MethodColor() => MethodProperty.Changed.AddClassHandler<TemplatedControl>((control, _) =>
    {
        control.GetValue(SubscriptionProperty)?.Dispose();
        control.SetValue(SubscriptionProperty, control.Bind(TemplatedControl.ForegroundProperty, control.GetResourceObservable(MethodBadge.ResourceKey(GetMethod(control)))));
    });

    /// <summary>Gets the method.</summary>
    public static string? GetMethod(TemplatedControl control) => control.GetValue(MethodProperty);

    /// <summary>Sets the method.</summary>
    public static void SetMethod(TemplatedControl control, string? value) => control.SetValue(MethodProperty, value);
}
