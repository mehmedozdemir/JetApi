using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace JetApi.Desktop.Controls;

/// <summary>Returns a theme brush when the value is true, otherwise transparent.</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    private readonly string _resourceKey;

    private BoolToBrushConverter(string resourceKey)
    {
        _resourceKey = resourceKey;
    }

    /// <summary>The selection highlight.</summary>
    public static BoolToBrushConverter Selection { get; } = new("App.Selection");

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && Application.Current is { } app && app.TryGetResource(_resourceKey, app.ActualThemeVariant, out var brush)
            ? brush
            : Brushes.Transparent;

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
