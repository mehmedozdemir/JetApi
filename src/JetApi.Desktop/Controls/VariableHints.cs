using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.VisualTree;
using JetApi.Desktop.Localization;
using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Controls;

/// <summary>
/// Shows the value and source of a <c>{{variable}}</c> in an editable card when the pointer rests on it.
/// <see cref="ContextProperty"/> is inherited, so a request view sets it once and every editor
/// below it (including rows in item templates) picks it up without bindings.
/// Text boxes opt in with <see cref="IsEnabledProperty"/>; <see cref="CodeEditor"/> also highlights.
/// </summary>
public static class VariableHints
{
    /// <summary>The variables of the enclosing request tab. Inherited.</summary>
    public static readonly AttachedProperty<VariableContext?> ContextProperty =
        AvaloniaProperty.RegisterAttached<Control, VariableContext?>("Context", typeof(VariableHints), inherits: true);

    /// <summary>Shows variable hints on hover in a text box.</summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("IsEnabled", typeof(VariableHints));

    static VariableHints() => IsEnabledProperty.Changed.AddClassHandler<TextBox>(OnIsEnabledChanged);

    /// <summary>Gets the context.</summary>
    public static VariableContext? GetContext(Control control) => control.GetValue(ContextProperty);

    /// <summary>Sets the context.</summary>
    public static void SetContext(Control control, VariableContext? value) => control.SetValue(ContextProperty, value);

    /// <summary>Gets whether hints are enabled.</summary>
    public static bool GetIsEnabled(TextBox textBox) => textBox.GetValue(IsEnabledProperty);

    /// <summary>Sets whether hints are enabled.</summary>
    public static void SetIsEnabled(TextBox textBox, bool value) => textBox.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(TextBox textBox, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            textBox.PointerMoved += OnPointerMoved;
            textBox.PointerExited += OnPointerExited;
            textBox.ContextFlyout = CreateTextBoxMenu(textBox);
            textBox.TextChanged += OnTextChanged;
            textBox.LostFocus += OnTextChanged;
            textBox.AddHandler(InputElement.KeyDownEvent, OnCompletionKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }
        else
        {
            textBox.PointerMoved -= OnPointerMoved;
            textBox.PointerExited -= OnPointerExited;
            textBox.TextChanged -= OnTextChanged;
            textBox.LostFocus -= OnTextChanged;
            textBox.RemoveHandler(InputElement.KeyDownEvent, OnCompletionKeyDown);
            VariableCompletion.Find(textBox)?.Close();
            VariablePopup.Find(textBox)?.Hide();
        }
    }

    /// <summary>The "Set as variable…" menu item; enabled by the owner when text is selected.</summary>
    internal static MenuItem CreateSetAsVariableItem(Action click)
    {
        var item = new MenuItem { Header = Loc.Get("Var_SetAsVariable") };
        item.Click += (_, _) => click();
        return item;
    }

    /// <summary>Whether <paramref name="selected"/> can become a variable value from an editor with <paramref name="context"/>.</summary>
    internal static bool CanSetAsVariable(VariableContext? context, string? selected) =>
        context?.Store is not null && !string.IsNullOrEmpty(selected) && selected.AsSpan().IndexOfAny('\r', '\n') < 0;

    // Cut, copy and paste as in the default menu, plus "Set as variable…" for the selected text.
    private static MenuFlyout CreateTextBoxMenu(TextBox textBox)
    {
        var cut = new MenuItem { Header = Loc.Get("Menu_Cut"), InputGesture = new KeyGesture(Key.X, KeyModifiers.Control) };
        cut.Click += (_, _) => textBox.Cut();
        var copy = new MenuItem { Header = Loc.Get("Btn_Copy"), InputGesture = new KeyGesture(Key.C, KeyModifiers.Control) };
        copy.Click += (_, _) => textBox.Copy();
        var paste = new MenuItem { Header = Loc.Get("Menu_Paste"), InputGesture = new KeyGesture(Key.V, KeyModifiers.Control) };
        paste.Click += (_, _) => textBox.Paste();
        var separator = new Separator();
        var setAsVariable = CreateSetAsVariableItem(() => SetSelectionAsVariable(textBox));

        var menu = new MenuFlyout { Items = { cut, copy, paste, separator, setAsVariable } };
        menu.Opening += (_, _) =>
        {
            cut.IsEnabled = textBox.CanCut;
            copy.IsEnabled = textBox.CanCopy;
            paste.IsEnabled = textBox.CanPaste;
            var available = GetContext(textBox)?.Store is not null && textBox.PasswordChar == default;
            separator.IsVisible = setAsVariable.IsVisible = available;
            setAsVariable.IsEnabled = available && CanSetAsVariable(GetContext(textBox), textBox.SelectedText);
        };
        return menu;
    }

