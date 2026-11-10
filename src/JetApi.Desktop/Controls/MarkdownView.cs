using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using JetApi.Desktop.Services;

namespace JetApi.Desktop.Controls;

/// <summary>
/// Shows parsed Markdown (<see cref="MarkdownText"/>): headings, paragraphs, nested bullets, code and links. Colors come
/// from theme tokens, so the theme switches live. Links open in the browser.
/// </summary>
public sealed class MarkdownView : StackPanel
{
    /// <summary>The blocks to show.</summary>
    public static readonly StyledProperty<IReadOnlyList<MarkdownBlock>?> BlocksProperty =
        AvaloniaProperty.Register<MarkdownView, IReadOnlyList<MarkdownBlock>?>(nameof(Blocks));

    /// <summary>Creates the view.</summary>
    public MarkdownView()
    {
        Spacing = 6;
    }

    /// <summary>The blocks to show.</summary>
    public IReadOnlyList<MarkdownBlock>? Blocks
    {
        get => GetValue(BlocksProperty);
        set => SetValue(BlocksProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        base.OnPropertyChanged(change);
        if (change.Property == BlocksProperty)
        {
            Build();
        }
    }

    private void Build()
    {
        Children.Clear();
        foreach (var block in Blocks ?? [])
        {
            Children.Add(block.Kind switch
            {
                MarkdownBlockKind.Heading => Heading(block),
                MarkdownBlockKind.Bullet => Bullet(block),
                MarkdownBlockKind.Code => Code(block),
                _ => Text(block.Inlines),
            });
        }
    }

    private TextBlock Heading(MarkdownBlock block)
    {
        var text = Text(block.Inlines);
        text.FontWeight = FontWeight.SemiBold;
        text.Margin = new Thickness(0, Children.Count == 0 ? 0 : 10, 0, 0);
        text.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable(block.Level <= 2 ? "App.FontSize.Heading" : "App.FontSize.Body"));
        return text;
    }

    private Grid Bullet(MarkdownBlock block)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(4 + (block.Level * 18), 0, 0, 0) };
        var dot = new Ellipse
        {
            Width = block.Level == 0 ? 5 : 4,
            Height = block.Level == 0 ? 5 : 4,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 10, 0),
        };
        dot.Bind(Shape.FillProperty, this.GetResourceObservable(block.Level == 0 ? "App.TextMuted" : "App.TextFaint"));
        var text = Text(block.Inlines);
        Grid.SetColumn(text, 1);
        grid.Children.Add(dot);
        grid.Children.Add(text);
        return grid;
    }

    private Border Code(MarkdownBlock block)
    {
        var text = new SelectableTextBlock { Text = MarkdownText.PlainText(block), TextWrapping = TextWrapping.NoWrap };
        text.Classes.Add("mono");
        var border = new Border { Padding = new Thickness(12, 8), Child = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = text } };
        border.Classes.Add("code");
        return border;
    }

    // One wrapped paragraph; links are underlined runs, opened by clicking (hit-tested on the text layout).
    private TextBlock Text(IReadOnlyList<MarkdownInline> inlines)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
        var links = new List<(int Start, int End, Uri Target)>();
        var position = 0;
        text.Inlines = [];
        foreach (var inline in inlines)
        {
            var run = new Run(inline.Text);
            switch (inline.Kind)
            {
                case MarkdownInlineKind.Code:
                    run.Bind(TextElement.FontFamilyProperty, this.GetResourceObservable("App.MonoFont"));
                    run.Bind(TextElement.BackgroundProperty, this.GetResourceObservable("App.Subtle"));
                    break;
                case MarkdownInlineKind.Strong:
                    run.FontWeight = FontWeight.SemiBold;
                    break;
                case MarkdownInlineKind.Emphasis:
                    run.FontStyle = FontStyle.Italic;
                    break;
                case MarkdownInlineKind.Link when inline.Link is not null:
                    run.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("App.Accent"));
                    run.TextDecorations = TextDecorations.Underline;
                    links.Add((position, position + inline.Text.Length, inline.Link));
                    break;
            }

            text.Inlines.Add(run);
            position += inline.Text.Length;
        }

        if (links.Count > 0)
        {
            text.PointerMoved += (_, e) => text.Cursor = LinkAt(text, links, e) is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
            text.PointerReleased += async (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left && LinkAt(text, links, e) is { } target && TopLevel.GetTopLevel(this) is { } top)
                {
                    e.Handled = true;
                    await top.Launcher.LaunchUriAsync(target).ConfigureAwait(true);
                }
            };
        }

        return text;
    }

    private static Uri? LinkAt(TextBlock text, List<(int Start, int End, Uri Target)> links, PointerEventArgs e)
    {
        var hit = text.TextLayout.HitTestPoint(e.GetPosition(text));
        if (!hit.IsInside)
        {
            return null;
        }

        var index = hit.TextPosition;
        foreach (var (start, end, target) in links)
        {
            if (index >= start && index < end)
            {
                return target;
            }
        }

        return null;
    }
}
