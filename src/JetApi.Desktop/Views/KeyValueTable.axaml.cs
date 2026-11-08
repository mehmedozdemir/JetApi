using Avalonia;
using Avalonia.Controls;

namespace JetApi.Desktop.Views;

/// <summary>Editable key/value table for params, headers, form fields and metadata.</summary>
public sealed partial class KeyValueTable : UserControl
{
    /// <summary>Show the description column.</summary>
    public static readonly StyledProperty<bool> ShowDescriptionProperty = AvaloniaProperty.Register<KeyValueTable, bool>(nameof(ShowDescription), true);

    /// <summary>Show the "file" column (multipart).</summary>
    public static readonly StyledProperty<bool> ShowFileToggleProperty = AvaloniaProperty.Register<KeyValueTable, bool>(nameof(ShowFileToggle));

    /// <summary>Creates the table.</summary>
    public KeyValueTable()
    {
        InitializeComponent();
    }

    /// <summary>Show the description column.</summary>
    public bool ShowDescription
    {
        get => GetValue(ShowDescriptionProperty);
        set => SetValue(ShowDescriptionProperty, value);
    }

    /// <summary>Show the "file" column.</summary>
    public bool ShowFileToggle
    {
        get => GetValue(ShowFileToggleProperty);
        set => SetValue(ShowFileToggleProperty, value);
    }
}
