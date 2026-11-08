using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace JetApi.Desktop.Localization;

/// <summary>XAML: <c>Text="{l:Tr Btn_Send}"</c> binds to a UI string that follows language changes.</summary>
public sealed class TrExtension : MarkupExtension
{
    /// <summary>Creates the extension.</summary>
    public TrExtension()
    {
    }

    /// <summary>Creates the extension for <paramref name="key"/>.</summary>
    public TrExtension(string key)
    {
        Key = key;
    }

    /// <summary>The string key in Strings.resx.</summary>
    public string Key { get; set; } = string.Empty;

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay };
}
