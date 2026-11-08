using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace JetApi.Desktop.Controls;

/// <summary>True → semi-bold, false → normal.</summary>
public sealed class BoolToFontWeightConverter : IValueConverter
{
    /// <summary>Shared instance.</summary>
    public static BoolToFontWeightConverter Instance { get; } = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? FontWeight.SemiBold : FontWeight.Normal;

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
