using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Click-to-sort: the shared rule, and the two ways a sortable header can wreck the table it sits
/// on. Both of those were found on a rendered frame after the markup was written and the ViewModel
/// tests were green, which is why they are measured here rather than described.
/// </summary>
public class TableSortTests
{
    private sealed record Row(string Name, double Weight);

    private static readonly IReadOnlyList<Row> Rows = new[]
    {
        new Row("beta", 3), new Row("alpha", 1), new Row("gamma", 2),
    };

    private static object? Key(Row r, string col) => col switch
    {
        "Weight" => r.Weight,
        "Name" => r.Name,
        _ => null,
    };

    [Fact]
    public void The_first_click_on_a_column_is_ascending_and_clicking_it_again_reverses()
    {
        var sort = new TableSort("Name");
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, sort.Order(Rows, Key).Select(r => r.Name));

        // ONE RULE, NO EXCEPTIONS — a numeric column is not special-cased into descending-first.
        // The variant table used to jump straight to weight-descending, so the direction depended on
        // which column you clicked and neither column could be reversed at all.
        sort.By("Weight");
        Assert.False(sort.Descending);
        Assert.Equal(new[] { "alpha", "gamma", "beta" }, sort.Order(Rows, Key).Select(r => r.Name));

        sort.By("Weight");
        Assert.True(sort.Descending);
        Assert.Equal(new[] { "beta", "gamma", "alpha" }, sort.Order(Rows, Key).Select(r => r.Name));

