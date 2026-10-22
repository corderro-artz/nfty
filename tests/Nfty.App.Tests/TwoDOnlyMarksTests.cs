using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Shape = Avalonia.Controls.Shapes.Path;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Models;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// THE 2D-ONLY MARKS AND THE OPACITY-LOCK WARNING. A book (and each layer) carrying a partly
/// transparent pixel is 2D only; the tree and the Kitchen shelf say so, from the pixels, and the
/// editor says so BEFORE it lets one in.
/// </summary>
public class TwoDOnlyMarksTests
{
    private static LoadedIngredient Ing(string id, bool partial)
    {
        var img = new Image<Rgba32>(8, 8);
        img[1, 1] = new Rgba32(200, 10, 10, partial ? (byte)128 : (byte)255);
        return new LoadedIngredient
        {
            Manifest = new IngredientManifest(id, id.ToUpperInvariant(), LayerKind.Custom, null,
                new[] { new Variant("a", "A", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = img },
        };
    }

    /// <summary>One recipe, layers "bg" and "hat"; <paramref name="partialLayer"/> (if any) carries partial alpha.</summary>
    private static LoadedCookBook Book(string? partialLayer) => new()
    {
        Manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8), new Collection("Book", "", "B"),
            new Dictionary<string, double> { ["cat"] = 1 }),
        Recipes = new[]
        {
            new LoadedRecipe
            {
                Manifest = new RecipeManifest("cat", "Cat", new[] { "bg", "hat" }, Array.Empty<IncompatibilityRule>()),
                Ingredients = new[] { Ing("bg", partialLayer == "bg"), Ing("hat", partialLayer == "hat") },
            },
        },
    };

    private static ExplorerViewModel Explorer(LoadedCookBook book)
    {
        var nav = new FakeNav();
        var session = new CookBookSession();
        var dialogs = new FakeDialogs();
        return new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());
    }

    private static ExplorerNode Layer(ExplorerViewModel vm, string id) => vm.Root.Children[0].Children.Single(c => c.Id == id);

    [AvaloniaFact]
    public void A_partial_alpha_layer_and_its_book_carry_the_mark()
    {
        using var book = Book("hat");
        using var vm = Explorer(book);

        Assert.True(vm.Root.IsTwoDOnly);
        Assert.True(Layer(vm, "hat").IsTwoDOnly);
        Assert.False(Layer(vm, "bg").IsTwoDOnly);
        Assert.False(vm.Root.Children[0].IsTwoDOnly);   // the recipe row carries no mark
    }

    [AvaloniaFact]
    public void A_book_with_clean_pixels_carries_no_mark_anywhere()
    {
        using var book = Book(null);
        using var vm = Explorer(book);

        Assert.False(vm.Root.IsTwoDOnly);
        Assert.All(vm.Root.Children[0].Children, n => Assert.False(n.IsTwoDOnly));
    }

    [AvaloniaFact]
    public void The_mark_follows_a_save()
    {
        using var dirty = Book("hat");
        using var vm = Explorer(dirty);
        Assert.True(Layer(vm, "hat").IsTwoDOnly);

        using var cleaned = Book(null);          // what Make voxel-ready / an editor save hands back
        vm.OnEditorSaved(cleaned);
        Assert.False(vm.Root.IsTwoDOnly);
        Assert.False(Layer(vm, "hat").IsTwoDOnly);

        using var again = Book("bg");
        vm.OnEditorSaved(again);
        Assert.True(vm.Root.IsTwoDOnly);
        Assert.True(Layer(vm, "bg").IsTwoDOnly);
        Assert.False(Layer(vm, "hat").IsTwoDOnly);
    }

    [AvaloniaFact]
    public void Filtering_the_tree_keeps_the_mark()
    {
        using var book = Book("hat");
        using var vm = Explorer(book);
        vm.SearchQuery = "hat";
        Assert.True(vm.Root.IsTwoDOnly);
        Assert.True(vm.Root.Children[0].Children.Single().IsTwoDOnly);
    }

    [Fact]
    public void The_kitchen_shelf_marks_a_two_d_only_cookbook()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using var dirty = Book("hat");
            using var clean = Book(null);
            var dirtyPath = Path.Combine(dir, "dirty.cbk");
            var cleanPath = Path.Combine(dir, "clean.cbk");
            CookBookArchive.Write(dirtyPath, dirty.Manifest, dirty.Recipes);
            CookBookArchive.Write(cleanPath, clean.Manifest, clean.Recipes);

            var cards = KitchenShelfViewModel.CardsFor(new KitchenContents(null!, dir,
                new[] { dirtyPath, cleanPath }, Array.Empty<string>(), Array.Empty<string>()));

            Assert.True(cards.Single(c => c.Path == dirtyPath).TwoDOnly);
            Assert.False(cards.Single(c => c.Path == cleanPath).TwoDOnly);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task The_warning_names_the_two_d_only_consequence_before_any_pixel()
    {
        var f = IngredientEditorSaveTests.OnDisk(LayerKind.Dynamic);
        var dialogs = new FakeDialogs();
        try
        {
            using var vm = new IngredientEditorViewModel(f.ing, f.recipe, f.session.Current!, new ImageBridge(),
                new FakeNav(), f.session, dialogs, new FilePickerService());

            var pending = vm.ToggleOpacityLockCommand.ExecuteAsync(null);
            await pending;

            var ask = Assert.IsType<ConfirmDialogViewModel>(dialogs.Active);
            Assert.Equal("Allow partial transparency?", ask.Title);
            Assert.Contains("2D only", ask.Message);
            Assert.Contains("this CookBook", ask.Message);
            Assert.Contains("Make voxel-ready", ask.Message);
            Assert.True(vm.IsOpacityLocked);          // a gate: nothing unlocked until accepted
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    private static (Window Window, Views.ExplorerView View) Show(ExplorerViewModel vm)
    {
        var view = new Views.ExplorerView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    private static (Border Row, Shape Mark, TextBlock Name) Parts(Avalonia.Visual view, ExplorerNode node)
    {
        var row = view.GetVisualDescendants().OfType<Border>()
            .First(b => b.Classes.Contains("node") && ReferenceEquals(b.DataContext, node));
        var mark = row.GetVisualDescendants().OfType<Shape>().Single(p => p.Classes.Contains("twod"));
        var name = row.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == node.Name);
        return (row, mark, name);
    }

    [AvaloniaFact]
    public void The_mark_reserves_its_slot()
    {
        using var dirty = Book("hat");
        using var clean = Book(null);
        using var a = Explorer(dirty);
        using var b = Explorer(clean);
        a.Root.Children[0].IsExpanded = true;
        b.Root.Children[0].IsExpanded = true;
        var (wa, va) = Show(a);
        var (wb, vb) = Show(b);
        try
        {
            Dispatcher.UIThread.RunJobs();
            var on = Parts(va, Layer(a, "hat"));
            var off = Parts(vb, Layer(b, "hat"));

            Assert.Equal(1.0, on.Mark.Opacity);
            Assert.Equal(0.0, off.Mark.Opacity);
            Assert.True(off.Mark.Bounds.Width > 0 && off.Mark.Bounds.Height > 0, "slot must be laid out when the ink is off");
            Assert.Equal(on.Row.Bounds, off.Row.Bounds);
            Assert.Equal(on.Name.Bounds, off.Name.Bounds);
            Assert.Equal(on.Mark.Bounds, off.Mark.Bounds);
            Assert.False(off.Mark.IsHitTestVisible);
        }
        finally { wa.Close(); wb.Close(); }
    }
}
