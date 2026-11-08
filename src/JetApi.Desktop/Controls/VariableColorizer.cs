using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using JetApi.Core.Variables;
using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Controls;

/// <summary>Colors <c>{{variable}}</c> placeholders: one tone for defined variables, another for undefined ones.</summary>
internal sealed class VariableColorizer : DocumentColorizingTransformer
{
    /// <summary>The variables; nothing is colored when <c>null</c>.</summary>
    public VariableContext? Context { get; set; }

    /// <summary>Brushes for defined variables (foreground, background).</summary>
    public (IBrush? Foreground, IBrush? Background) Defined { get; set; }

    /// <summary>Brushes for undefined variables (foreground, background).</summary>
    public (IBrush? Foreground, IBrush? Background) Undefined { get; set; }

    /// <inheritdoc />
    protected override void ColorizeLine(DocumentLine line)
    {
        var context = Context;
        if (context is null || line.Length < 5)
        {
            return;
        }

        var text = CurrentContext.Document.GetText(line);
        foreach (var placeholder in VariableResolver.FindPlaceholders(text))
        {
            var (foreground, background) = context.IsDefined(placeholder.Name) ? Defined : Undefined;
            ChangeLinePart(line.Offset + placeholder.Start, line.Offset + placeholder.End, element =>
            {
                if (foreground is not null)
                {
                    element.TextRunProperties.SetForegroundBrush(foreground);
                }

                element.BackgroundBrush = background;
            });
        }
    }
}
