using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Nfty.App.ViewModels;
using Xunit;
using IconPath = Avalonia.Controls.Shapes.Path;   // disambiguates the implicit System.IO.Path

namespace Nfty.App.Tests;

/// <summary>Structural guards for the quick-reference sheet (docs/design/mockups/help.html). The
/// frames are the real check on how it LOOKS; these pin the two things a rendered frame cannot
/// distinguish — which icon resource a glyph came from, and the column ratios behind a layout that
/// would still look plausible if they drifted.</summary>
public class HelpSheetTests
{
    private static Views.HelpView Sheet() =>
        StyledHost.Show(new Views.HelpView { DataContext = new HelpViewModel(new FakeDialogs()) });

    private static Geometry Icon(string key) => (Geometry)Application.Current!.FindResource(key)!;

    private static Geometry[] Glyphs(Visual view) =>
        view.GetVisualDescendants().OfType<IconPath>().Select(p => p.Data!).Where(d => d is not null).ToArray();

    private static bool Draws(Visual view, Geometry icon) =>
        Glyphs(view).Any(d => ReferenceEquals(d, icon));

    // help.html's "Rules & state" legend documents the three marks the app itself draws, so it must
    // read them from the IconMark* keys. IconMarkExclude and IconClose hold IDENTICAL path data
    // (as do IconMarkRequire and IconArrowRight's family), so no rendered frame — and no comparison
    // of geometry STRINGS — can tell a legend wired to the window-chrome glyph from a correct one.
    // {StaticResource} hands out the dictionary's own instance, so reference identity can: swap the
    // view to IconClose and this fails while the screenshot stays byte-identical.
    [AvaloniaFact]
    public void Rule_legend_draws_the_dedicated_mark_geometry_not_the_chrome_glyphs()
    {
        var view = Sheet();

        Assert.True(Draws(view, Icon("IconMarkExclude")), "exclude mark must come from IconMarkExclude");
        Assert.True(Draws(view, Icon("IconMarkRequire")), "require mark must come from IconMarkRequire");
        Assert.True(Draws(view, Icon("IconMarkFlag")), "rule flag must come from IconMarkFlag");

        Assert.False(Draws(view, Icon("IconClose")), "IconClose is window chrome, not a rule mark");
        Assert.False(Draws(view, Icon("IconArrowRight")), "IconArrowRight is a link arrow, not a rule mark");
    }

    [AvaloniaFact]
    public void Five_words_gutter_draws_the_five_type_marks()
    {
        var view = Sheet();

        Assert.True(Draws(view, Icon("IconTypeCookBook")));
        Assert.True(Draws(view, Icon("IconTypeRecipe")));
        Assert.True(Draws(view, Icon("IconTypeIngredient")));
        Assert.True(Draws(view, Icon("IconTypeVariant")));
        Assert.True(Draws(view, Icon("IconTypeSet")));
    }

    // A mistyped `d` can still parse into an empty geometry, which renders as nothing at all — the
    // failure mode that would leave the two NEW marks as blank 16px holes in the gutter. This is a
    // bounding-box check and nothing more: it catches a glyph that collapsed, not one that lost an
    // interior stroke (mutation-probed — truncating one of IconTypeSet's four squares leaves the
    // box unchanged and passes). Reading the rendered frame is what covers the rest.
    [AvaloniaTheory]
    [InlineData("IconTypeVariant")]
    [InlineData("IconTypeSet")]
    public void New_type_marks_parse_to_a_glyph_that_fills_its_24_unit_box(string key)
    {
        var bounds = Icon(key).Bounds;
        Assert.True(bounds.Width > 12, $"{key} spans only {bounds.Width} of the 24-unit box");
        Assert.True(bounds.Height > 12, $"{key} spans only {bounds.Height} of the 24-unit box");
    }

    [AvaloniaFact]
    public void Sheet_labels_all_five_reference_sections()
    {
        var labels = Sheet().GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Classes.Contains("slbl"))
            .Select(t => t.Text)
            .ToArray();

        Assert.Contains("THE FIVE WORDS", labels);
        Assert.Contains("LAYER KINDS", labels);
        Assert.Contains("RULES & STATE", labels);
        Assert.Contains("KEYS", labels);
        Assert.Contains("COLOUR", labels);
    }

    // help.html `.sh-b{grid-template-columns: 1.35fr 1fr .82fr}`. Star sizing is the only thing
    // holding the three reference columns at the mockup's widths; equal (or Auto) columns would
    // still render a plausible-looking sheet, so the ratios are pinned here.
    [AvaloniaFact]
    public void Body_columns_hold_the_mockup_star_ratios()
    {
        var body = Sheet().FindControl<Grid>("SheetBody")!;

        Assert.Equal(3, body.ColumnDefinitions.Count);
        Assert.Equal(new GridLength(1.35, GridUnitType.Star), body.ColumnDefinitions[0].Width);
        Assert.Equal(new GridLength(1, GridUnitType.Star), body.ColumnDefinitions[1].Width);
        Assert.Equal(new GridLength(0.82, GridUnitType.Star), body.ColumnDefinitions[2].Width);
    }

    // help.html `.sheet{width:780px}`. The sheet is a fixed-width reference card, not a panel that
    // stretches to whatever hosts it — the placeholder this replaced filled its host and left 80%
    // of the surface empty.
    [AvaloniaFact]
    public void Sheet_is_the_mockups_780px_card()
    {
        var sheet = Sheet().FindControl<Border>("Sheet")!;
        Assert.Equal(780, sheet.Width);
    }
}
