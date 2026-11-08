using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Controls;

/// <summary>
/// Shows the resolved value and source of a <c>{{variable}}</c> when the pointer rests on it.
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

    /// <summary>Builds the tooltip content for a hint.</summary>
    public static Control CreateTip(VariableHint hint)
    {
        ArgumentNullException.ThrowIfNull(hint);
        var panel = new StackPanel { Spacing = 2, MaxWidth = 480 };
        var title = Text(hint.Placeholder, "mono");
        title.FontWeight = Avalonia.Media.FontWeight.SemiBold;
        panel.Children.Add(title);
        if (hint.IsDefined)
        {
            panel.Children.Add(Text(hint.Value.Length == 0 ? "\"\"" : hint.Value, "mono"));
        }

        panel.Children.Add(Text(hint.Description, "muted", "small"));
        return panel;

        static TextBlock Text(string text, params string[] classes)
        {
            var block = new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Left };
            block.Classes.AddRange(classes);
            return block;
        }
    }

    /// <summary>Opens, moves or closes the tooltip of <paramref name="control"/> for <paramref name="hint"/>.</summary>
    internal static void ShowTip(Control control, VariableHint? hint)
    {
        if (hint is null)
        {
            if (ToolTip.GetTip(control) is not null)
            {
                ToolTip.SetIsOpen(control, false);
                ToolTip.SetTip(control, null);
            }

            return;
        }

        if (ToolTip.GetTip(control) is Control { Tag: VariableHint shown } && shown == hint)
        {
            return;
        }

        var tip = CreateTip(hint);
        tip.Tag = hint;
        ToolTip.SetIsOpen(control, false);
        ToolTip.SetTip(control, tip);
        ToolTip.SetIsOpen(control, true);
    }

    private static void OnIsEnabledChanged(TextBox textBox, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            textBox.PointerMoved += OnPointerMoved;
            textBox.PointerExited += OnPointerExited;
        }
        else
        {
            textBox.PointerMoved -= OnPointerMoved;
            textBox.PointerExited -= OnPointerExited;
        }
    }

    private static void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        ShowTip(textBox, HintUnderPointer(textBox, e));
    }

    private static void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            ShowTip(textBox, null);
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
