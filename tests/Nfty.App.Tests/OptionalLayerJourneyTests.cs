using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The optional-layers feature end to end: a number typed into the table's ABSENT cell, through the
/// pane's own commit, through the Explorer's gate, into the <c>.cbk</c> on disk, and out again as
/// assets that really are missing that layer.
///
/// <para>Everything else about the feature is covered a level up or a level down — the roller, the
/// unique space, the metadata, the pane's toggle — and none of it answers the question this file
/// exists for: <b>does pressing the thing do the thing</b>. This project has twice shipped a control
/// that looked wired and was not (Landing's "+ Recipe" dropped its wizard's result on the floor; the
/// rules panel's row actions were unreachable by mouse), and both were found by a person driving the
/// app rather than by a test. A journey test is the repeatable version of that drive: it starts at
/// the ViewModel the view binds to and ends at the pixels, with a real file in the middle.</para>
/// </summary>
public class OptionalLayerJourneyTests
{
    private static LoadedIngredient Ing(string id, params string[] variants) => new()
    {
        Manifest = new IngredientManifest(id, id.ToUpperInvariant(), LayerKind.Custom, null,
            variants.Select(v => new Variant(v, v, 1)).ToArray()),
        VariantImages = variants.ToDictionary(v => v, _ => new Image<Rgba32>(8, 8, new Rgba32(1, 2, 3, 255))),
    };

    private static LoadedCookBook MemoryBook() => new()
    {
        Manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
            new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
        Recipes = new[]
        {
            new LoadedRecipe
            {
                Manifest = new RecipeManifest("cat", "Cat", new[] { "bg", "aura" },
                    Array.Empty<IncompatibilityRule>()),
                Ingredients = new[] { Ing("bg", "day", "night"), Ing("aura", "none", "glow") },
            },
        },
    };

