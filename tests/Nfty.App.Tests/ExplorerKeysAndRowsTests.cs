using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Models;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.App.Views;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The Explorer's keys, the panes' clickable titles, and the two tables' shared row shape: every
/// chord printed in the Quick Reference does what it says, the variant table never selects nothing,
/// and the layer and variant rows are one height.
/// </summary>
public class ExplorerKeysAndRowsTests
{
    private static LoadedIngredient Ing(string id, params string[] variants) => new()
    {
        Manifest = new IngredientManifest(id, id.ToUpperInvariant(), LayerKind.Custom, null,
            variants.Select(v => new Variant(v, v.ToUpperInvariant(), 1)).ToArray()),
        VariantImages = variants.ToDictionary(v => v, _ => new Image<Rgba32>(8, 8)),
    };

    private static (string Path, CookBookSession Session) OnDisk()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "book.cbk");
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8), new Collection("Book", "", "B"),
                new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("cat", "Cat", new[] { "body", "hat" }, Array.Empty<IncompatibilityRule>()),
                    Ingredients = new[] { Ing("body", "a", "b"), Ing("hat", "x") },
                },
            },
        };
        using (book) CookBookArchive.Write(path, book.Manifest, book.Recipes);
        var session = new CookBookSession();
        session.Open(CookBookArchive.Read(path), path);
        return (path, session);
    }

    private static (ExplorerViewModel Vm, FakeDialogs Dialogs) Explorer(ICookBookSession session)
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var vm = new ExplorerViewModel(session.Current!, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());
        return (vm, dialogs);
    }

    private static void Cleanup(ICookBookSession session, string path)
    {
        session.Dispose();
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    [AvaloniaFact]
    public void F2_opens_the_rename_card_and_Ctrl_B_the_book_settings()
    {
        var (path, session) = OnDisk();
        try
        {
            var (vm, dialogs) = Explorer(session);
            using (vm)
            {
                var view = new ExplorerView { DataContext = vm };
                vm.SelectedNode = vm.Root.Children[0];
                Assert.True(view.OnShortcut(vm, Key.F2, KeyModifiers.None));
                var rename = Assert.IsType<ItemSettingsDialogViewModel>(dialogs.Active);
                Assert.Equal("Cat", rename.Name);

                Assert.True(view.OnShortcut(vm, Key.B, KeyModifiers.Control));
                Assert.IsType<BookSettingsDialogViewModel>(dialogs.Active);
            }
        }
        finally { Cleanup(session, path); }
    }

    [AvaloniaFact]
    public void Ctrl_L_toggles_the_lock_and_Delete_asks_before_deleting()
    {
        var (path, session) = OnDisk();
        try
        {
            var (vm, dialogs) = Explorer(session);
            using (vm)
            {
                var view = new ExplorerView { DataContext = vm };
                Assert.False(vm.IsEditing);
                Assert.True(view.OnShortcut(vm, Key.L, KeyModifiers.Control));
                Assert.True(vm.IsEditing);
                vm.SelectedNode = vm.Root.Children[0].Children[1];
                Assert.True(view.OnShortcut(vm, Key.Delete, KeyModifiers.None));
                Assert.IsType<ConfirmDialogViewModel>(dialogs.Active);   // asks; the fake answers no
                Assert.Equal(2, vm.Root.Children[0].Children.Count);
            }
        }
        finally { Cleanup(session, path); }
    }

    [AvaloniaFact]
    public void Every_title_opens_its_editor()
    {
        var (path, session) = OnDisk();
        try
        {
            var (vm, dialogs) = Explorer(session);
            using (vm)
            {
                vm.SelectedNode = vm.Root.Children[0];
                var recipe = Assert.IsType<RecipeDetailViewModel>(vm.CurrentDetail);
                recipe.EditNameCommand.Execute(null);
                Assert.Equal("Edit Recipe", Assert.IsType<ItemSettingsDialogViewModel>(dialogs.Active).Title);

                vm.SelectedNode = vm.Root.Children[0].Children[0];
                var layer = Assert.IsType<IngredientDetailViewModel>(vm.CurrentDetail);
                layer.EditNameCommand.Execute(null);
                Assert.Equal("Edit Ingredient", Assert.IsType<ItemSettingsDialogViewModel>(dialogs.Active).Title);
            }
        }
        finally { Cleanup(session, path); }
    }

    [AvaloniaFact]
    public void The_variant_table_never_selects_nothing()
    {
        var (path, session) = OnDisk();
        try
        {
            var (vm, _) = Explorer(session);
            using (vm)
            {
                vm.SelectedNode = vm.Root.Children[0].Children[0];
                var layer = Assert.IsType<IngredientDetailViewModel>(vm.CurrentDetail);
                Assert.Equal("a", layer.Selected!.Id);              // opens on the first
                layer.SelectVariantCommand.Execute("a");             // clicking it again keeps it
                Assert.Equal("a", layer.Selected!.Id);
                layer.SelectVariantCommand.Execute("b");
                Assert.Equal("b", layer.Selected!.Id);
                Assert.Single(layer.Variants, v => v.IsSelected);
            }
        }
        finally { Cleanup(session, path); }
    }

    [AvaloniaFact]
    public void Layer_rows_and_variant_rows_are_one_height_and_both_carry_art()
    {
        var (path, session) = OnDisk();
        try
        {
            var (vm, _) = Explorer(session);
            using (vm)
            {
                vm.SelectedNode = vm.Root.Children[0];
                var recipeView = new RecipeDetailView { DataContext = vm.CurrentDetail };
                var w1 = new Window { Width = 900, Height = 600, Content = recipeView };
                w1.Show(); Dispatcher.UIThread.RunJobs();
                var layerRows = recipeView.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("thumbrow")).ToArray();
                Assert.NotEmpty(layerRows);
                Assert.All(((RecipeDetailViewModel)vm.CurrentDetail!).Layers, r => Assert.NotNull(r.Thumbnail));
                double layerHeight = layerRows[0].Bounds.Height;
                w1.Close();

                vm.SelectedNode = vm.Root.Children[0].Children[0];
                var ingView = new IngredientDetailView { DataContext = vm.CurrentDetail };
                var w2 = new Window { Width = 900, Height = 600, Content = ingView };
                w2.Show(); Dispatcher.UIThread.RunJobs();
                var variantRows = ingView.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("thumbrow")).ToArray();
                Assert.NotEmpty(variantRows);
                Assert.Equal(layerHeight, variantRows[0].Bounds.Height, 0.5);
                // The variant rows carry the grip the layer rows do.
                Assert.Equal(variantRows.Length, ingView.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("grip")));
                w2.Close();
            }
        }
        finally { Cleanup(session, path); }
    }

    [AvaloniaFact]
    public void Every_explorer_chord_is_in_the_quick_reference()
    {
        foreach (var cap in new[] { KeyHints.Rename, KeyHints.AddItem, KeyHints.ToggleLock, KeyHints.BookSettings,
                     KeyHints.Cook, KeyHints.OpenInViewer, KeyHints.Reroll, KeyHints.RiseKey, KeyHints.EditLayer, KeyHints.ChildWindows })
            Assert.Contains(HelpCatalog.Entries, e => e.Cap == cap);
    }
}
