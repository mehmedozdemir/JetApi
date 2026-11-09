using System.Globalization;
using Avalonia.Data.Converters;

namespace JetApi.Desktop.Controls;

/// <summary>Turns an icon resource key (<c>Icon.Plus</c>) into its geometry, for view models that name icons.</summary>
public sealed class IconKeyConverter : IValueConverter
{
    /// <summary>The shared instance.</summary>
    public static IconKeyConverter Instance { get; } = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Icon.FromKey(value as string);

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
