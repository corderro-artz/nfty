using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;

namespace Nfty.App.Controls;

/// <summary>
/// The glyph in the quick-reference sheet's 20px gutter, chosen by the catalog's glyph token:
/// <c>icon:IconTypeSet</c> (a type mark), <c>mark:IconMarkExclude:accent</c> (a rule mark, stroked
/// in the named ink), <c>kind:D</c> (a layer-kind letter) or <c>dot</c> (the valid dot). Geometry is
/// the dictionary's own instance, so the legend and the app can be told apart from a look-alike
/// glyph by reference.
/// </summary>
public class HelpGlyph : Panel
{
    /// <summary>The catalog glyph token.</summary>
    public static readonly StyledProperty<string?> GlyphProperty =
        AvaloniaProperty.Register<HelpGlyph, string?>(nameof(Glyph));

    /// <summary>The catalog glyph token.</summary>
    public string? Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(Panel);

    static HelpGlyph() => GlyphProperty.Changed.AddClassHandler<HelpGlyph>((g, _) => g.Rebuild());

    private void Rebuild()
    {
        Children.Clear();
        var parts = (Glyph ?? "").Split(':');
        switch (parts[0])
        {
            case "icon":
            case "mark":
                var path = new Avalonia.Controls.Shapes.Path { Data = Application.Current!.FindResource(parts[1]) as Geometry };
                path.Classes.Add("ico");
                path.Classes.Add(parts[0] == "icon" ? "ti-sh" : "sm");
                var ink = parts.Length > 2 ? parts[2] switch { "accent" => "AccentTextBrush", "success" => "SuccessBrush", _ => null } : null;
                if (ink is not null) path.Bind(Shape.StrokeProperty, this.GetResourceObservable(ink));
                Children.Add(path);
                break;
            case "kind":
                var k = new TextBlock { Text = parts[1] };
                k.Classes.Add("kmark");
                k.Classes.Add(parts[1] switch { "D" => "kdyn", "S" => "kstat", _ => "kcust" });
                Children.Add(k);
                break;
            case "dot":
                var dot = new Ellipse { Width = 7, Height = 7 };
                dot.Bind(Shape.FillProperty, this.GetResourceObservable("SuccessBrush"));
                Children.Add(dot);
                break;
        }
    }
}
