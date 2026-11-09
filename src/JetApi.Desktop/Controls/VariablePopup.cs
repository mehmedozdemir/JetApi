using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JetApi.Desktop.ViewModels;
using JetApi.Desktop.Views;

namespace JetApi.Desktop.Controls;

/// <summary>
/// The variable card of one editor (a <see cref="CodeEditor"/> or a text box): opens after the pointer rests on a
/// <c>{{variable}}</c>, stays open while the pointer is over the card or a value is being edited, and closes when the
/// pointer moves away, on Escape, after saving, or on a click elsewhere.
/// </summary>
internal sealed class VariablePopup
{
    private static readonly ConditionalWeakTable<Control, VariablePopup> Popups = [];

    private readonly Control _anchor;
    private readonly Flyout _flyout;
    private readonly DispatcherTimer _openTimer;
    private readonly DispatcherTimer _closeTimer;
    private VariableContext? _pendingContext;
    private VariableHint? _pending;
    private bool _pointerOverCard;
    private TopLevel? _topLevel;
    private Action? _afterClose;

    private VariablePopup(Control anchor)
    {
        _anchor = anchor;
        _flyout = new Flyout
        {
            OverlayDismissEventPassThrough = true,
            VerticalOffset = 10,
            FlyoutPresenterClasses = { "card" },
        };
        _flyout.Closed += (_, _) => OnClosed();
        _openTimer = new DispatcherTimer { Interval = OpenDelay };
        _openTimer.Tick += (_, _) =>
        {
            _openTimer.Stop();
            OpenPending();
        };
        _closeTimer = new DispatcherTimer { Interval = CloseDelay };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            if (!KeepsOpen)
            {
                Hide();
            }
        };
    }

    /// <summary>How long the pointer rests on a variable before the card opens. Zero opens it at once (tests).</summary>
    internal static TimeSpan OpenDelay { get; set; } = TimeSpan.FromMilliseconds(450);

    /// <summary>How long the card stays after the pointer leaves it (time to move onto it). Zero closes at once (tests).</summary>
    internal static TimeSpan CloseDelay { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>The card's view model while it is open.</summary>
    internal VariableEditorViewModel? Current { get; private set; }

    /// <summary>The card's view while it is open.</summary>
    internal Control? View => _flyout.Content as Control;

    /// <summary>The card is open.</summary>
    internal bool IsOpen => _flyout.IsOpen;

    private bool KeepsOpen =>
        _pointerOverCard || (Current?.KeepsOpen ?? false) || (_flyout.Content as Control)?.IsKeyboardFocusWithin == true;

    /// <summary>The card of <paramref name="anchor"/>.</summary>
    internal static VariablePopup For(Control anchor) => Popups.GetValue(anchor, a => new VariablePopup(a));

    /// <summary>The card of <paramref name="anchor"/> if one was ever created.</summary>
    internal static VariablePopup? Find(Control anchor) => Popups.TryGetValue(anchor, out var popup) ? popup : null;

    /// <summary>The pointer moved over the editor: <paramref name="hint"/> is the variable under it, or <c>null</c>.</summary>
    internal void Hover(VariableContext? context, VariableHint? hint)
    {
        if (hint is null || context is null)
        {
            CancelPending();
            ScheduleClose();
            return;
        }

        if (IsOpen && Current?.Hint == hint)
        {
            _closeTimer.Stop();
            return;
        }

        if (_pending == hint && _openTimer.IsEnabled)
        {
            return;
        }

        if (IsOpen && KeepsOpen)
        {
            // Editing another variable; do not replace the card under the user.
            return;
        }

        _pending = hint;
        _pendingContext = context;
        if (OpenDelay == TimeSpan.Zero)
        {
            OpenPending();
        }
        else
        {
            _openTimer.Interval = OpenDelay;
            _openTimer.Stop();
            _openTimer.Start();
        }
    }

    /// <summary>
    /// The pointer left the editor: a card that has not opened yet is cancelled. An open card closes on real pointer
    /// movement away from it (see <see cref="OnTopLevelPointerMoved"/>), because opening a popup also raises a leave.
    /// </summary>
    internal void Leave() => CancelPending();

    /// <summary>Opens the card that turns <paramref name="selectedText"/> into a variable.</summary>
    /// <param name="context">The editor's variables.</param>
    /// <param name="selectedText">The selected text, the initial value.</param>
    /// <param name="usePlaceholder">Replaces the selection with the new placeholder.</param>
    /// <param name="restoreFocus">Gives focus back to the editor after the card closes.</param>
    /// <param name="below">Where the card opens: the bottom-left corner of the selection, in the editor's coordinates.</param>
    internal void ShowForSelection(VariableContext context, string selectedText, Action<string> usePlaceholder, Action restoreFocus, Point below)
    {
        CancelPending();
        Hide();
        var editor = VariableEditorViewModel.ForSelection(context, selectedText);
        editor.Saved += (_, name) =>
        {
            usePlaceholder("{{" + name + "}}");
            Hide();
        };
        Show(editor, takeFocus: true, restoreFocus, below);
    }

    /// <summary>Closes the card.</summary>
    internal void Hide()
    {
        _closeTimer.Stop();
        if (IsOpen)
        {
            _flyout.Hide();
        }
    }

    private void OpenPending()
    {
        if (_pending is not { } hint || _pendingContext is not { } context)
        {
            return;
        }

        _pending = null;
        _pendingContext = null;
        Hide();
        var editor = VariableEditorViewModel.ForHint(context, hint);
        editor.Saved += (_, _) => Hide();
        Show(editor, takeFocus: false, null, null);
    }

    private void Show(VariableEditorViewModel editor, bool takeFocus, Action? restoreFocus, Point? below)
    {
        editor.Dismissed += (_, _) => Hide();
        Current = editor;
        _afterClose = restoreFocus;
        _pointerOverCard = false;

        var view = new VariableEditorView { DataContext = editor };
        view.PointerEntered += (_, _) =>
        {
            _pointerOverCard = true;
            _closeTimer.Stop();
        };
        view.PointerExited += (_, _) => _pointerOverCard = false;
        _flyout.Content = view;
        _topLevel = TopLevel.GetTopLevel(_anchor);
        _topLevel?.AddHandler(InputElement.PointerMovedEvent, OnTopLevelPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);

        // A hover card must not take focus from the field being typed in; "Set as variable" does.
        _flyout.ShowMode = takeFocus ? FlyoutShowMode.Standard : FlyoutShowMode.Transient;
        if (below is { } point)
        {
            // Under the selection, also when the menu was opened from the keyboard.
            _flyout.Placement = PlacementMode.BottomEdgeAlignedLeft;
            _flyout.HorizontalOffset = point.X;
            _flyout.VerticalOffset = point.Y - _anchor.Bounds.Height + 6;
            _flyout.ShowAt(_anchor);
        }
        else
        {
            _flyout.HorizontalOffset = 0;
            _flyout.VerticalOffset = 10;
            _flyout.ShowAt(_anchor, showAtPointer: true);
        }
        if (takeFocus)
        {
            view.FocusFirstField();
        }
    }

    // Pointer movement anywhere in the window: over the card keeps it, over the editor is handled by Hover,
    // anywhere else closes it (unless a value is being edited).
    private void OnTopLevelPointerMoved(object? sender, PointerEventArgs e)
    {
        if (View is { } view && e.Source is Visual source && (source == view || view.IsVisualAncestorOf(source)))
        {
            _pointerOverCard = true;
            _closeTimer.Stop();
            return;
        }

        if (new Rect(_anchor.Bounds.Size).Contains(e.GetPosition(_anchor)))
        {
            return;
        }

        _pointerOverCard = false;
        ScheduleClose();
    }

    private void CancelPending()
    {
        _openTimer.Stop();
        _pending = null;
        _pendingContext = null;
    }

    private void ScheduleClose()
    {
        if (IsOpen && !KeepsOpen)
        {
            _closeTimer.Stop();
            if (CloseDelay == TimeSpan.Zero)
            {
                Hide();
                return;
            }

            _closeTimer.Interval = CloseDelay;
            _closeTimer.Start();
        }
    }

    private void OnClosed()
    {
        _topLevel?.RemoveHandler(InputElement.PointerMovedEvent, OnTopLevelPointerMoved);
        _topLevel = null;
        _closeTimer.Stop();
        Current = null;
        _flyout.Content = null;
        var restore = _afterClose;
        _afterClose = null;
        restore?.Invoke();
    }
}
