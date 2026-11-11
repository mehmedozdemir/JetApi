using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using JetApi.Desktop.ViewModels;
using JetApi.Desktop.Views;
using JetApi.Grpc.Codec;
using JetApi.Grpc.Schema;

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

    /// <summary>The message the text is the JSON of (gRPC message editor): its fields are offered too.</summary>
    ProtoMessageType? MessageType => null;

    /// <summary>The bottom-left corner of the caret, in <see cref="Anchor"/> coordinates.</summary>
    Point CaretBottom();

    /// <summary>Applies an edit and moves the caret.</summary>
    void Apply(CompletionEdit edit);
}

/// <summary>
/// Lists the variables of the request after <c>{{</c> is typed, filtered as the name is typed. In a message editor it
/// also lists the fields that can be written where a property name is being typed (or after Ctrl+Space). Up/Down
/// move, Enter or Tab insert, Escape closes. The list never takes focus from the field.
/// </summary>
internal sealed class VariableCompletion
{
    private static readonly ConditionalWeakTable<Control, VariableCompletion> Completions = [];

    private readonly ICompletionHost _host;
    private readonly Flyout _flyout;
    private readonly VariableCompletionView _view = new();
    private CompletionPrefix? _prefix;
    private ProtoJsonCompletion? _fields;
    private int _openedAt = -1;
    private bool _fieldsRequested;

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
        _flyout.Closed += (_, _) => Reset();
        _view.Chosen += (_, item) => Insert(item);
    }

    /// <summary>The list is open.</summary>
    internal bool IsOpen => _flyout.IsOpen;

    /// <summary>The offered variables while open.</summary>
    internal IReadOnlyList<VariableSuggestion> Items => IsOpen ? [.. _view.Items.OfType<VariableSuggestion>()] : [];

    /// <summary>The offered message fields while open.</summary>
    internal IReadOnlyList<FieldSuggestion> Fields => IsOpen ? [.. _view.Items.OfType<FieldSuggestion>()] : [];

    /// <summary>The highlighted variable.</summary>
    internal VariableSuggestion? Selected => IsOpen ? _view.Selected as VariableSuggestion : null;

    /// <summary>The highlighted field.</summary>
    internal FieldSuggestion? SelectedField => IsOpen ? _view.Selected as FieldSuggestion : null;

    /// <summary>Sets up completion for a field.</summary>
    internal static VariableCompletion Attach(ICompletionHost host) => Completions.GetValue(host.Anchor, _ => new VariableCompletion(host));

    /// <summary>The completion of <paramref name="anchor"/>, if attached.</summary>
    internal static VariableCompletion? Find(Control anchor) => Completions.TryGetValue(anchor, out var completion) ? completion : null;

    /// <summary>Call after the text changed: opens, filters or closes the list (after the caret has moved).</summary>
    internal void OnTextChanged() => Dispatcher.UIThread.Post(() => Update());

    /// <summary>Ctrl+Space: offers the fields that can be written at the caret even before a name is started.</summary>
    internal void Request()
    {
        if (_host.MessageType is null)
        {
            return;
        }

        _fieldsRequested = true;
        Update();
    }

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

        Reset();
    }

    /// <summary>Opens, filters or closes the list for the current text and caret.</summary>
    internal void Update()
    {
        if (!_host.IsTyping)
        {
            Close();
            return;
        }

        var context = _host.Context;
        var prefix = context?.Store is null ? null : VariableCompletionText.FindPrefix(_host.Text, _host.Caret);
        var variables = prefix is null ? [] : VariableCompletionText.Filter(context!.Suggestions(), prefix.Prefix);
        if (prefix is not null && variables.Count > 0)
        {
            _fields = null;
            _prefix = prefix;
            Show(prefix.Start, variables);
            return;
        }

        // Fields: while a property name is typed, or after Ctrl+Space until the caret leaves the name position.
        var fields = _host.MessageType is { } type ? ProtoJsonCompletion.Find(type, _host.Text, _host.Caret) : null;
        if (fields is { Fields.Count: > 0 } && (fields.IsTyped || _fieldsRequested))
        {
            _prefix = null;
            _fields = fields;
            _fieldsRequested = true;
            Show(-2 - fields.Start, [.. fields.Fields.Select(FieldSuggestion.From)]);
            return;
        }

        Close();
    }

    // Shows the items; the list moves only when it was not already open for the same name (variables: the index after
    // "{{", fields: minus two minus the start of the name).
    private void Show(int openedAt, IReadOnlyList<object> items)
    {
        var wasOpenHere = IsOpen && _openedAt == openedAt;
        _view.Items = items;
        if (!wasOpenHere)
        {
            _flyout.Hide();
            _openedAt = openedAt;
            var caret = _host.CaretBottom();
            _flyout.HorizontalOffset = caret.X - 8;
            _flyout.VerticalOffset = caret.Y - _host.Anchor.Bounds.Height + 4;
            _flyout.ShowAt(_host.Anchor);
        }
    }

    private void Reset()
    {
        _prefix = null;
        _fields = null;
        _openedAt = -1;
        _fieldsRequested = false;
    }

    private void Insert(object item)
    {
        CompletionEdit? edit = null;
        if (item is VariableSuggestion variable && _prefix is { } prefix)
        {
            edit = VariableCompletionText.Apply(_host.Text, prefix, variable.Name);
        }
        else if (item is FieldSuggestion field && _fields is { } fields)
        {
            var insert = fields.Insert(field.Field);
            edit = new CompletionEdit(insert.Start, insert.Length, insert.Text, insert.SelectionStart, insert.SelectionLength);
        }

        Close();
        if (edit is not null)
        {
            _host.Apply(edit);
        }
    }
}
