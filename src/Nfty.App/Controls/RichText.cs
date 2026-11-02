using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Styling;

namespace Nfty.App.Controls;

/// <summary>
/// A <see cref="TextBlock"/> that reads a tiny markup: <c>*word*</c> is emphasis (full-strength ink),
/// <c>^word^</c> accent ink. The help catalog is one list of plain strings that both the pages and the
/// search read; this is the only place the two marks mean anything, so a sentence keeps its few bold
/// words without the catalog carrying XAML. The runs take their ink from the theme dictionary, so a
/// theme switch repaints them.
/// </summary>
public class RichText : TextBlock
{
    /// <summary>The marked-up text.</summary>
    public static readonly StyledProperty<string?> MarkupProperty =
        AvaloniaProperty.Register<RichText, string?>(nameof(Markup));

    /// <summary>The marked-up text.</summary>
    public string? Markup
    {
        get => GetValue(MarkupProperty);
        set => SetValue(MarkupProperty, value);
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(TextBlock);

    static RichText() => MarkupProperty.Changed.AddClassHandler<RichText>((t, _) => t.Rebuild());

    private void Rebuild()
    {
        Inlines ??= new InlineCollection();
        Inlines.Clear();
        var s = Markup ?? "";
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c is '*' or '^')
            {
                var end = s.IndexOf(c, i + 1);
                if (end > i)
                {
                    var run = new Run(s.Substring(i + 1, end - i - 1));
                    var key = c == '*' ? "FgBrush" : "AccentTextBrush";
                    run.Bind(TextElement.ForegroundProperty, this.GetResourceObservable(key));
                    if (c == '^') run.FontWeight = FontWeight.SemiBold;
                    Inlines.Add(run);
                    i = end + 1;
                    continue;
                }
            }
            var next = s.IndexOfAny(['*', '^'], i + 1);
            if (next < 0) next = s.Length;
            Inlines.Add(new Run(s.Substring(i, next - i)));
            i = next;
        }
    }
}
