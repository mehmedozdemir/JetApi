using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Search;
using AvaloniaEdit.TextMate;
using JetApi.Desktop.ViewModels;
using TextMateSharp.Grammars;

namespace JetApi.Desktop.Controls;

/// <summary>
/// A code editor with syntax highlighting (TextMate grammars) and a bindable <see cref="Text"/>.
/// Rendering is virtualized, so large responses stay responsive.
/// </summary>
public sealed class CodeEditor : UserControl
{
    /// <summary>The text.</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<CodeEditor, string?>(nameof(Text), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>json, xml or text.</summary>
    public static readonly StyledProperty<string?> LanguageProperty = AvaloniaProperty.Register<CodeEditor, string?>(nameof(Language), "text");

    /// <summary>Read-only.</summary>
    public static readonly StyledProperty<bool> IsReadOnlyProperty = AvaloniaProperty.Register<CodeEditor, bool>(nameof(IsReadOnly));

    /// <summary>Show line numbers.</summary>
    public static readonly StyledProperty<bool> ShowLineNumbersProperty = AvaloniaProperty.Register<CodeEditor, bool>(nameof(ShowLineNumbers), true);

    /// <summary>Highlight <c>{{variables}}</c> and show their values on hover (request editors only).</summary>
    public static readonly StyledProperty<bool> HighlightVariablesProperty = AvaloniaProperty.Register<CodeEditor, bool>(nameof(HighlightVariables));

    /// <summary>One line only, like a text box (URL fields): no line breaks, Tab moves focus, Enter runs <see cref="EnterCommand"/>.</summary>
    public static readonly StyledProperty<bool> IsSingleLineProperty = AvaloniaProperty.Register<CodeEditor, bool>(nameof(IsSingleLine));

    /// <summary>Shown while the editor is empty.</summary>
    public static readonly StyledProperty<string?> PlaceholderTextProperty = AvaloniaProperty.Register<CodeEditor, string?>(nameof(PlaceholderText));

    /// <summary>Runs on Enter in single-line mode (e.g. send the request).</summary>
    public static readonly StyledProperty<ICommand?> EnterCommandProperty = AvaloniaProperty.Register<CodeEditor, ICommand?>(nameof(EnterCommand));

    private readonly TextEditor _editor;
    private readonly TextBlock _placeholder;
    private readonly SearchPanel _search;
    private readonly RegistryOptions _registry;
    private readonly TextMate.Installation _textMate;
    private readonly VariableColorizer _variables = new();
    private bool _updating;

    /// <summary>Creates the editor.</summary>
    public CodeEditor()
    {
        _editor = new TextEditor
        {
            ShowLineNumbers = true,
            WordWrap = false,
            FontSize = 12.5,
            Padding = new Thickness(4, 6),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        _editor.Options.EnableHyperlinks = false;
        _editor.Options.EnableEmailHyperlinks = false;
        _editor.Options.ConvertTabsToSpaces = true;
        _editor.Options.IndentationSize = 2;
        _editor[!TextEditor.FontFamilyProperty] = this.GetResourceObservable("App.MonoFont").ToBinding();
        _editor[!TextEditor.ForegroundProperty] = this.GetResourceObservable("App.Text").ToBinding();
        _editor[!TextEditor.LineNumbersForegroundProperty] = this.GetResourceObservable("App.TextFaint").ToBinding();
        _editor.Background = Brushes.Transparent;

        _registry = new RegistryOptions(ThemeName.LightPlus);
        _textMate = _editor.InstallTextMate(_registry);
        _editor.Document.TextChanged += (_, _) =>
        {
            UpdatePlaceholder();
            if (IsSingleLine && _editor.Document.Text.AsSpan().IndexOfAny('\r', '\n') >= 0)
            {
                // Pasted or dropped line breaks: keep one line (applied after the change completes).
                Dispatcher.UIThread.Post(RemoveLineBreaks);
                return;
            }

            if (!_updating)
            {
                _updating = true;
                SetCurrentValue(TextProperty, _editor.Document.Text);
                _updating = false;
            }
        };
        _editor.TextArea.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);

        // Ctrl+F opens find (and replace in editable editors).
        _search = SearchPanel.Install(_editor);

        // Added after TextMate so variable colors win over syntax colors.
        _editor.TextArea.TextView.LineTransformers.Add(_variables);
        _editor.TextArea.TextView.PointerMoved += (_, e) => VariableHints.ShowTip(_editor, HintUnderPointer(e));
        _editor.TextArea.TextView.PointerExited += (_, _) => VariableHints.ShowTip(_editor, null);

        _placeholder = new TextBlock
        {
            IsHitTestVisible = false,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            FontSize = 12.5,
            IsVisible = false,
        };
        _placeholder[!TextBlock.FontFamilyProperty] = this.GetResourceObservable("App.MonoFont").ToBinding();
        _placeholder[!TextBlock.ForegroundProperty] = this.GetResourceObservable("App.TextFaint").ToBinding();

        Content = new Panel { Children = { _editor, _placeholder } };
        StyleGutter();
        ApplyLanguage();
        ActualThemeVariantChanged += (_, _) => ApplyTheme();
    }

    /// <summary>Highlight <c>{{variables}}</c> and show their values on hover.</summary>
    public bool HighlightVariables
    {
        get => GetValue(HighlightVariablesProperty);
        set => SetValue(HighlightVariablesProperty, value);
    }

    /// <summary>The text.</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>json, xml or text.</summary>
    public string? Language
    {
        get => GetValue(LanguageProperty);
        set => SetValue(LanguageProperty, value);
    }

