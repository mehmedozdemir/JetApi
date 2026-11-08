using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Search;
using AvaloniaEdit.TextMate;
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

    private readonly TextEditor _editor;
    private readonly RegistryOptions _registry;
    private readonly TextMate.Installation _textMate;
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
            if (!_updating)
            {
                _updating = true;
                SetCurrentValue(TextProperty, _editor.Document.Text);
                _updating = false;
            }
        };

        // Ctrl+F opens find (and replace in editable editors).
        SearchPanel.Install(_editor);

        Content = _editor;
        ApplyLanguage();
        ActualThemeVariantChanged += (_, _) => ApplyTheme();
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

    /// <summary>Moves keyboard focus into the editor.</summary>
    public void FocusEditor() => _editor.TextArea.Focus();

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
        }
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

    private void ApplyTheme() =>
        _textMate.SetTheme(_registry.LoadTheme(ActualThemeVariant == ThemeVariant.Dark ? ThemeName.DarkPlus : ThemeName.LightPlus));
}
