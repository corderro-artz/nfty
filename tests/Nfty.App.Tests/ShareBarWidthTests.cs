using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Converters;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// A share bar has to fill the fraction of its track that it claims to.
/// </summary>
/// <remarks>
/// Found by looking at the app: a recipe holding 100% of mints drew a bar about four fifths full.
/// The fill was <c>share * 3.1</c> — exact for a 310px track, and that track is a star column, so it
/// is wider than 310 at any real pane size. Three of the app's four bars pin their track width and
/// were right; this one did not and was not.
/// </remarks>
public class ShareBarWidthTests
{
    // ---------------- the converter ----------------

    [Theory]
    [InlineData(100, 400, 400)]
    [InlineData(50, 400, 200)]
    [InlineData(0, 400, 0)]
    [InlineData(25, 310, 77.5)]
    public void The_fill_is_that_fraction_of_the_track(double share, double track, double expected) =>
        Assert.Equal(expected, (double)ShareWidthConverter.Instance
            .Convert(new object?[] { share, track }, typeof(double), null, null!));

    /// <summary>A mid-layout pass hands through unset values, and a bar cannot be measured before its
    /// track has been. Zero, not a crash and not a full bar.</summary>
    [Theory]
    [InlineData(null, 400d)]
    [InlineData(50d, null)]
    [InlineData(50d, 0d)]
    [InlineData(50d, double.NaN)]
    public void An_unmeasured_track_yields_nothing(object? share, object? track) =>
        Assert.Equal(0d, (double)ShareWidthConverter.Instance
            .Convert(new[] { share, track }, typeof(double), null, null!));

    [Fact]
    public void A_share_outside_the_range_is_clamped_rather_than_overflowing_its_track()
    {
        Assert.Equal(400d, (double)ShareWidthConverter.Instance
            .Convert(new object?[] { 140d, 400d }, typeof(double), null, null!));
        Assert.Equal(0d, (double)ShareWidthConverter.Instance
            .Convert(new object?[] { -20d, 400d }, typeof(double), null, null!));
    }

    // ---------------- the bar, as it actually renders ----------------

    /// <summary>The real claim, measured off a laid-out tree: a lone recipe holds 100% of mints, so
    /// its bar must fill its track — whatever width the pane gives that track.</summary>
    [AvaloniaTheory]
    [InlineData(1180)]
    [InlineData(1416)]
    public void A_recipe_holding_every_mint_fills_its_whole_track(double windowWidth)
    {
        using var book = ExplorerViewModelTests.TwoRecipeBook();
        // One recipe at weight 100 and nothing else: exactly the "100% of mints" case.
        var single = new Nfty.Core.Formats.LoadedCookBook
        {
            Manifest = book.Manifest with
            {
                RecipeWeights = new Dictionary<string, double> { [book.Recipes[0].Manifest.Id] = 100 },
            },
            Recipes = new[] { book.Recipes[0] },
        };

        var vm = new CookBookDetailViewModel(single, new FakeNotYetWired(), () => { });
        var view = new Views.CookBookDetailView { DataContext = vm };
        var window = new Window
        {
            RequestedThemeVariant = ThemeVariant.Dark,
            Content = view,
            Width = windowWidth,
            Height = 720,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var track = view.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("cbar"));
            var fill = track.GetVisualChildren().OfType<Border>().First();

            Assert.True(track.Bounds.Width > 0, "the track never got a width");
            Assert.Equal(track.Bounds.Width, fill.Bounds.Width, precision: 1);
        }
        finally { window.Close(); }
    }
}
