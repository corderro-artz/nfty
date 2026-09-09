using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.ViewModels;
using Nfty.Core.Generation;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The Set browser's rarity rail shows whole rows only: its fold lands on a row boundary rather than
/// across the middle of one.
/// </summary>
/// <remarks>
/// <para>The rail used to scroll as one block — number, DNA, table header and rows together — so the
/// fold landed wherever the identity block above happened to end. At the smallest window that was
/// across the middle of a rarity row, sliced by the pinned Save band below it, which reads as a
/// clipped panel rather than as a list with more in it.</para>
///
/// <para>Only the rows scroll now, and the host is sized to a whole number of them. The assertion is
/// on the ARITHMETIC — host height divides by row height — because that is the property; a rail that
/// happens to fit its rows at one window size proves nothing about any other.</para>
/// </remarks>
public class RarityRailSnapTests
{
    /// <summary>
    /// A set whose assets carry TWELVE traits, so the rail genuinely runs out of room.
    /// </summary>
    /// <remarks>
    /// The shared tiny fixture has one layer, so its rail holds two rows and fits at every size —
    /// which made the first version of this test pass with the snapping deleted. A test that cannot
    /// be made to fail by breaking the code it names is decoration.
    /// </remarks>
    /// <summary>The same twelve-trait Set, for other sweeps that need a real Set browser.</summary>
    /// <param name="dir">The temp directory it was written to; the caller deletes it.</param>
    /// <returns>The loaded Set.</returns>
    internal static LoadedSet CookedSetFor(out string dir) => CookedSet(out dir);

    private static LoadedSet CookedSet(out string dir)
    {
        LoadedIngredient Ing(string id) => new()
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
                [new Variant($"{id}a", "A", 1), new Variant($"{id}b", "B", 1)]),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                [$"{id}a"] = new Image<Rgba32>(8, 8),
                [$"{id}b"] = new Image<Rgba32>(8, 8),
            },
        };
        string[] layers = Enumerable.Range(0, 12).Select(i => $"l{i}").ToArray();
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", layers, []),
            Ingredients = layers.Select(Ing).ToArray(),
        };
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
                new Collection("VaporCats", "desc", "VC"),
                new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = [recipe],
        };

        dir = Directory.CreateTempSubdirectory().FullName;
        using var set = Generator.Generate(book, new GenerateOptions(2, "seed1"));
        SetWriter.Write(set, dir, pack: false);
        return SetReader.Read(dir);
    }

    /// <summary>Renders the browser with an asset selected, at a stated page size.</summary>
    private static (Window Window, Views.SetBrowserView View, SetBrowserViewModel Vm, string Dir)
        Render(double width, double height)
    {
        var loaded = CookedSet(out string dir);
        var vm = new SetBrowserViewModel(loaded);
        vm.SelectedItem = vm.Items[0];
        var view = new Views.SetBrowserView { DataContext = vm };
        var window = new Window { Content = view, Width = width, Height = height };
        window.Show();
        // Twice: the snap is applied from LayoutUpdated, which fires at the END of a pass, so the
        // height it writes is only arranged by the next one. The app gets that for free; a headless
        // test has to pump for it.
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm, dir);
    }

    [AvaloniaTheory]
    [InlineData(1118, 519)]     // the page at the smallest window the app opens
    [InlineData(1118, 640)]
    [InlineData(1600, 900)]
    public void The_rail_shows_whole_rarity_rows_at_every_size(double w, double h)
    {
        var (window, view, vm, dir) = Render(w, h);
        try
        {
            var host = view.GetVisualDescendants().OfType<ScrollViewer>()
                .First(s => s.Name == "RarityRows");
            var row = host.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("data-row"));

            Assert.True(row.Bounds.Height > 0);
            Assert.True(host.Bounds.Height >= row.Bounds.Height,
                $"the rail is {host.Bounds.Height:F1}px and a row is {row.Bounds.Height:F1}px");

            double rows = host.Bounds.Height / row.Bounds.Height;
            Assert.True(Math.Abs(rows - Math.Round(rows)) < 0.02,
                $"the rail is {host.Bounds.Height:F1}px, which is {rows:F2} rows of "
                + $"{row.Bounds.Height:F1}px - the fold cuts one in half");
        }
        finally
        {
            window.Close();
            vm.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaTheory]
    [InlineData(1118, 519)]
    [InlineData(1118, 640)]
    [InlineData(1600, 900)]
    public void The_rail_fits_the_row_it_sits_in(double w, double h)
    {
        // The snap test above asserts the host is a WHOLE number of rows, which stays true whatever
        // the arithmetic overstates by - so it was structurally blind to the room being computed
        // 12px too large. The room was reconstructed as "grid height less each sibling's Bounds",
        // and Bounds excludes margin, so the identity block's 12px bottom margin went uncounted and
        // the host was sized past its own slot at every window height. This is the other half:
        // whatever height is written, it has to fit the star row it was written for.
        var (window, view, vm, dir) = Render(w, h);
        try
        {
            var host = view.GetVisualDescendants().OfType<ScrollViewer>()
                .First(s => s.Name == "RarityRows");
            var grid = Assert.IsType<Grid>(host.Parent);
            double slot = grid.RowDefinitions[1].ActualHeight
                - host.Margin.Top - host.Margin.Bottom;

            Assert.True(host.Bounds.Height <= slot + 0.5,
                $"the rail was sized {host.Bounds.Height:F1}px into a {slot:F1}px slot");
        }
        finally
        {
            window.Close();
            vm.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public void The_identity_block_and_the_table_header_do_not_scroll_away()
    {
        // They are what a rarity row is read AGAINST, which is the other half of the argument for
        // splitting the rail: a percentage under no header is a number with no name.
        var (window, view, vm, dir) = Render(1118, 519);
        try
        {
            var host = view.GetVisualDescendants().OfType<ScrollViewer>()
                .First(s => s.Name == "RarityRows");

            foreach (string text in new[] { "DNA", "RARITY", "TRAIT" })
            {
                var block = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == text);
                Assert.DoesNotContain(host, block.GetVisualAncestors());
            }
        }
        finally
        {
            window.Close();
            vm.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }
}
