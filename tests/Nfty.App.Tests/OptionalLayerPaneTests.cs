using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
/// The Recipe pane's optional-layer column and its derived toggle.
/// </summary>
public class OptionalLayerPaneTests
{
    private static LoadedIngredient Ing(string id, params string[] vs) => new()
    {
        Manifest = new IngredientManifest(id, id.ToUpperInvariant(), LayerKind.Custom, null,
            vs.Select(v => new Variant(v, v, 1)).ToArray()),
        VariantImages = vs.ToDictionary(v => v, _ => new Image<Rgba32>(4, 4)),
    };

    private static (LoadedCookBook book, LoadedRecipe recipe) Fixture(
        IReadOnlyDictionary<string, double>? absent)
    {
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg", "hat" },
                Array.Empty<IncompatibilityRule>(), AbsentPercent: absent),
            Ingredients = new[] { Ing("bg", "day", "night"), Ing("hat", "crown", "cap") },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
        return (book, recipe);
    }

    private static RecipeDetailViewModel Pane(LoadedRecipe recipe, LoadedCookBook book,
        bool canReorder = true, Func<Func<RecipeManifest, RecipeManifest>, string, Task<LoadedCookBook?>>? edit = null) =>
        new(recipe, book, new ImageBridge(), _ => { }, canReorder: canReorder,
            editRules: edit ?? ((_, _) => Task.FromResult<LoadedCookBook?>(null)),
            dialogs: new FakeDialogs());

    [AvaloniaFact]
    public void The_toggle_is_derived_from_the_data_and_cannot_disagree_with_it()
    {
        var (plainBook, plain) = Fixture(null);
        try
        {
            using var off = Pane(plain, plainBook);
            Assert.False(off.OptionalLayers);
            Assert.False(off.ShowChanceColumn);
        }
        finally { plainBook.Dispose(); }

        var (chaseBook, chase) = Fixture(new Dictionary<string, double> { ["hat"] = 85 });
        try
        {
            using var on = Pane(chase, chaseBook);
            // Nothing stored says "the feature is on" — a chance being set IS the feature being on,
            // so there is no flag to fall out of step with the numbers underneath it.
            Assert.True(on.OptionalLayers);
            Assert.True(on.ShowChanceColumn);
        }
        finally { chaseBook.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Turning_it_on_writes_nothing_and_turning_it_off_asks_first()
    {
        var (book, recipe) = Fixture(null);
        try
        {
            int writes = 0;
            using var vm = Pane(recipe, book,
                edit: (_, _) => { writes++; return Task.FromResult<LoadedCookBook?>(null); });

            // ON reveals a column over numbers the recipe already has — every layer still always
            // appears — so there is nothing to save. The asymmetry is deliberate: the destructive
            // direction is the one that asks.
            await vm.ToggleOptionalLayersCommand.ExecuteAsync(null);
            Assert.True(vm.ShowChanceColumn);
            Assert.False(vm.OptionalLayers);
            Assert.Equal(0, writes);
        }
        finally { book.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Turning_it_off_clears_every_chance_once_confirmed()
    {
        var (book, recipe) = Fixture(new Dictionary<string, double> { ["hat"] = 85 });
        try
        {
            RecipeManifest? applied = null;
            using var vm = new RecipeDetailViewModel(recipe, book, new ImageBridge(), _ => { },
                canReorder: true, dialogs: new ScriptedDialogs(true),
                editRules: (edit, _) => { applied = edit(recipe.Manifest); return Task.FromResult<LoadedCookBook?>(null); });

            await vm.ToggleOptionalLayersCommand.ExecuteAsync(null);
            Assert.NotNull(applied);
            Assert.Null(applied!.AbsentPercent);
        }
        finally { book.Dispose(); }
    }

    [AvaloniaFact]
    public async Task A_declined_turn_off_keeps_the_chances()
    {
        var (book, recipe) = Fixture(new Dictionary<string, double> { ["hat"] = 85 });
        try
        {
            bool touched = false;
            using var vm = new RecipeDetailViewModel(recipe, book, new ImageBridge(), _ => { },
                canReorder: true, dialogs: new ScriptedDialogs(false),
                editRules: (_, _) => { touched = true; return Task.FromResult<LoadedCookBook?>(null); });

            await vm.ToggleOptionalLayersCommand.ExecuteAsync(null);
            Assert.False(touched);
        }
        finally { book.Dispose(); }
    }

    /// <summary>
    /// The chance column is a MODE, not an edit, so a locked book still shows it. That is the same
    /// distinction the pencil already draws: the lock governs what a recipe can produce, not whether
    /// you are allowed to look at it. Gating this had a second cost the contrast sweep found — the
    /// toggle rendered disabled at 0.38 and measured 1.41 against a floor of 2.0.
    /// </summary>
    [AvaloniaFact]
    public async Task A_locked_book_can_still_show_the_column_but_not_edit_it()
    {
        var (book, recipe) = Fixture(new Dictionary<string, double> { ["hat"] = 85 });
        try
        {
            using var vm = Pane(recipe, book, canReorder: false);
            Assert.True(vm.ToggleOptionalLayersCommand.CanExecute(null));
            Assert.True(vm.ShowChanceColumn);
            Assert.False(vm.CanEditChances);

            // And an edit attempted anyway writes nothing.
            vm.Layers.First(l => l.Id == "hat").AbsentPercent = 10;
            Assert.False(await vm.CommitAbsentAsync(vm.Layers.First(l => l.Id == "hat")));
        }
        finally { book.Dispose(); }
    }

    [AvaloniaFact]
    public async Task A_committed_chance_is_written_once_and_only_when_it_changed()
    {
        var (book, recipe) = Fixture(null);
        try
        {
            int writes = 0;
            RecipeManifest? applied = null;
            // The fake returns the BOOK, as the real seam does — null is how the seam says
            // "refused", so a fake that always returns null makes every save look rejected.
            using var vm = Pane(recipe, book, edit: (edit, _) =>
            {
                writes++;
                applied = edit(recipe.Manifest);
                return Task.FromResult<LoadedCookBook?>(book);
            });

            var row = vm.Layers.First(l => l.Id == "hat");
            row.AbsentPercent = 85;
            Assert.True(await vm.CommitAbsentAsync(row));
            Assert.Equal(85, applied!.AbsentPercentOf("hat"));
            Assert.Equal(1, writes);

            // A field that lost focus WITHOUT BEING CHANGED must not rewrite every PNG in the book,
            // and a numeric field loses focus constantly. Asserted against a value the manifest
            // already holds rather than by committing twice: in the app the first save rebinds the
            // pane onto the saved graph, which a fake seam does not do, so committing twice here
            // would be testing the fake.
            var untouched = vm.Layers.First(l => l.Id == "bg");
            untouched.AbsentPercent = 0;                     // what the recipe already says
            Assert.False(await vm.CommitAbsentAsync(untouched));
            Assert.Equal(1, writes);
        }
        finally { book.Dispose(); }
    }

    [AvaloniaFact]
    public void The_factor_chip_counts_the_absence_and_the_tooltip_still_tells_the_truth()
    {
        var (book, recipe) = Fixture(new Dictionary<string, double> { ["hat"] = 85 });
        try
        {
            using var vm = Pane(recipe, book);
            var hat = vm.Factors.Single(f => f.Name == "HAT");

            // The chip multiplies 3 — "not present" is an outcome the roll can land on.
            Assert.Equal(3, hat.VariantCount);
            Assert.Equal("6", vm.TotalText);

            // But the layer HAS two variants, and the table column one hover away says so. A tooltip
            // reading the factor would call it three.
            Assert.Contains("2 variants + not present", hat.Tip);
            Assert.Equal(2, vm.Layers.Single(l => l.Id == "hat").VariantCount);
        }
        finally { book.Dispose(); }
    }

    [AvaloniaFact]
    public void A_layer_that_never_appears_counts_one_not_its_variants()
    {
        var (book, recipe) = Fixture(new Dictionary<string, double> { ["hat"] = 100 });
        try
        {
            using var vm = Pane(recipe, book);
            Assert.Equal(1, vm.Factors.Single(f => f.Name == "HAT").VariantCount);
            Assert.Equal("2", vm.TotalText);
            Assert.Equal("never", vm.Layers.Single(l => l.Id == "hat").AbsentText);
        }
        finally { book.Dispose(); }
    }

    [AvaloniaFact]
    public void The_column_is_absent_from_the_table_until_it_is_asked_for()
    {
        var (book, recipe) = Fixture(null);
        try
        {
            using var vm = Pane(recipe, book);
            var view = new Views.RecipeDetailView { DataContext = vm };
            var window = new Window { Content = view, Width = 1180, Height = 720 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            try
            {
                // Header and rows are separate Grids over an Auto column — the trap this app has
                // been bitten by twice. Both cells are the same styled wrapper, so hidden they
                // measure zero in both and the column is simply not there.
                var cells = view.GetVisualDescendants().OfType<Panel>()
                    .Where(p => p.Classes.Contains("chancecell")).ToList();
                Assert.NotEmpty(cells);
                Assert.All(cells, c => Assert.False(c.IsEffectivelyVisible));

                vm.ToggleOptionalLayersCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();

                var shown = view.GetVisualDescendants().OfType<Panel>()
                    .Where(p => p.Classes.Contains("chancecell")).ToList();
                Assert.All(shown, c => Assert.True(c.IsEffectivelyVisible));
                // And every one of them is the same width, header and rows alike.
                Assert.Single(shown.Select(c => c.Bounds.Width).Distinct());
            }
            finally { window.Close(); }
        }
        finally { book.Dispose(); }
    }
}
