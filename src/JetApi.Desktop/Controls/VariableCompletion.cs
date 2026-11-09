using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using JetApi.Desktop.ViewModels;
using JetApi.Desktop.Views;

namespace JetApi.Desktop.Controls;

/// <summary>What <see cref="VariableCompletion"/> needs from the field it completes in.</summary>
internal interface ICompletionHost
{
    /// <summary>The control the list opens under.</summary>
    Control Anchor { get; }

    /// <summary>The field's text.</summary>
    string Text { get; }

    /// <summary>The caret index.</summary>
    int Caret { get; }

    /// <summary>The field has keyboard focus (the user is typing).</summary>
    bool IsTyping { get; }

    /// <summary>The variables of the enclosing request, or <c>null</c>.</summary>
    VariableContext? Context { get; }

    /// <summary>The bottom-left corner of the caret, in <see cref="Anchor"/> coordinates.</summary>
    Point CaretBottom();

    /// <summary>Applies an edit and moves the caret.</summary>
    void Apply(CompletionEdit edit);
}

/// <summary>
/// Lists the variables of the request after <c>{{</c> is typed, filtered as the name is typed. Up/Down move,
/// Enter or Tab insert the name and the closing braces, Escape closes. The list never takes focus from the field.
/// </summary>
internal sealed class VariableCompletion
{
    private static readonly ConditionalWeakTable<Control, VariableCompletion> Completions = [];

    private readonly ICompletionHost _host;
    private readonly Flyout _flyout;
    private readonly VariableCompletionView _view = new();
    private CompletionPrefix? _prefix;

    private VariableCompletion(ICompletionHost host)
    {
        _host = host;
        _flyout = new Flyout
        {
            Content = _view,
            ShowMode = FlyoutShowMode.Transient,
            OverlayDismissEventPassThrough = true,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            FlyoutPresenterClasses = { "card" },
        };
        _flyout.Closed += (_, _) => _prefix = null;
        _view.Chosen += (_, suggestion) => Insert(suggestion);
    }

    /// <summary>The list is open.</summary>
    internal bool IsOpen => _flyout.IsOpen;

    /// <summary>The offered variables while open.</summary>
    internal IReadOnlyList<VariableSuggestion> Items => IsOpen ? _view.Items : [];

    /// <summary>The highlighted variable.</summary>
    internal VariableSuggestion? Selected => IsOpen ? _view.Selected : null;

    /// <summary>Sets up completion for a field.</summary>
    internal static VariableCompletion Attach(ICompletionHost host) => Completions.GetValue(host.Anchor, _ => new VariableCompletion(host));

    /// <summary>The completion of <paramref name="anchor"/>, if attached.</summary>
    internal static VariableCompletion? Find(Control anchor) => Completions.TryGetValue(anchor, out var completion) ? completion : null;

    /// <summary>Call after the text changed: opens, filters or closes the list (after the caret has moved).</summary>
    internal void OnTextChanged() => Dispatcher.UIThread.Post(Update);

    /// <summary>Handles a key while the list is open; returns true when it was used.</summary>
    internal bool HandleKey(Key key)
    {
        if (!IsOpen || !VariableCompletionView.IsNavigationKey(key))
        {
            return false;
        }

        switch (key)
        {
            case Key.Up:
                _view.Move(-1);
                break;
            case Key.Down:
                _view.Move(1);
                break;
            case Key.PageUp:
                _view.Move(-6);
                break;
            case Key.PageDown:
                _view.Move(6);
                break;
            case Key.Escape:
                Close();
                break;
            default:
                if (_view.Selected is { } selected)
                {
                    Insert(selected);
                }

                break;
        }

        return true;
    }

    /// <summary>Closes the list.</summary>
    internal void Close()
    {
        if (IsOpen)
        {
            _flyout.Hide();
        }

        _prefix = null;
    }

    /// <summary>Opens, filters or closes the list for the current text and caret.</summary>
    internal void Update()
    {
        var context = _host.Context;
        var prefix = context?.Store is null || !_host.IsTyping ? null : VariableCompletionText.FindPrefix(_host.Text, _host.Caret);
        var items = prefix is null ? [] : VariableCompletionText.Filter(context!.Suggestions(), prefix.Prefix);
        if (prefix is null || items.Count == 0)
        {
            Close();
            return;
        }

        var wasOpenHere = IsOpen && _prefix?.Start == prefix.Start;
        _prefix = prefix;
        _view.Items = items;
        if (!wasOpenHere)
        {
            _flyout.Hide();
            var caret = _host.CaretBottom();
            _flyout.HorizontalOffset = caret.X - 8;
            _flyout.VerticalOffset = caret.Y - _host.Anchor.Bounds.Height + 4;
            _flyout.ShowAt(_host.Anchor);
        }
    }

    private void Insert(VariableSuggestion suggestion)
    {
        if (_prefix is not { } prefix)
        {
            return;
        }

        var edit = VariableCompletionText.Apply(_host.Text, prefix, suggestion.Name);
        Close();
        _host.Apply(edit);
    }
}
