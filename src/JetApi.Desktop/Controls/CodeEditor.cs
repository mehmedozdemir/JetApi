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

    /// <summary>
    /// Runs with the whole text after a paste (or drop) when it can execute, e.g. to import a pasted cURL command.
    /// The command gets the text before line breaks are removed in single-line mode.
    /// </summary>
    public static readonly StyledProperty<ICommand?> PastedTextCommandProperty = AvaloniaProperty.Register<CodeEditor, ICommand?>(nameof(PastedTextCommand));

    /// <summary>
    /// Offered on a JSON value in a read-only editor (a response): "Add a check for this value". Gets the
    /// <see cref="JetApi.Testing.JsonPath.JsonPathHit"/> under the caret or selection.
    /// </summary>
    public static readonly StyledProperty<ICommand?> AddCheckCommandProperty = AvaloniaProperty.Register<CodeEditor, ICommand?>(nameof(AddCheckCommand));

    /// <summary>Offered like <see cref="AddCheckCommandProperty"/>: "Add a rule to save it to a variable".</summary>
    public static readonly StyledProperty<ICommand?> AddExtractionCommandProperty = AvaloniaProperty.Register<CodeEditor, ICommand?>(nameof(AddExtractionCommand));

    /// <summary>Runs on Enter in single-line mode (e.g. send the request).</summary>
    public static readonly StyledProperty<ICommand?> EnterCommandProperty = AvaloniaProperty.Register<CodeEditor, ICommand?>(nameof(EnterCommand));

    // The palette's TextMate theme (App.SyntaxTheme) and a variable color, bound to resources so changes reach ApplyTheme.
    private static readonly StyledProperty<object?> SyntaxThemeProperty = AvaloniaProperty.Register<CodeEditor, object?>("SyntaxTheme");
    private static readonly StyledProperty<object?> VariableBrushProperty = AvaloniaProperty.Register<CodeEditor, object?>("VariableBrush");

    private readonly TextEditor _editor;
    private ThemeName? _syntaxTheme;
    private int _lastInsertionLength;
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
            Padding = new Thickness(4, 6),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        _editor.Options.EnableHyperlinks = false;
        _editor.Options.EnableEmailHyperlinks = false;
        _editor.Options.ConvertTabsToSpaces = true;
        _editor.Options.IndentationSize = 2;
        _editor[!TextEditor.FontFamilyProperty] = this.GetResourceObservable("App.MonoFont").ToBinding();
        _editor[!TextEditor.FontSizeProperty] = this.GetResourceObservable("App.EditorFontSize").ToBinding();
        _editor[!TextEditor.ForegroundProperty] = this.GetResourceObservable("App.Text").ToBinding();
        _editor[!TextEditor.LineNumbersForegroundProperty] = this.GetResourceObservable("App.TextFaint").ToBinding();
        _editor.Background = Brushes.Transparent;

        _registry = new RegistryOptions(ThemeName.LightPlus);
        _textMate = _editor.InstallTextMate(_registry);
        _editor.Document.Changed += (_, e) => _lastInsertionLength = e.InsertionLength;
        _editor.Document.TextChanged += (_, _) =>
        {
            UpdatePlaceholder();
            if (_lastInsertionLength > 1 && !_updating && PastedTextCommand is { } pasted && pasted.CanExecute(_editor.Document.Text))
            {
                // A paste rather than typing: let the owner take the whole text (applied after the change completes).
                var text = _editor.Document.Text;
                _lastInsertionLength = 0;
                Dispatcher.UIThread.Post(() => pasted.Execute(text));
            }

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
                Completion?.OnTextChanged();
            }
        };
        _editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (Completion is { IsOpen: true } completion)
            {
                completion.OnTextChanged();
            }
        };
        _editor.TextArea.LostFocus += (_, _) => Completion?.OnTextChanged();
        _editor.TextArea.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        _editor.TextArea.AddHandler(PointerPressedEvent, OnTextAreaPointerPressed, RoutingStrategies.Tunnel);

        // The frame around the editor shows focus; the default focus rectangle inside it is noise.
        _editor.TextArea.FocusAdorner = null;

        // Ctrl+F opens find (and replace in editable editors).
        _search = SearchPanel.Install(_editor);

        // Added after TextMate so variable colors win over syntax colors.
        _editor.TextArea.TextView.LineTransformers.Add(_variables);
        _editor.TextArea.TextView.PointerMoved += (_, e) => VariablePopup.For(_editor).Hover(ActiveVariables, HintUnderPointer(e));
        _editor.TextArea.TextView.PointerExited += (_, _) => VariablePopup.Find(_editor)?.Leave();
        _editor.ContextFlyout = CreateMenu();

        _placeholder = new TextBlock
        {
            IsHitTestVisible = false,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            IsVisible = false,
        };
        _placeholder[!TextBlock.FontFamilyProperty] = this.GetResourceObservable("App.MonoFont").ToBinding();
        _placeholder[!TextBlock.FontSizeProperty] = this.GetResourceObservable("App.EditorFontSize").ToBinding();
        _placeholder[!TextBlock.ForegroundProperty] = this.GetResourceObservable("App.TextFaint").ToBinding();

        Content = new Panel { Children = { _editor, _placeholder } };
        StyleGutter();
        ApplyLanguage();
        ActualThemeVariantChanged += (_, _) => ApplyTheme();

        // A palette change keeps the theme variant, so the syntax theme and variable colors follow the resources too.
        this[!SyntaxThemeProperty] = this.GetResourceObservable("App.SyntaxTheme").ToBinding();
        this[!VariableBrushProperty] = this.GetResourceObservable("Tone.Success.Fg").ToBinding();
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
    public ICommand? PastedTextCommand
    {
        get => GetValue(PastedTextCommandProperty);
        set => SetValue(PastedTextCommandProperty, value);
    }

    /// <summary>One line only.</summary>
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

    /// <summary>Adds a check for the JSON value under the caret.</summary>
    public ICommand? AddCheckCommand
    {
        get => GetValue(AddCheckCommandProperty);
        set => SetValue(AddCheckCommandProperty, value);
    }

    /// <summary>Adds an extraction for the JSON value under the caret.</summary>
    public ICommand? AddExtractionCommand
    {
        get => GetValue(AddExtractionCommandProperty);
        set => SetValue(AddExtractionCommandProperty, value);
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

            // Responses and other read-only text wrap when the setting asks for it.
            if (IsReadOnly && !IsSingleLine)
            {
                _editor[!TextEditor.WordWrapProperty] = this.GetResourceObservable("App.WrapResponses").ToBinding();
            }
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
        else if (change.Property == SyntaxThemeProperty || change.Property == VariableBrushProperty)
        {
            ApplyTheme();
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
        if (Completion?.HandleKey(e.Key) == true)
        {
            e.Handled = true;
            return;
        }

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

    // Cut, copy, paste and select all, plus "Set as variable…" in request editors.
    private MenuFlyout CreateMenu()
    {
        var cut = new MenuItem { Header = Localization.Loc.Get("Menu_Cut"), Icon = Icon.ForMenu("Icon.Scissors"), InputGesture = new KeyGesture(Key.X, KeyModifiers.Control) };
        cut.Click += (_, _) => _editor.Cut();
        var copy = new MenuItem { Header = Localization.Loc.Get("Btn_Copy"), Icon = Icon.ForMenu("Icon.Copy"), InputGesture = new KeyGesture(Key.C, KeyModifiers.Control) };
        copy.Click += (_, _) => _editor.Copy();
        var paste = new MenuItem { Header = Localization.Loc.Get("Menu_Paste"), Icon = Icon.ForMenu("Icon.Clipboard"), InputGesture = new KeyGesture(Key.V, KeyModifiers.Control) };
        paste.Click += (_, _) => _editor.Paste();
        var selectAll = new MenuItem { Header = Localization.Loc.Get("Menu_SelectAll"), InputGesture = new KeyGesture(Key.A, KeyModifiers.Control) };
        selectAll.Click += (_, _) => _editor.SelectAll();
        var separator = new Separator();
        var setAsVariable = VariableHints.CreateSetAsVariableItem(SetSelectionAsVariable);
        var assign = new MenuItem { Header = Localization.Loc.Get("Var_AssignToVariable"), Icon = Icon.ForMenu("Icon.Variable") };
        assign.Click += (_, _) => AssignSelectionToVariable();
        JetApi.Testing.JsonPath.JsonPathHit? hit = null;
        var testSeparator = new Separator();
        var addCheck = new MenuItem { Header = Localization.Loc.Get("Tests_AddCheckHere"), Icon = Icon.ForMenu("Icon.Check") };
        addCheck.Click += (_, _) => AddCheckCommand?.Execute(hit);
        var addExtraction = new MenuItem { Header = Localization.Loc.Get("Tests_AddExtractionHere"), Icon = Icon.ForMenu("Icon.Variable") };
        addExtraction.Click += (_, _) => AddExtractionCommand?.Execute(hit);

        var menu = new MenuFlyout { Items = { cut, copy, paste, selectAll, separator, setAsVariable, assign, testSeparator, addCheck, addExtraction } };
        menu.Opening += (_, _) =>
        {
            var hasSelection = !_editor.TextArea.Selection.IsEmpty;
            cut.IsEnabled = hasSelection && !IsReadOnly;
            copy.IsEnabled = hasSelection;
            paste.IsVisible = cut.IsVisible = !IsReadOnly;
            var available = !IsReadOnly && ActiveVariables?.Store is not null;
            setAsVariable.IsVisible = available;
            setAsVariable.IsEnabled = available && VariableHints.CanSetAsVariable(ActiveVariables, SelectedText());

            // Responses and other read-only text: save the selected value into a variable.
            var canAssign = IsReadOnly && VariableHints.GetContext(this)?.Store is not null;
            assign.IsVisible = canAssign;
            assign.IsEnabled = canAssign && VariableHints.CanSetAsVariable(VariableHints.GetContext(this), SelectedText());
            separator.IsVisible = available || canAssign;

            // Responses: a check or extraction for the JSON value under the caret (its path is shown in the menu).
            hit = IsReadOnly && (AddCheckCommand ?? AddExtractionCommand) is not null ? JsonValueAtCaret() : null;
            testSeparator.IsVisible = addCheck.IsVisible = hit is not null && AddCheckCommand is not null;
            addExtraction.IsVisible = hit is not null && AddExtractionCommand is not null;
            testSeparator.IsVisible |= addExtraction.IsVisible;
            if (hit is not null)
            {
                addCheck.Header = Localization.Loc.Format("Tests_AddCheckAt", hit.Path);
                addExtraction.Header = Localization.Loc.Format("Tests_AddExtractionAt", hit.Path);
            }
        };
        return menu;
    }

    // The JSON value at the start of the selection, or at the caret.
    private JetApi.Testing.JsonPath.JsonPathHit? JsonValueAtCaret()
    {
        var offset = _editor.TextArea.Selection.IsEmpty ? _editor.CaretOffset : _editor.TextArea.Selection.SurroundingSegment.Offset;
        return JetApi.Testing.JsonPath.JsonPathLocator.Find(_editor.Document.Text, offset);
    }

    // A right click moves the caret there (unless it is inside the selection), so the menu acts on what was clicked.
    private void OnTextAreaPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_editor.TextArea).Properties.IsRightButtonPressed || _editor.GetPositionFromPoint(e.GetPosition(_editor)) is not { } position)
        {
            return;
        }

        var offset = _editor.Document.GetOffset(position.Location);
        var selection = _editor.TextArea.Selection;
        if (selection.IsEmpty || !selection.Contains(offset))
        {
            _editor.TextArea.ClearSelection();
            _editor.CaretOffset = offset;
        }
    }

    /// <summary>Puts the caret at <paramref name="offset"/> (tests).</summary>
    internal void MoveCaret(int offset) => _editor.CaretOffset = offset;

    /// <summary>The editor's context menu (tests).</summary>
    internal MenuFlyout? Menu => _editor.ContextFlyout as MenuFlyout;

    private string SelectedText() => _editor.TextArea.Selection.IsEmpty ? string.Empty : _editor.TextArea.Selection.GetText();

    /// <summary>Opens the "Set as variable" card for the selected text.</summary>
    internal void SetSelectionAsVariable()
    {
        var context = ActiveVariables;
        var selected = SelectedText();
        if (!VariableHints.CanSetAsVariable(context, selected))
        {
            return;
        }

        var segment = _editor.TextArea.Selection.SurroundingSegment;
        var (offset, length) = (segment.Offset, segment.Length);
        VariablePopup.For(_editor).ShowForSelection(
            context!,
            selected,
            placeholder =>
            {
                // Only when the selected text is still where it was.
                if (offset + length <= _editor.Document.TextLength && _editor.Document.GetText(offset, length) == selected)
                {
                    _editor.Document.Replace(offset, length, placeholder);
                    _editor.CaretOffset = offset + placeholder.Length;
                }
            },
            () => _editor.TextArea.Focus(),
            SelectionBottomLeft(offset));
    }

    /// <summary>Opens the card that saves the selected text (e.g. a response value) into a variable.</summary>
    internal void AssignSelectionToVariable()
    {
        var context = VariableHints.GetContext(this);
        var selected = SelectedText();
        if (!VariableHints.CanSetAsVariable(context, selected))
        {
            return;
        }

        var offset = _editor.TextArea.Selection.SurroundingSegment.Offset;
        VariablePopup.For(_editor).ShowForAssignment(context!, selected, () => _editor.TextArea.Focus(), SelectionBottomLeft(offset));
    }

    // The bottom-left corner of the line part at <paramref name="offset"/>, in the coordinates of the inner editor.
    private Point SelectionBottomLeft(int offset)
    {
        var textView = _editor.TextArea.TextView;
        var location = _editor.Document.GetLocation(offset);
        var point = textView.GetVisualPosition(new AvaloniaEdit.TextViewPosition(location), AvaloniaEdit.Rendering.VisualYPosition.LineBottom) - textView.ScrollOffset;
        return textView.TranslatePoint(point, _editor) ?? default;
    }

    // "{{" completion in request editors that can be typed in.
    private VariableCompletion? Completion =>
        HighlightVariables && !IsReadOnly ? VariableCompletion.Attach(new EditorCompletionHost(this)) : null;

    /// <summary>The "{{" completion list of this editor (tests).</summary>
    internal VariableCompletion? CompletionList => VariableCompletion.Find(_editor);

    private sealed class EditorCompletionHost(CodeEditor owner) : ICompletionHost
    {
        public Control Anchor => owner._editor;

        public string Text => owner._editor.Document.Text;

        public int Caret => owner._editor.CaretOffset;

        public bool IsTyping => owner._editor.TextArea.IsKeyboardFocusWithin;

        public VariableContext? Context => owner.ActiveVariables;

        public Point CaretBottom() => owner.SelectionBottomLeft(owner._editor.CaretOffset);

        public void Apply(CompletionEdit edit)
        {
            owner._editor.Document.Replace(edit.Start, edit.Length, edit.Insert);
            owner._editor.CaretOffset = Math.Min(edit.Caret, owner._editor.Document.TextLength);
            owner._editor.TextArea.Focus();
        }
    }

    /// <summary>The variable card of this editor (tests).</summary>
    internal VariablePopup? Popup => VariablePopup.Find(_editor);

    /// <summary>Selects <paramref name="length"/> characters from <paramref name="offset"/> (tests).</summary>
    internal void Select(int offset, int length) => _editor.Select(offset, length);

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
            "json" or "xml" or "shellscript" or "bat" or "powershell" or "csharp" or "javascript" or "python" => Language,
            _ => null,
        };
        _textMate.SetGrammar(language is null ? null : _registry.GetScopeByLanguageId(language));
    }

    private void ApplyTheme()
    {
        var theme = GetValue(SyntaxThemeProperty) is string name && Enum.TryParse<ThemeName>(name, out var parsed)
            ? parsed
            : ActualThemeVariant == ThemeVariant.Dark ? ThemeName.DarkPlus : ThemeName.LightPlus;
        if (theme != _syntaxTheme)
        {
            _syntaxTheme = theme;
            _textMate.SetTheme(_registry.LoadTheme(theme));
        }

        ApplyVariables();
    }
}
