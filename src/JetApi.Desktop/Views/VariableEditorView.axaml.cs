using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Views;

/// <summary>The variable card shown over a <c>{{variable}}</c> or for "Set as variable".</summary>
public partial class VariableEditorView : UserControl
{
    /// <summary>Creates the view.</summary>
    public VariableEditorView() => InitializeComponent();

    /// <summary>Moves focus to the first field (name for a new variable, otherwise the value).</summary>
    public void FocusFirstField() => Dispatcher.UIThread.Post(() =>
    {
        var box = DataContext is VariableEditorViewModel { IsNew: true } ? NameBox : ValueBox;
        box.Focus(NavigationMethod.Tab);
        box.SelectAll();
    });
}
