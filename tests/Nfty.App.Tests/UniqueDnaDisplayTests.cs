using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The UNIQUE DNA tile is about 90px wide and the figure it names is unbounded. It rounds; the
/// tooltip does not.
/// </summary>
/// <remarks>
/// <para><b>The tile is allowed to round only because the exact figure is one hover away.</b> This
/// is the number an author tunes quantize steps against — a product they cannot read is a product
/// they cannot tune — so the two halves have to be tested together. A test of the compact form alone
/// would pass on a card that had quietly stopped being able to show the real figure at all.</para>
///
/// <para>Both are asserted off a laid-out frame rather than off the ViewModel, because the ViewModel
/// was already right the whole time the card had no tooltip on it.</para>
/// </remarks>
public class UniqueDnaDisplayTests
{
    /// <summary>
    /// A book whose space is past a billion: four custom layers of 200 variants each, which is
    /// 1,600,000,000 and needs no rules walk and no color buckets to get there.
    /// </summary>
    private static LoadedCookBook HugeBook()
    {
        LoadedIngredient Ing(string id) => new()
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
                Enumerable.Range(0, 200).Select(i => new Variant($"v{i}", $"V{i}", 1)).ToArray()),
            VariantImages = Enumerable.Range(0, 200)
                .ToDictionary(i => $"v{i}", _ => new Image<Rgba32>(8, 8)),
        };
        string[] layers = ["a", "b", "c", "d"];
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Huge", new Dimensions(8, 8),
                new Collection("Huge", "", "HG"),
                new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes =
            [
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("cat", "cat", layers, []),
                    Ingredients = layers.Select(Ing).ToArray(),
                },
            ],
        };
    }

    [Fact]
    public void A_figure_past_a_billion_rounds_on_the_tile_and_stays_whole_on_the_tooltip()
    {
        using var book = HugeBook();
        var vm = new CookBookDetailViewModel(book, () => { });

        Assert.Equal("1.6 billion", vm.UniqueDnaText);
        Assert.Equal("1,600,000,000 unique DNA", vm.UniqueDnaTip);

        // The cookbar is a bar, not a tile: it exists to compare an intended supply against the
        // space, and a comparison between a rounded number and an exact one is not one.
        Assert.Contains("1,600,000,000", vm.CookBarText);
    }

    [Fact]
    public void A_figure_that_fits_is_shown_in_full_on_both()
    {
        // Degrade only when you must. Essentially every real book lands here, so essentially every
        // book shows its true figure on the card — the rounded form is the exception, not the rule.
        using var book = ExplorerViewModelTests.TwoRecipeBook();
        var vm = new CookBookDetailViewModel(book, () => { });

        Assert.Equal("2", vm.UniqueDnaText);
        Assert.Equal("2 unique DNA", vm.UniqueDnaTip);
    }

    [AvaloniaFact]
    public void The_tile_actually_carries_the_tooltip()
    {
        // A tooltip the ViewModel exposes and the markup never binds is the reason the tile is
        // allowed to round, silently absent. Read it off the rendered control.
        using var book = HugeBook();
        var view = new Views.CookBookDetailView
        {
            DataContext = new CookBookDetailViewModel(book, () => { }, () => { }),
        };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var label = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "UNIQUE DNA");
        var tile = label.GetVisualAncestors().OfType<StackPanel>().First();

        Assert.Equal("1,600,000,000 unique DNA", ToolTip.GetTip(tile));

        // And the figure beside it is the rounded one, so the pair is doing what it claims.
        var figure = tile.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Classes.Contains("mv"));
        Assert.Equal("1.6 billion", figure.Text);
    }

    [AvaloniaFact]
    public void The_widest_figure_the_compact_form_can_produce_still_fits_the_tile()
    {
        // Width is a budget and an overrun is silent — a TextBlock is arranged to its parent and
        // clips, reporting the width it was asked for either way. So measure the INK, against the
        // tile's own content box, for the worst string this formatter can emit: long.MaxValue is
        // "9.22 quintillion", sixteen characters and the ceiling of the type.
        using var book = HugeBook();
        var view = new Views.CookBookDetailView
        {
            DataContext = new CookBookDetailViewModel(book, () => { }, () => { }),
        };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var figure = view.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Classes.Contains("mv") && t.Text == "1.6 billion");
        var tile = figure.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("metric"));

        var widest = new FormattedText(SpaceText.Compact(long.MaxValue),
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(figure.FontFamily, figure.FontStyle, figure.FontWeight),
            figure.FontSize, Brushes.Black);

        double room = tile.Bounds.Width - tile.Padding.Left - tile.Padding.Right
            - tile.BorderThickness.Left - tile.BorderThickness.Right;

        Assert.True(widest.Width <= room,
            $"\"{SpaceText.Compact(long.MaxValue)}\" measures {widest.Width:F1}px in a tile with "
            + $"{room:F1}px of room");
    }

    [Fact]
    public void Each_recipe_row_carries_its_own_exact_figure()
    {
        // The per-recipe rows are narrower than the tile and carry the same kind of number. Their
        // tooltip also has to keep the derivation the arrow between the chips and the figure leaves
        // implicit — one control has one tooltip, so it holds both facts or loses one.
        using var book = HugeBook();
        var vm = new CookBookDetailViewModel(book, () => { });
        var row = Assert.Single(vm.Recipes);

        Assert.Equal("1.6 billion", row.DnaSpaceText);
        Assert.Contains("1,600,000,000 unique DNA", row.DnaSpaceTip);
        Assert.Contains("quantized colors", row.DnaSpaceTip);
    }
}
