using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace JetApi.Desktop.Controls;

/// <summary>Binds a radio-style tab to an enum value: true when equal; selecting it sets the value.</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    /// <summary>Shared instance.</summary>
    public static EnumEqualsConverter Instance { get; } = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is not null && string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null ? Enum.Parse(targetType, parameter.ToString()!) : BindingOperations.DoNothing;
}