    private static void SetSelectionAsVariable(TextBox textBox)
    {
        var context = GetContext(textBox);
        var selected = textBox.SelectedText;
        if (!CanSetAsVariable(context, selected))
        {
            return;
        }

        var text = textBox.Text ?? string.Empty;
        var start = Math.Min(textBox.SelectionStart, textBox.SelectionEnd);
        var end = Math.Max(textBox.SelectionStart, textBox.SelectionEnd);
        VariablePopup.For(textBox).ShowForSelection(
            context!,
            selected,
            placeholder =>
            {
                // Only when the text was not changed meanwhile.
                if (textBox.Text == text)
                {
                    textBox.Text = text[..start] + placeholder + text[end..];
                    textBox.CaretIndex = start + placeholder.Length;
                }
            },
            () => textBox.Focus(),
            new Point(0, textBox.Bounds.Height - 6));
    }

    private static void OnTextChanged(object? sender, EventArgs e)
    {
        if (sender is TextBox textBox && (GetContext(textBox) is not null || VariableCompletion.Find(textBox) is not null))
        {
            VariableCompletion.Attach(new TextBoxCompletionHost(textBox)).OnTextChanged();
        }
    }

    private static void OnCompletionKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is TextBox textBox && VariableCompletion.Find(textBox)?.HandleKey(e.Key) == true)
        {
            e.Handled = true;
        }
    }

    // "{{" completion in a text box (table cells, auth fields).
    private sealed class TextBoxCompletionHost(TextBox textBox) : ICompletionHost
    {
        public Control Anchor => textBox;

        public string Text => textBox.Text ?? string.Empty;

        public int Caret => textBox.CaretIndex;

        public bool IsTyping => textBox.IsKeyboardFocusWithin && textBox.PasswordChar == default && !textBox.IsReadOnly;

        public VariableContext? Context => GetContext(textBox);

        public Point CaretBottom()
        {
            if (textBox.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault() is not { } presenter)
            {
                return new Point(0, textBox.Bounds.Height);
            }

            var rect = presenter.TextLayout.HitTestTextPosition(Math.Min(textBox.CaretIndex, Text.Length));
            return presenter.TranslatePoint(rect.BottomLeft, textBox) ?? new Point(0, textBox.Bounds.Height);
        }

        public void Apply(CompletionEdit edit)
        {
            var text = Text;
            textBox.Text = text[..edit.Start] + edit.Insert + text[(edit.Start + edit.Length)..];
            textBox.CaretIndex = Math.Min(edit.Caret, textBox.Text.Length);
            textBox.Focus();
        }
    }

    private static void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        VariablePopup.For(textBox).Hover(GetContext(textBox), HintUnderPointer(textBox, e));
    }

    private static void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            VariablePopup.Find(textBox)?.Leave();
        }
    }

    private static VariableHint? HintUnderPointer(TextBox textBox, PointerEventArgs e)
    {
        var context = GetContext(textBox);
        var text = textBox.Text;
        if (context is null || textBox.PasswordChar != default || string.IsNullOrEmpty(text) || !text.Contains("{{", StringComparison.Ordinal))
        {
            return null;
        }

        var presenter = textBox.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
        if (presenter is null)
        {
            return null;
        }

        var hit = presenter.TextLayout.HitTestPoint(e.GetPosition(presenter));
        return hit.IsInside ? context.DescribeAt(text, hit.TextPosition) : null;
    }
}
