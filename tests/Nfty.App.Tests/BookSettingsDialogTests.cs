using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The book settings card: the one place a book's names, supply, recipe weights and swatches can be
/// edited, opened from the CookBook card's title.
/// </summary>
/// <remarks>
/// <para>The card closes the gap the "book carries two names" rule left: the CookBook card shows both
/// the book's name and the collection name it mints under, and until this card nothing in the GUI
/// could change either.</para>
/// </remarks>
public class BookSettingsDialogTests
{
    private static (ExplorerViewModel vm, CookBookSession session, string path, FakeDialogs dialogs) Explorer(
        IPaletteService? palette = null)
    {
        (var path, var session, _, _) = IngredientEditorSaveTests.OnDisk();
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var vm = new ExplorerViewModel(session.Current!, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session,
            new FilePickerService(), ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs),
            new StatusService(), palette: palette);
        return (vm, session, path, dialogs);
    }

    private static void Cleanup(ExplorerViewModel vm, CookBookSession session, string path)
    {
        vm.Dispose();
        session.Dispose();
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    private static async Task<BookSettingsDialogViewModel> Open(ExplorerViewModel vm, FakeDialogs dialogs)
    {
        await vm.ShowBookSettingsAsync();
        return Assert.IsType<BookSettingsDialogViewModel>(dialogs.Active);
    }

    /// <summary>A 12-recipe in-memory book, for the fit test.</summary>
    private static LoadedCookBook TwelveRecipes()
    {
        var recipes = Enumerable.Range(0, 12).Select(i => new LoadedRecipe
        {
            Manifest = new RecipeManifest($"r{i}", $"Recipe number {i}", [$"l{i}"], []),
            Ingredients =
            [
                new LoadedIngredient
                {
                    Manifest = new IngredientManifest($"l{i}", $"l{i}", LayerKind.Custom, null,
                        [new Variant("a", "A", 1), new Variant("b", "B", 1)]),
                    VariantImages = new Dictionary<string, Image<Rgba32>>
                    {
                        ["a"] = new Image<Rgba32>(8, 8),
                        ["b"] = new Image<Rgba32>(8, 8),
                    },
                },
            ],
        }).ToArray();
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Twelve", new Dimensions(8, 8),
                new Collection("Twelve", "a description", "TW"),
                recipes.ToDictionary(r => r.Manifest.Id, _ => 10.0),
                TargetSupply: 500,
                Palette: ["hex:d6249f", "hex:3d7f8f", "hex:202020"]),
            Recipes = recipes,
        };
    }

    // ---- opening --------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task The_card_opens_from_the_cookbook_title()
    {
        var (vm, session, path, dialogs) = Explorer();
        var window = new Window
        {
            Content = new Views.ExplorerView { DataContext = vm },
            Width = (ShellViewModel.MinWindowWidth - 24) / ShellViewModel.BaseScale,
            Height = (ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            vm.SelectNodeCommand.Execute(vm.Root);
            Dispatcher.UIThread.RunJobs();
            var title = window.GetVisualDescendants().OfType<Button>()
                .First(b => b.Classes.Contains("titlelink"));

            // A real press and release at the title's centre - raising Click on the button would
            // route the event without ever running Button's own handler, so Command would not fire.
            var centre = title.TranslatePoint(
                new Avalonia.Point(title.Bounds.Width / 2, title.Bounds.Height / 2), window)!.Value;
            window.MouseDown(centre, MouseButton.Left);
            window.MouseUp(centre, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.IsType<BookSettingsDialogViewModel>(dialogs.Active);
            await Task.CompletedTask;
        }
        finally { window.Close(); Cleanup(vm, session, path); }
    }

    // ---- saving ---------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Saving_renames_the_collection_shown_on_the_card()
    {
        var (vm, session, path, dialogs) = Explorer();
        try
        {
            vm.ToggleLockCommand.Execute(null);
            vm.SelectNodeCommand.Execute(vm.Root);
            var card = await Open(vm, dialogs);
            card.CollectionName = "Renamed Pets";
            card.Name = "Renamed Book";
            Assert.Equal("mints as Renamed Pets #1", card.MintName);

            await card.SaveCommand.ExecuteAsync(null);

            Assert.Null(dialogs.Active);                                   // closed on success
            var detail = Assert.IsType<CookBookDetailViewModel>(vm.CurrentDetail);
            Assert.Equal("mints as Renamed Pets #1", detail.MintName);
            Assert.Equal("Renamed Book", detail.Name);
            using var reread = CookBookArchive.Read(path);
            Assert.Equal("Renamed Pets", reread.Manifest.Collection.Name);
            Assert.Equal("Renamed Book", reread.Manifest.Name);
        }
        finally { Cleanup(vm, session, path); }
    }

    [AvaloniaFact]
    public async Task Target_supply_none_clears_it()
    {
        var (vm, session, path, dialogs) = Explorer();
        try
        {
            vm.ToggleLockCommand.Execute(null);
            vm.SelectNodeCommand.Execute(vm.Root);

            var first = await Open(vm, dialogs);
            Assert.True(first.NoTarget);                                   // the fixture states none
            first.NoTarget = false;
            first.TargetSupply = 500;
            await first.SaveCommand.ExecuteAsync(null);
            using (var stated = CookBookArchive.Read(path))
                Assert.Equal(500, stated.Manifest.TargetSupply);

            var second = await Open(vm, dialogs);
            Assert.False(second.NoTarget);
            Assert.Equal(500, second.TargetSupply);
            second.NoTarget = true;
            await second.SaveCommand.ExecuteAsync(null);

            using var cleared = CookBookArchive.Read(path);
            Assert.Null(cleared.Manifest.TargetSupply);                    // None, never 0
        }
        finally { Cleanup(vm, session, path); }
    }

    [AvaloniaFact]
    public async Task The_editors_saved_palette_can_be_carried_into_the_book()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var palette = new PaletteService(StateStore.At(dir));
        palette.Add(new RgbColor(214, 36, 159), PaletteMode.Color);
        palette.Add(new RgbColor(61, 61, 61), PaletteMode.Grayscale);
        var (vm, session, path, dialogs) = Explorer(palette);
        try
        {
            vm.ToggleLockCommand.Execute(null);
            vm.SelectNodeCommand.Execute(vm.Root);
            var card = await Open(vm, dialogs);
            Assert.Empty(card.Swatches);
            Assert.True(card.AddEditorPaletteCommand.CanExecute(null));

            card.AddEditorPaletteCommand.Execute(null);
            Assert.Equal(2, card.Swatches.Count);
            Assert.False(card.AddEditorPaletteCommand.CanExecute(null));   // nothing left to add
            await card.SaveCommand.ExecuteAsync(null);

            using var reread = CookBookArchive.Read(path);
            Assert.Equal(2, reread.Manifest.Palette!.Count);

            var again = await Open(vm, dialogs);
            again.Swatches[0].RemoveCommand.Execute(null);
            Assert.True(again.IsDirty);
        }
        finally { Cleanup(vm, session, path); Directory.Delete(dir, recursive: true); }
    }

    // ---- the weight caveat ----------------------------------------------------------------------

    [AvaloniaFact]
    public void A_weight_change_warns_that_the_collection_changes()
    {
        using var book = TwelveRecipes();
        var vm = new BookSettingsDialogViewModel(new FakeDialogs(), book, null, () => true,
            _ => Task.FromResult(new BookSettingsOutcome(true, [])));
        var view = new Views.BookSettingsDialogView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var note = view.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.Classes.Contains("wnote"));
            var before = note.Bounds;
            Assert.False(vm.HasWeightChange);
            Assert.Equal(0, note.Opacity);

            vm.Weights[3].Weight = 40;
            Dispatcher.UIThread.RunJobs();

            Assert.True(vm.HasWeightChange);
            Assert.Equal(1, note.Opacity);
            Assert.Contains("which assets a seed produces", note.Text);
            Assert.Equal(before, note.Bounds);                             // reserved: nothing moved
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Weight_zero_reads_shelved_and_the_shares_follow_the_weights()
    {
        using var book = TwelveRecipes();
        var vm = new BookSettingsDialogViewModel(new FakeDialogs(), book, null, () => true,
            _ => Task.FromResult(new BookSettingsOutcome(true, [])));

        Assert.Equal("8.3%", vm.Weights[0].ShareText);                     // 10 of 120
        vm.Weights[0].Weight = 0;
        Assert.Equal("shelved", vm.Weights[0].ShareText);
        Assert.Equal("9.1%", vm.Weights[1].ShareText);                     // 10 of 110
    }

    // ---- the lock -------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task The_card_refuses_to_save_on_a_locked_book_even_when_forced()
    {
        using var book = TwelveRecipes();
        int commits = 0;
        var vm = new BookSettingsDialogViewModel(new FakeDialogs(), book, null, () => false,
            _ => { commits++; return Task.FromResult(new BookSettingsOutcome(true, [])); });
        vm.Name = "Changed";
        Assert.False(vm.SaveCommand.CanExecute(null));

        // The generated command's ExecuteAsync does not consult CanExecute, so a dimmed button is
        // the only thing standing here unless the command asks again itself.
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(0, commits);
        Assert.Contains("locked", vm.StatusLine, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task A_locked_explorer_writes_nothing_even_if_the_card_is_forced()
    {
        var (vm, session, path, dialogs) = Explorer();
        try
        {
            vm.SelectNodeCommand.Execute(vm.Root);                          // book opens LOCKED
            Assert.False(vm.IsEditing);
            var card = await Open(vm, dialogs);
            card.Name = "Sneaky";

            var outcome = await vm.CommitBookSettingsAsync(b => b);
            Assert.False(outcome.Saved);

            using var reread = CookBookArchive.Read(path);
            Assert.Equal("Book", reread.Manifest.Name);
        }
        finally { Cleanup(vm, session, path); }
    }

    // ---- validation -----------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task An_invalid_edit_is_reported_and_not_written()
    {
        var (vm, session, path, dialogs) = Explorer();
        try
        {
            vm.ToggleLockCommand.Execute(null);
            vm.SelectNodeCommand.Execute(vm.Root);
            var card = await Open(vm, dialogs);
            card.Weights[0].Weight = 0;                    // the only recipe, shelved: nothing can roll
            card.Name = "Also renamed";

            await card.SaveCommand.ExecuteAsync(null);

            Assert.Same(card, dialogs.Active);                             // still open
            Assert.Contains("zero total recipe weight", card.StatusLine);
            using var reread = CookBookArchive.Read(path);
            Assert.Equal(100, reread.Manifest.RecipeWeights["cat"]);       // nothing written
            Assert.Equal("Book", reread.Manifest.Name);
        }
        finally { Cleanup(vm, session, path); }
    }

    [AvaloniaFact]
    public async Task A_refusal_is_cleared_once_the_author_changes_a_value()
    {
        var (vm, session, path, dialogs) = Explorer();
        try
        {
            vm.ToggleLockCommand.Execute(null);
            vm.SelectNodeCommand.Execute(vm.Root);
            var card = await Open(vm, dialogs);
            card.Weights[0].Weight = 0;
            await card.SaveCommand.ExecuteAsync(null);
            Assert.Contains("zero total recipe weight", card.StatusLine);     // survives the save's own bookkeeping

            card.Weights[0].Weight = 5;                                       // the cause is gone

            Assert.Equal(string.Empty, card.StatusLine);
        }
        finally { Cleanup(vm, session, path); }
    }

    // ---- fit and geometry -----------------------------------------------------------------------

    [AvaloniaFact]
    public void The_card_fits_the_smallest_window_with_twelve_recipes()
    {
        using var book = TwelveRecipes();
        var vm = new BookSettingsDialogViewModel(new FakeDialogs(), book, null, () => true,
            _ => Task.FromResult(new BookSettingsOutcome(true, [])));
        // The riskiest state: a weight caveat lit, a refusal printed, fourteen swatches.
        vm.Weights[0].Weight = 3;
        vm.StatusLine = "CookBook has zero total recipe weight.";
        var view = new Views.BookSettingsDialogView { DataContext = vm };
        var window = new Window { Content = view, Width = 1600, Height = 1200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            view.Measure(Avalonia.Size.Infinity);
            var size = view.DesiredSize;

            double needW = size.Width * ShellViewModel.BaseScale + 24;
            double needH = size.Height * ShellViewModel.BaseScale + ShellViewModel.ChromeReserve;
            Assert.True(ShellViewModel.MinWindowWidth >= needW && ShellViewModel.MinWindowHeight >= needH,
                $"The book settings card needs {needW:0} x {needH:0}; the minimum window is " +
                $"{ShellViewModel.MinWindowWidth:0} x {ShellViewModel.MinWindowHeight:0}");

            // And it pages rather than growing: twelve recipes show four rows.
            Assert.Equal(BookSettingsDialogViewModel.PageSize, vm.VisibleWeights.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_title_line_keeps_its_geometry_as_a_button()
    {
        var (vm, session, path, _) = Explorer();
        var window = new Window
        {
            Content = new Views.ExplorerView { DataContext = vm },
            Width = (ShellViewModel.MinWindowWidth - 24) / ShellViewModel.BaseScale,
            Height = (ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            vm.SelectNodeCommand.Execute(vm.Root);
            Dispatcher.UIThread.RunJobs();
            var button = window.GetVisualDescendants().OfType<Button>()
                .First(b => b.Classes.Contains("titlelink"));
            var text = button.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.Classes.Contains("idname"));

            // The button adds NOTHING around the text: the same size, at its origin. Fluent's
            // padding, border and minimum size would each show up here.
            Assert.Equal(text.Bounds.Size, button.Bounds.Size);
            var origin = text.TranslatePoint(default, button)!.Value;
            Assert.Equal(0, origin.X, 0.01);
            Assert.Equal(0, origin.Y, 0.01);
            Assert.Equal(24, text.Bounds.Height, 0.5);                     // .idname's stated line box
        }
        finally { window.Close(); Cleanup(vm, session, path); }
    }

    /// <summary>
    /// The pencil that says the title is a link costs the card no geometry: it is an overlay drawn
    /// in the gap beside the title, inked only while the title is hovered or focused. At rest the
    /// button is exactly the title's width, the suffix is still one column gap away, and the pencil is
    /// transparent; its ink fits inside that gap. (The card's pixels at rest were also diffed against
    /// the frames from before the pencil existed: identical.)
    /// </summary>
    [AvaloniaFact]
    public async Task The_title_pencil_is_reserved_in_the_gap_and_inked_only_on_hover()
    {
        var (vm, session, path, dialogs) = Explorer();
        var window = new Window
        {
            Content = new Views.ExplorerView { DataContext = vm },
            Width = (ShellViewModel.MinWindowWidth - 24) / ShellViewModel.BaseScale,
            Height = (ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            vm.SelectNodeCommand.Execute(vm.Root);
            Dispatcher.UIThread.RunJobs();
            var title = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("titlelink"));
            var text = title.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("idname"));
            var pen = title.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().First(p => p.Classes.Contains("pen"));
            var suffix = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("mintname"));

            // No layout cost: the button is the title's width, and the suffix still starts one gap on.
            Assert.Equal(text.Bounds.Width, title.Bounds.Width, 0.5);
            double buttonRight = title.TranslatePoint(new Avalonia.Point(title.Bounds.Width, 0), window)!.Value.X;
            double suffixLeft = suffix.TranslatePoint(new Avalonia.Point(0, 0), window)!.Value.X;
            Assert.Equal(10, suffixLeft - buttonRight, 0.5);

            // The ink (2..10 of the 12px box) lands inside that gap.
            double penLeft = pen.TranslatePoint(new Avalonia.Point(0, 0), window)!.Value.X;
            Assert.True(penLeft >= buttonRight - 0.5 && penLeft + 10 <= suffixLeft + 0.5,
                $"the pencil's ink spans {penLeft + 2:F1}..{penLeft + 10:F1} in a gap of {buttonRight:F1}..{suffixLeft:F1}");

            // Ink toggled, not space: transparent at rest, drawn once the pointer is on the title.
            Assert.Equal(0, pen.Opacity);
            var centre = title.TranslatePoint(new Avalonia.Point(title.Bounds.Width / 2, title.Bounds.Height / 2), window)!.Value;
            window.MouseMove(centre);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, pen.Opacity);
            await Task.CompletedTask;
        }
        finally { window.Close(); Cleanup(vm, session, path); }
    }
}
