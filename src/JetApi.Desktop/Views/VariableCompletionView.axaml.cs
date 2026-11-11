using Avalonia.Controls;
using Avalonia.Input;
using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Views;

/// <summary>The list of variables offered after typing <c>{{</c>, or of message fields (<see cref="FieldSuggestion"/>).</summary>
public partial class VariableCompletionView : UserControl
{
    /// <summary>Creates the view.</summary>
    public VariableCompletionView()
    {
        InitializeComponent();
        List.Tapped += (_, _) =>
        {
            if (List.SelectedItem is { } item)
            {
                Chosen?.Invoke(this, item);
            }
        };
    }

    /// <summary>Raised when an item is clicked.</summary>
    public event EventHandler<object>? Chosen;

    /// <summary>The offered variables or fields.</summary>
    public IReadOnlyList<object> Items
    {
        get => List.ItemsSource as IReadOnlyList<object> ?? [];
        set
        {
            List.ItemsSource = value;
            List.SelectedIndex = value.Count > 0 ? 0 : -1;
        }
    }

    /// <summary>The highlighted item.</summary>
    public object? Selected => List.SelectedItem;

    /// <summary>Moves the highlight by <paramref name="delta"/> items (wrapping).</summary>
    public void Move(int delta)
    {
        var count = Items.Count;
        if (count == 0)
        {
            return;
        }

        List.SelectedIndex = ((List.SelectedIndex + delta) % count + count) % count;
        List.ScrollIntoView(List.SelectedIndex);
    }

    /// <summary>Keys handled while the list is open.</summary>
    internal static bool IsNavigationKey(Key key) => key is Key.Up or Key.Down or Key.Enter or Key.Tab or Key.Escape or Key.PageUp or Key.PageDown;
}
