using System.Globalization;
using Avalonia.Data.Converters;

namespace JetApi.Desktop.Controls;

/// <summary>A number minus the parameter (e.g. a viewport width minus margins); never below zero.</summary>
public sealed class SubtractConverter : IValueConverter
{
    /// <summary>The shared instance.</summary>
    public static SubtractConverter Instance { get; } = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double number
            ? Math.Max(0, number - double.Parse(parameter?.ToString() ?? "0", CultureInfo.InvariantCulture))
            : double.NaN;

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