    private static (string Path, CookBookSession Session) OnDisk()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "book.cbk");
        using (var seed = MemoryBook())
            CookBookArchive.Write(path, seed.Manifest, seed.Recipes);
        var session = new CookBookSession();
        session.Open(CookBookArchive.Read(path), path);
        return (path, session);
    }

    private static ExplorerViewModel Explorer(CookBookSession session, IStatusService status)
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        return new ExplorerViewModel(session.Current!, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), status);
    }

    private static void Cleanup(CookBookSession session, string path)
    {
        session.Dispose();
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    /// <summary>Opens the book unlocked with the Cat recipe on screen — the state a user is in when
    /// they reach for the ABSENT column.</summary>
    private static RecipeDetailViewModel OpenRecipe(ExplorerViewModel explorer)
    {
        explorer.ToggleLockCommand.Execute(null);   // a chance is structure; the lock governs it
        explorer.SelectNodeCommand.Execute(explorer.Root.Children[0]);
        return Assert.IsType<RecipeDetailViewModel>(explorer.CurrentDetail);
    }

    [AvaloniaFact]
    public async Task A_chance_typed_into_the_table_reaches_the_archive_and_comes_back_on_the_pane()
    {
        var (path, session) = OnDisk();
        try
        {
            using var explorer = Explorer(session, new StatusService());
            var pane = OpenRecipe(explorer);

            // The column is not there until it is asked for — the reveal the OPTIONAL LAYERS chip
            // performs. Nothing is written by asking.
            Assert.False(pane.ShowChanceColumn);
            await pane.ToggleOptionalLayersCommand.ExecuteAsync(null);
            Assert.True(pane.ShowChanceColumn);
            Assert.True(pane.CanEditChances);

            // What the view does on lost focus: the row's bound value has already changed, and the
            // pane is asked to commit it.
            var aura = pane.Layers.Single(l => l.Id == "aura");
            aura.AbsentPercent = 40;
            Assert.True(await pane.CommitAbsentAsync(aura));

            // On disk, which is the only claim that matters.
            using var reread = CookBookArchive.Read(path);
            Assert.Equal(40, reread.Recipes[0].Manifest.AbsentPercentOf("aura"));
            Assert.Equal(0, reread.Recipes[0].Manifest.AbsentPercentOf("bg"));

            // And the pane the user is still looking at agrees, without navigating away and back.
            var after = Assert.IsType<RecipeDetailViewModel>(explorer.CurrentDetail);
            Assert.True(after.OptionalLayers);
            Assert.Equal(40, after.Layers.Single(l => l.Id == "aura").AbsentPercent);
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>
    /// The end of the journey: the saved book, reopened from disk and cooked, produces assets that
    /// really are missing the layer — and assets that still have it.
    /// </summary>
    /// <remarks>
    /// This is the assertion the ViewModel tests cannot make. A chance that reached the manifest and
    /// changed no pixel would pass every one of them.
    /// </remarks>
    [AvaloniaFact]
    public async Task The_saved_book_cooks_assets_that_really_are_missing_the_layer()
    {
        var (path, session) = OnDisk();
        try
        {
            using (var explorer = Explorer(session, new StatusService()))
            {
                var pane = OpenRecipe(explorer);
                await pane.ToggleOptionalLayersCommand.ExecuteAsync(null);
                var aura = pane.Layers.Single(l => l.Id == "aura");
                aura.AbsentPercent = 50;
                Assert.True(await pane.CommitAbsentAsync(aura));
            }

            using var reread = CookBookArchive.Read(path);
            using var cooked = Generator.Generate(reread,
                new GenerateOptions(40, "journey", EnforceUniqueDna: false));

            var without = cooked.Assets.Where(a => a.AbsentLayers.Any(l => l.IngredientId == "aura")).ToList();
            var with = cooked.Assets.Where(a => !a.AbsentLayers.Any(l => l.IngredientId == "aura")).ToList();

            // Both outcomes occur — a chance that always fired or never fired would be a different
            // bug wearing the same passing test.
            Assert.NotEmpty(without);
            Assert.NotEmpty(with);

            // An absent layer publishes nothing: no trait, and no "None" value invented to stand in
            // for one. And bg, which is not optional, is on every asset either way.
            Assert.All(without, a =>
            {
                Assert.DoesNotContain(a.Traits, t => t.IngredientId == "aura");
                Assert.Contains(a.Traits, t => t.IngredientId == "bg");
            });
            Assert.All(with, a => Assert.Contains(a.Traits, t => t.IngredientId == "aura"));
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>
    /// 100 shelves a layer: it is still in the recipe, and it never appears. That is the state the
    /// table prints as "never" rather than as a percentage, and it must survive the round trip —
    /// shelving is how an author parks a layer without deleting its art.
    /// </summary>
    [AvaloniaFact]
    public async Task A_hundred_shelves_the_layer_without_removing_it()
    {
        var (path, session) = OnDisk();
        try
        {
            using (var explorer = Explorer(session, new StatusService()))
            {
                var pane = OpenRecipe(explorer);
                await pane.ToggleOptionalLayersCommand.ExecuteAsync(null);
                var aura = pane.Layers.Single(l => l.Id == "aura");
                aura.AbsentPercent = 100;
                Assert.Equal("never", aura.AbsentText);
                Assert.True(await pane.CommitAbsentAsync(aura));
            }

            using var reread = CookBookArchive.Read(path);
            // Still in the stack, still carrying its art.
            Assert.Contains("aura", reread.Recipes[0].Manifest.LayerOrder);
            Assert.Contains(reread.Recipes[0].Ingredients, i => i.Manifest.Id == "aura");

            using var cooked = Generator.Generate(reread,
                new GenerateOptions(8, "shelved", EnforceUniqueDna: false));
            Assert.All(cooked.Assets, a =>
            {
                Assert.Contains(a.AbsentLayers, l => l.IngredientId == "aura");
                Assert.DoesNotContain(a.Traits, t => t.IngredientId == "aura");
            });
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>
    /// Setting a chance back to zero removes it, and the book returns to the shape it had before the
    /// feature was ever touched — no <c>absentPercent</c> key at all.
    /// </summary>
    /// <remarks>
    /// Not cosmetic. The whole no-RNG-change guarantee rests on a book that does not use the feature
    /// being indistinguishable from one written before it existed, so "undo" has to reach that
    /// shape rather than an equivalent-looking dictionary of zeros.
    /// </remarks>
    [AvaloniaFact]
    public async Task Setting_it_back_to_zero_leaves_no_trace_in_the_file()
    {
        var (path, session) = OnDisk();
        try
        {
            using var explorer = Explorer(session, new StatusService());
            var pane = OpenRecipe(explorer);
            await pane.ToggleOptionalLayersCommand.ExecuteAsync(null);

            var aura = pane.Layers.Single(l => l.Id == "aura");
            aura.AbsentPercent = 25;
            Assert.True(await pane.CommitAbsentAsync(aura));

            var again = Assert.IsType<RecipeDetailViewModel>(explorer.CurrentDetail);
            var row = again.Layers.Single(l => l.Id == "aura");
            row.AbsentPercent = 0;
            Assert.True(await again.CommitAbsentAsync(row));

            using var reread = CookBookArchive.Read(path);
            Assert.Null(reread.Recipes[0].Manifest.AbsentPercent);
            Assert.False(reread.Recipes[0].Manifest.HasOptionalLayers);
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>
    /// A locked book refuses the commit and writes nothing. The pane already declines to offer the
    /// field, but the gate that guards the FILE is the Explorer's, and that is what this asserts.
    /// </summary>
    [AvaloniaFact]
    public async Task A_locked_book_refuses_the_commit_and_the_file_is_untouched()
    {
        var (path, session) = OnDisk();
        try
        {
            using var explorer = Explorer(session, new StatusService());   // opens LOCKED
            explorer.SelectNodeCommand.Execute(explorer.Root.Children[0]);
            var pane = Assert.IsType<RecipeDetailViewModel>(explorer.CurrentDetail);

            Assert.False(pane.CanEditChances);
            var aura = pane.Layers.Single(l => l.Id == "aura");
            aura.AbsentPercent = 70;
            Assert.False(await pane.CommitAbsentAsync(aura));

            using var reread = CookBookArchive.Read(path);
            Assert.Null(reread.Recipes[0].Manifest.AbsentPercent);
        }
        finally { Cleanup(session, path); }
    }
}