    /// <summary>Read-only.</summary>
    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>Show line numbers.</summary>
    public bool ShowLineNumbers
    {
        get => GetValue(ShowLineNumbersProperty);
        set => SetValue(ShowLineNumbersProperty, value);
    }

    /// <summary>One line only (URL fields).</summary>
    public bool IsSingleLine
    {
        get => GetValue(IsSingleLineProperty);
        set => SetValue(IsSingleLineProperty, value);
    }

    /// <summary>Shown while empty.</summary>
    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>Runs on Enter in single-line mode.</summary>
    public ICommand? EnterCommand
    {
        get => GetValue(EnterCommandProperty);
        set => SetValue(EnterCommandProperty, value);
    }

    /// <summary>Moves keyboard focus into the editor.</summary>
    public void FocusEditor() => _editor.TextArea.Focus(NavigationMethod.Tab);

    /// <summary>Selects all text.</summary>
    public void SelectAll() => _editor.SelectAll();

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyTheme();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty && !_updating)
        {
            var text = Text ?? string.Empty;
            if (_editor.Document.Text != text)
            {
                _updating = true;
                _editor.Document.Text = text;
                _editor.ScrollToHome();
                _updating = false;
            }
        }
        else if (change.Property == LanguageProperty)
        {
            ApplyLanguage();
        }
        else if (change.Property == IsReadOnlyProperty)
        {
            _editor.IsReadOnly = IsReadOnly;
        }
        else if (change.Property == ShowLineNumbersProperty)
        {
            _editor.ShowLineNumbers = ShowLineNumbers;
            StyleGutter();
        }
        else if (change.Property == HighlightVariablesProperty || change.Property == VariableHints.ContextProperty)
        {
            ApplyVariables();
        }
        else if (change.Property == IsSingleLineProperty)
        {
            ApplySingleLine();
        }
        else if (change.Property == PlaceholderTextProperty)
        {
            _placeholder.Text = PlaceholderText;
            UpdatePlaceholder();
        }
        else if (change.Property == Avalonia.Automation.AutomationProperties.NameProperty)
        {
            // Screen readers focus the inner text area, so it carries the name.
            Avalonia.Automation.AutomationProperties.SetName(_editor.TextArea, Avalonia.Automation.AutomationProperties.GetName(this));
        }
    }

    // A text-box-like editor: no gutter, scroll bars or find panel, vertically centred in its frame.
    private void ApplySingleLine()
    {
        if (!IsSingleLine)
        {
            return;
        }

        _editor.ShowLineNumbers = false;
        _editor.WordWrap = false;
        _editor.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden;
        _editor.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        _editor.Padding = new Thickness(10, 0);
        _editor.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        _search.Uninstall();

        RemoveLineBreaks();
    }

    private void RemoveLineBreaks()
    {
        var text = _editor.Document.Text;
        if (text.AsSpan().IndexOfAny('\r', '\n') < 0)
        {
            return;
        }

        var caret = _editor.CaretOffset;
        _editor.Document.Text = text.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ').Trim();
        _editor.CaretOffset = Math.Min(caret, _editor.Document.TextLength);
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsSingleLine)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && EnterCommand is { } command && command.CanExecute(null))
            {
                command.Execute(null);
            }
        }
        else if (e.Key == Key.Tab)
        {
            // Tab leaves the field like in a text box instead of inserting spaces.
            e.Handled = true;
            var direction = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? NavigationDirection.Previous : NavigationDirection.Next;
            TopLevel.GetTopLevel(this)?.FocusManager?.TryMoveFocus(direction, new FindNextElementOptions());
        }
    }

    private void UpdatePlaceholder() => _placeholder.IsVisible = !string.IsNullOrEmpty(PlaceholderText) && _editor.Document.TextLength == 0;

    // Line numbers sit on the tinted editor ground without AvaloniaEdit's separator line, with room before the text.
    private void StyleGutter()
    {
        foreach (var margin in _editor.TextArea.LeftMargins)
        {
            if (margin is Avalonia.Controls.Shapes.Line line)
            {
                line.IsVisible = false;
            }
            else if (margin is AvaloniaEdit.Editing.LineNumberMargin numbers)
            {
                numbers.Margin = new Thickness(10, 0, 14, 0);
            }
        }
    }

    private VariableContext? ActiveVariables => HighlightVariables ? VariableHints.GetContext(this) : null;

    private void ApplyVariables()
    {
        _variables.Context = ActiveVariables;
        _variables.Defined = (FindBrush("Tone.Success.Fg"), FindBrush("Tone.Success.Bg"));
        _variables.Undefined = (FindBrush("Tone.Danger.Fg"), FindBrush("Tone.Danger.Bg"));
        _editor.TextArea.TextView.Redraw();
    }

    private IBrush? FindBrush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) ? value as IBrush : null;

    private VariableHint? HintUnderPointer(Avalonia.Input.PointerEventArgs e)
    {
        var context = ActiveVariables;
        if (context is null || _editor.GetPositionFromPoint(e.GetPosition(_editor)) is not { } position)
        {
            return null;
        }

        var line = _editor.Document.GetLineByNumber(position.Line);
        return context.DescribeAt(_editor.Document.GetText(line), position.Column - 1);
    }

    private void ApplyLanguage()
    {
        var language = Language switch
        {
            "json" => "json",
            "xml" => "xml",
            _ => null,
        };
        _textMate.SetGrammar(language is null ? null : _registry.GetScopeByLanguageId(language));
    }

    private void ApplyTheme()
    {
        _textMate.SetTheme(_registry.LoadTheme(ActualThemeVariant == ThemeVariant.Dark ? ThemeName.DarkPlus : ThemeName.LightPlus));
        ApplyVariables();
    }
}
