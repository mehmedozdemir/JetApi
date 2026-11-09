using Avalonia.Controls;
using Avalonia.Input;
using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Views;

/// <summary>The list of variables offered after typing <c>{{</c>.</summary>
public partial class VariableCompletionView : UserControl
{
    /// <summary>Creates the view.</summary>
    public VariableCompletionView()
    {
        InitializeComponent();
        List.Tapped += (_, _) =>
        {
            if (List.SelectedItem is VariableSuggestion suggestion)
            {
                Chosen?.Invoke(this, suggestion);
            }
        };
    }

    /// <summary>Raised when an item is clicked.</summary>
    public event EventHandler<VariableSuggestion>? Chosen;

    /// <summary>The offered variables.</summary>
    public IReadOnlyList<VariableSuggestion> Items
    {
        get => List.ItemsSource as IReadOnlyList<VariableSuggestion> ?? [];
        set
        {
            List.ItemsSource = value;
            List.SelectedIndex = value.Count > 0 ? 0 : -1;
        }
    }

    /// <summary>The highlighted variable.</summary>
    public VariableSuggestion? Selected => List.SelectedItem as VariableSuggestion;

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