        // A different column starts over ascending rather than inheriting the reversal.
        sort.By("Name");
        Assert.False(sort.Descending);
    }

    [Fact]
    public void An_unknown_column_leaves_the_natural_order_alone()
    {
        // A column key is a string from markup. A typo there must degrade to "unsorted", not take
        // the pane down — and it is also how the rules panel expresses "as authored", which is not
        // a column any header offers.
        var sort = new TableSort("Authored");
        Assert.Equal(new[] { "beta", "alpha", "gamma" }, sort.Order(Rows, Key).Select(r => r.Name));
    }

    [Fact]
    public void Equal_keys_keep_the_order_they_came_in()
    {
        var rows = new[] { new Row("beta", 1), new Row("alpha", 1), new Row("gamma", 1) };
        var sort = new TableSort("Weight");
        // Stability is what keeps a sort on a coarse column readable underneath.
        Assert.Equal(new[] { "beta", "alpha", "gamma" }, sort.Order(rows, Key).Select(r => r.Name));
    }

    [Fact]
    public void Strings_sort_ordinally_so_the_same_table_lists_the_same_way_on_any_machine()
    {
        // Ordinal puts every uppercase letter before every lowercase one; a culture-aware compare
        // interleaves them. These tables are screenshotted into the manual, so the difference is
        // visible in a published artifact.
        var rows = new[] { new Row("apple", 1), new Row("Banana", 1), new Row("Apple", 1) };
        var sort = new TableSort("Name");
        Assert.Equal(new[] { "Apple", "Banana", "apple" }, sort.Order(rows, Key).Select(r => r.Name));
    }

    // ------------------------------------------------------------- measured on a frame

    private static (Window window, IngredientDetailViewModel vm, Views.IngredientDetailView view) Render()
    {
        LoadedIngredient Ing() => new()
        {
            Manifest = new IngredientManifest("aura", "Aura", LayerKind.Custom, null,
                new[] { new Variant("a", "Apple", 1), new Variant("z", "Zephyr", 3) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["a"] = new Image<Rgba32>(4, 4), ["z"] = new Image<Rgba32>(4, 4),
            },
        };
        var ing = Ing();
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
        var vm = new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(), () => { }, () => false);
        var view = new Views.IngredientDetailView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, view);
    }

    private static double RightEdge(TextBlock t, Visual root) =>
        t.TranslatePoint(new Avalonia.Point(t.Bounds.Width, 0), root)!.Value.X;

    /// <summary>
    /// A numeric header still sits over the values it names. The arrow slot LEADS on a right-aligned
    /// header for exactly this reason: trailing, it took the right edge and pushed the label left,
    /// so every header stopped lining up with its own column — which is the defect the table's fixed
    /// pixel columns were introduced to fix in the first place.
    /// </summary>
    [AvaloniaFact]
    public void Every_numeric_header_still_sits_over_the_values_it_names()
    {
        var (window, vm, view) = Render();
        try
        {
            var heads = view.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.Classes.Contains("data-h") && t.Classes.Contains("num")).ToList();
            var cells = view.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.Classes.Contains("data-row") && t.Classes.Contains("num")).ToList();

            Assert.Equal(3, heads.Count);                 // WEIGHT, IN RECIPE, OVERALL
            Assert.True(cells.Count >= 3, $"expected a row of values; found {cells.Count}");

            for (int i = 0; i < 3; i++)
                Assert.Equal(RightEdge(heads[i], view), RightEdge(cells[i], view), 1);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// Sorting a column does not move its header. The arrows are reserved — both of them, in ONE
    /// 11px slot — because laid out side by side they cost 26px, which the 56px WEIGHT column did
    /// not have: they were arranged past the column's edge and silently not drawn.
    /// </summary>
    [AvaloniaFact]
    public void Clicking_a_header_does_not_move_it()
    {
        var (window, vm, view) = Render();
        try
        {
            var head = view.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.Text == "WEIGHT");
            var before = RightEdge(head, view);

            vm.Sort.ByCommand.Execute("Weight");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before, RightEdge(head, view), 1);

            vm.Sort.ByCommand.Execute("Weight");   // and again, for the descending arrow
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before, RightEdge(head, view), 1);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    [AvaloniaFact]
    public void Exactly_one_column_carries_the_indicator()
    {
        var (window, vm, view) = Render();
        try
        {
            vm.Sort.ByCommand.Execute("Overall");
            Dispatcher.UIThread.RunJobs();

            var on = view.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Classes.Contains("sorth") && b.Classes.Contains("on")).ToList();
            Assert.Single(on);
            Assert.Equal("Overall", on[0].CommandParameter);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// The Set browser's rarity table lines its headers up with its values. Its VALUE column was an
    /// <c>Auto</c> in BOTH the header Grid and the row Grid — two Grids, sized independently, so the
    /// header measured "VALUE" and the rows measured "Cat" and column 1 began at a different x in
    /// each. The label stood left of the values it named the whole time; making the header sortable
    /// only widened the header enough to make it visible.
    /// </summary>
    [AvaloniaFact]
    public void The_rarity_tables_headers_line_up_with_its_values()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        using var set = Nfty.Core.Generation.Generator.Generate(
            CoreTestBook.Tiny(), new Nfty.Core.Generation.GenerateOptions(2, "seed1"));
        Nfty.Core.Output.SetWriter.Write(set, dir, pack: false);
        var loaded = Nfty.Core.Output.SetReader.Read(dir);
        var vm = new SetBrowserViewModel(loaded);
        var view = new Views.SetBrowserView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        vm.SelectedItem = vm.Items[0];
        Dispatcher.UIThread.RunJobs();
        try
        {
            static double Left(TextBlock t, Visual root) =>
                t.TranslatePoint(default, root)!.Value.X;

            var heads = view.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.Classes.Contains("data-h")).ToList();
            var cells = view.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.Classes.Contains("data-row")).ToList();
            Assert.True(heads.Count >= 3 && cells.Count >= 3,
                $"expected a rarity table; found {heads.Count} heads and {cells.Count} cells");

            // TRAIT and VALUE are both left-aligned, so their left edges are the comparison.
            Assert.Equal(Left(heads[0], view), Left(cells[0], view), 1);
            Assert.Equal(Left(heads[1], view), Left(cells[1], view), 1);
        }
        finally { window.Close(); vm.Dispose(); Directory.Delete(dir, recursive: true); }
    }
}
