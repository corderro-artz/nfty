using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Shape = Avalonia.Controls.Shapes.Path;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Models;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
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

    // ---- round 1 fixes ---------------------------------------------------------------------------

    /// <summary>Answers every confirm with a fixed answer and keeps the last dialog it was shown.</summary>
    private sealed class AnsweringDialogs(bool answer) : IDialogService
    {
        public ViewModelBase? Last { get; private set; }
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase d)
        {
            Last = d;
            return Task.FromResult((TResult?)(object?)answer);
        }
        public void Close(object? result) { }
    }

    private static IngredientEditorViewModel LooseEditor(AnsweringDialogs dialogs)
    {
        var ing = Ing("body", partial: false);
        var wrapper = LooseWorkspace.WrapIngredient(ing);
        return new IngredientEditorViewModel(ing, wrapper.Recipes[0], wrapper, new ImageBridge(), new FakeNav(),
            new CookBookSession(), dialogs, new FilePickerService(),
            looseSavePath: Path.Combine(Directory.CreateTempSubdirectory().FullName, "Body.igt"),
            isEditing: () => true);
    }

    [AvaloniaFact]
    public async Task A_loose_ingredient_warning_says_this_Ingredient()
    {
        var dialogs = new AnsweringDialogs(false);
        using var vm = LooseEditor(dialogs);

        await vm.ToggleOpacityLockCommand.ExecuteAsync(null);

        var ask = Assert.IsType<ConfirmDialogViewModel>(dialogs.Last);
        Assert.Contains("this Ingredient becomes 2D only", ask.Message);
        Assert.DoesNotContain("this CookBook", ask.Message);
    }

    [AvaloniaFact]
    public async Task Accepting_the_warning_unlocks_and_declining_does_not()
    {
        var yes = new AnsweringDialogs(true);
        using (var vm = LooseEditor(yes))
        {
            Assert.True(vm.IsOpacityLocked);
            await vm.ToggleOpacityLockCommand.ExecuteAsync(null);
            Assert.Equal(OpacityLock.Unlocked, vm.OpacityMode);
        }

        var no = new AnsweringDialogs(false);
        using (var vm = LooseEditor(no))
        {
            await vm.ToggleOpacityLockCommand.ExecuteAsync(null);
            Assert.Equal(OpacityLock.Locked, vm.OpacityMode);
        }
    }

    [AvaloniaFact]
    public void A_loose_ingredient_tree_carries_the_mark()
    {
        var ing = Ing("body", partial: true);
        using var wrapper = LooseWorkspace.WrapIngredient(ing);
        using var vm = Explorer(wrapper);

        Assert.True(vm.Root.IsTwoDOnly);
        Assert.True(Layer(vm, "body").IsTwoDOnly);
    }

    /// <summary>The real Saved path: Make voxel-ready on a Rise page writes the book and raises Saved,
    /// which the Explorer is wired to exactly as OpenRise wires it.</summary>
    [AvaloniaFact]
    public async Task Make_voxel_ready_clears_the_tree_mark_through_the_saved_path()
    {
        var (session, _) = RiseVoxelReadyTests.Disk();
        var nav = new FakeNav();
        var dialogs = new RiseVoxelReadyTests.ScriptedDialogs { Answer = true };
        using var explorer = new ExplorerViewModel(session.Current!, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());
        Assert.True(explorer.Root.IsTwoDOnly);
        Assert.True(explorer.Root.Children[0].Children.Single(n => n.Id == "body").IsTwoDOnly);

        using var rise = new RiseViewModel(RiseFixtures.Variant(session, "ghost"), new ImageBridge(), nav,
            dialogs, new FilePickerService(), new StatusService());
        rise.Saved += explorer.OnEditorSaved;           // what ExplorerViewModel.OpenRise does
        await rise.MakeVoxelReadyCommand.ExecuteAsync(null);

        Assert.False(explorer.Root.IsTwoDOnly);
        Assert.False(explorer.Root.Children[0].Children.Single(n => n.Id == "body").IsTwoDOnly);
        session.Dispose();
    }

    private static double Ink(TextBlock tb) => new FormattedText(tb.Text!, CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight, new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch),
        tb.FontSize, Brushes.Black).Width;

    private static LandingViewModel ShelfOver(string dir, string longName)
    {
        for (int i = 1; i <= 6; i++)
        {
            using var b = Book(i == 1 ? "hat" : null);   // card 1 is 2D only, the rest are not
            var m = new CookBookManifest("cb", i == 1 ? longName : $"Collection {i}", new Dimensions(512, 512),
                new Collection("C", "", "C"), new Dictionary<string, double> { ["cat"] = 1 });
            CookBookArchive.Write(Path.Combine(dir, $"Book{i}.cbk"), m, b.Recipes);
        }
        var ktn = Path.Combine(dir, "Studio.ktn");
        Kitchen.Create(ktn, new KitchenManifest("studio", "Studio"));
        var kitchen = new KitchenSession();
        kitchen.Open(ktn);
        return new LandingViewModel(new FakeNav(), new FakeDialogs(), new FilePickerService(),
            new RecentsService(Directory.CreateTempSubdirectory().FullName), new CookBookSession(),
            _ => null!, _ => null!, (_, _, _) => null!, kitchen, rise: _ => null!);
    }

    /// <summary>A long name ellipsizes, and its "..." must stop short of the mark; the meta line must
    /// not lose a character to the mark at 1180 or at the smallest window.</summary>
    [AvaloniaTheory]
    [InlineData(1180.0)]
    [InlineData(-1.0)]   // the smallest window's page width
    public void A_long_name_stops_short_of_the_mark_and_the_meta_line_keeps_its_width(double width)
    {
        if (width < 0) width = ShellViewModel.MinWindowWidth / ShellViewModel.BaseScale;
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var landing = ShelfOver(dir, "A Collection Name Far Too Long To Fit On One Card");
            var view = new Views.LandingView { DataContext = landing };
            var window = new Window { Content = view, Width = width, Height = 720 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                var cards = view.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("kcard"))
                    .Where(b => b.DataContext is KitchenCard).ToList();
                var card = cards.Single(c => ((KitchenCard)c.DataContext!).TwoDOnly);
                var mark = card.GetVisualDescendants().OfType<Shape>().Single(p => p.Classes.Contains("twod"));
                var title = card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("rname"));

                var markLeft = mark.TranslatePoint(default, window)!.Value.X;
                var titleRight = title.TranslatePoint(new Avalonia.Point(title.Bounds.Width, 0), window)!.Value.X;
                Assert.True(Ink(title) > title.Bounds.Width, "fixture: the name must actually be ellipsized");
                Assert.True(titleRight <= markLeft, $"title ends at {titleRight}, mark starts at {markLeft}");

                foreach (var c in cards)
                {
                    var meta = c.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("rmeta"));
                    Assert.True(Ink(meta) <= meta.Bounds.Width + 0.5, $"meta '{meta.Text}' is truncated at {width}");
                }
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(dir, true); }
    }
}
