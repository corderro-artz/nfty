using System.IO;
using System.IO.Compression;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Publish;
using Nfty.Ticket;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The CookBook card's "Export Menu…": a dialog that calls the CLI's own <see cref="MenuWriter"/>.
/// </summary>
/// <remarks>
/// The journey test is the one that matters: dialog → file on disk → read back with
/// <see cref="MenuArchive"/> → one asset rolled and rendered, pixel-equal to what the CLI's
/// <c>menu</c> + <c>serve</c> path produces from the same book. The rest pin the card's own rules.
/// </remarks>
public class MenuExportDialogTests
{
    private sealed class SavePicker(string? path) : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string t, params string[] e) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string t, string e) => Task.FromResult(path);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult<string?>(null);
    }

    private static MenuExportDialogViewModel Vm(LoadedCookBook book, string mnu, FakeDialogs? dialogs = null,
        IStatusService? status = null) =>
        new(book, Path.ChangeExtension(mnu, ".cbk"), new SavePicker(null), dialogs ?? new FakeDialogs(),
            status ?? new StatusService());

    private static string Roll(MenuArchive menu, string seed, out byte[] pixels)
    {
        var prepared = Ticketer.Prepare(menu.Book);
        var rng = new Nfty.Core.Generation.SplitMix64Rng(Nfty.Core.Generation.SeedHash.ToUlong(seed));
        AssetTicket? ticket = null;
        for (int i = 0; i < 50 && ticket is null; i++) ticket = Ticketer.TryRoll(prepared, rng);
        Assert.NotNull(ticket);
        using var image = MenuRenderer.Render(menu, ticket!);
        var buffer = new Rgba32[image.Width * image.Height];
        image.CopyPixelDataTo(buffer);
        pixels = buffer.SelectMany(p => new[] { p.R, p.G, p.B, p.A }).ToArray();
        return ticket!.Dna;
    }

    [AvaloniaFact]
    public async Task Exporting_an_atlas_menu_writes_a_readable_mnu()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        using var book = CookDialogModelsTests.Book(variants: 4);
        string mnu = Path.Combine(dir, "Blocks.mnu");
        var status = new StatusService();
        var vm = Vm(book, mnu, status: status);

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.True(File.Exists(mnu));
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        Assert.Matches(@"^Wrote Blocks\.mnu \(\d+(\.\d)? (bytes|KB|MB)\)\.$", status.Last);

        // The same book written by the CLI's own call, rolled and rendered the same way `serve` does.
        string cli = Path.Combine(dir, "cli.mnu");
        MenuWriter.Write(book, cli, new MenuWriteOptions());
        using var fromDialog = MenuArchive.Open(mnu);
        using var fromCli = MenuArchive.Open(cli);
        Assert.Equal(MenuArtMode.Atlas, fromDialog.Book.Art.Mode);
        string dnaA = Roll(fromDialog, "serve-seed", out var a);
        string dnaB = Roll(fromCli, "serve-seed", out var b);
        Assert.Equal(dnaB, dnaA);
        Assert.Equal(b, a);
        Assert.Contains(a, x => x != 0);
    }

    [AvaloniaFact]
    public async Task Exporting_a_loose_menu_writes_one_png_per_variant()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        using var book = CookDialogModelsTests.Book(variants: 3);
        string mnu = Path.Combine(dir, "Blocks.mnu");
        var vm = Vm(book, mnu);
        vm.ChooseLooseCommand.Execute(null);

        await vm.ExportCommand.ExecuteAsync(null);

        using var zip = ZipFile.OpenRead(mnu);
        Assert.DoesNotContain(zip.Entries, e => e.FullName == "atlas.png");
        // Three variants of solid, DIFFERENT-id-but-identical-pixels art pack once per distinct
        // image in the atlas, but loose mode writes a cell per variant key.
        Assert.Equal(3, zip.Entries.Count(e => e.FullName.StartsWith("art/") && e.FullName.EndsWith(".png")));
        using var menu = MenuArchive.Open(mnu);
        Assert.Equal(MenuArtMode.Loose, menu.Book.Art.Mode);
    }

    /// <summary>A card that has already been refused: the long atlas message on its reserved line.</summary>
    internal static MenuExportDialogViewModel RefusedVm(LoadedCookBook wideBook)
    {
        var vm = Vm(wideBook, Path.Combine(Path.GetTempPath(), "Wide.mnu"));
        UiThreadWait.For(vm.ExportCommand.ExecuteAsync(null));
        return vm;
    }

    internal static LoadedCookBook WideBook()
    {
        // One variant on a canvas wider than the atlas side ceiling: a few KB of pixels, and the
        // smallest honest way to meet CheckAtlasFits without allocating a huge atlas.
        int w = SpriteSheet.MaxSide + 1;
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("body", "Body", LayerKind.Custom, null,
                new[] { new Variant("v0", "V0", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["v0"] = new Image<Rgba32>(w, 1, new Rgba32(200, 40, 40, 255)),
            },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Wide", new Dimensions(w, 1),
                new Collection("Wide", "", "WD"), new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("cat", "Cat", new[] { "body" }, Array.Empty<IncompatibilityRule>()),
                    Ingredients = new[] { ing },
                },
            },
        };
    }

    [AvaloniaFact]
    public async Task An_atlas_too_large_is_refused_in_the_card_and_points_at_loose()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        using var book = WideBook();
        string mnu = Path.Combine(dir, "Wide.mnu");
        var dialogs = new FakeDialogs();
        var vm = Vm(book, mnu, dialogs);

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.False(File.Exists(mnu));
        Assert.Empty(Directory.GetFiles(dir));
        Assert.True(vm.MessageIsProblem);
        Assert.Contains("pixel limit on a side", vm.Message);
        // The problem, then this screen's own way out - never the command-line flag.
        Assert.EndsWith("refused rather than built. Choose Loose above.", vm.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("--art", vm.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("write with", vm.Message, StringComparison.Ordinal);
        Assert.False(vm.IsRunning);
        Assert.Equal(0, vm.Progress);

        // And Loose is the way out, as the line says.
        vm.ChooseLooseCommand.Execute(null);
        Assert.Equal("", vm.Message);
        await vm.ExportCommand.ExecuteAsync(null);
        Assert.True(File.Exists(mnu));
    }

    private static byte[] Plant(string path)
    {
        var old = System.Text.Encoding.UTF8.GetBytes("the Menu that was already here");
        File.WriteAllBytes(path, old);
        return old;
    }

    [AvaloniaFact]
    public async Task A_refused_export_leaves_an_existing_menu_byte_identical()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        using var book = WideBook();
        string mnu = Path.Combine(dir, "Wide.mnu");
        var old = Plant(mnu);

        await Vm(book, mnu).ExportCommand.ExecuteAsync(null);

        Assert.Equal(old, File.ReadAllBytes(mnu));
        Assert.Equal(new[] { mnu }, Directory.GetFiles(dir));   // and no temp left beside it
    }

    [AvaloniaFact]
    public async Task A_canceled_export_leaves_an_existing_menu_byte_identical()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        using var book = CookDialogModelsTests.Book(variants: 4);
        string mnu = Path.Combine(dir, "Blocks.mnu");
        var old = Plant(mnu);
        var status = new StatusService();
        var vm = Vm(book, mnu, status: status);
        var started = new TaskCompletionSource();
        // A writer held mid-run with half a file already on disk beside the destination, as a real
        // write is; it ends only when canceled. (A real write of a small book is over before a test
        // can press Cancel.)
        vm.Writer = async (_, tmp, _, _, ct) =>
        {
            await File.WriteAllBytesAsync(tmp, new byte[] { 1, 2, 3 }, ct);
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        };

        var run = vm.ExportCommand.ExecuteAsync(null);
        await started.Task;
        Assert.True(vm.IsRunning);
        Assert.True(vm.CancelCommand.CanExecute(null));
        vm.CancelCommand.Execute(null);
        await run;

        Assert.Equal(old, File.ReadAllBytes(mnu));
        Assert.Equal(new[] { mnu }, Directory.GetFiles(dir));
        Assert.False(vm.IsRunning);
        Assert.Equal("Canceled", vm.Phase);
        Assert.Equal("", vm.Failure);                    // a cancel is not a failure
        Assert.Equal(0, vm.Progress);
        Assert.NotEqual("Wrote", status.Last?.Split(' ')[0]);   // and nothing claims success
        Assert.True(vm.CloseCommand.CanExecute(null));   // the way out is back
    }

    [AvaloniaFact]
    public async Task The_real_writer_stops_for_a_canceled_token_and_the_old_menu_survives()
    {
        // The test above holds the run open with a stand-in; this is the real MenuWriter seeing a
        // canceled token, so "Cancel works" is not only a property of the stand-in.
        string dir = Directory.CreateTempSubdirectory().FullName;
        using var book = CookDialogModelsTests.Book(variants: 4);
        string mnu = Path.Combine(dir, "Blocks.mnu");
        var old = Plant(mnu);
        var vm = Vm(book, mnu);
        vm.Writer = (b, tmp, o, p, _) => MenuWriter.WriteAsync(b, tmp, o, p, new CancellationToken(true));

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.Equal("Canceled", vm.Phase);
        Assert.Equal(old, File.ReadAllBytes(mnu));
        Assert.Equal(new[] { mnu }, Directory.GetFiles(dir));
    }

    [AvaloniaFact]
    public async Task A_progress_report_after_the_refusal_does_not_refill_the_bar()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        using var book = WideBook();
        var vm = Vm(book, Path.Combine(dir, "Wide.mnu"));
        await vm.ExportCommand.ExecuteAsync(null);
        Assert.True(vm.MessageIsProblem);
        Assert.Equal(0, vm.Progress);

        // Progress<T> posts, so this is what a report still in flight does when it lands.
        vm.OnProgress(new MenuWriteProgress(Written: 9, Total: 9, Phase: "Writing…"));

        Assert.Equal(0, vm.Progress);
        Assert.NotEqual("Writing…", vm.Phase);
    }

    [AvaloniaFact]
    public async Task A_progress_report_during_the_run_does_move_the_bar()
    {
        // The guard above must not be a bar that never moves.
        string dir = Directory.CreateTempSubdirectory().FullName;
        using var book = CookDialogModelsTests.Book(variants: 4);
        var vm = Vm(book, Path.Combine(dir, "Blocks.mnu"));
        var started = new TaskCompletionSource();
        vm.Writer = async (_, _, _, _, ct) => { started.SetResult(); await Task.Delay(Timeout.Infinite, ct); };
        var run = vm.ExportCommand.ExecuteAsync(null);
        await started.Task;
        Assert.True(vm.IsRunning);

        vm.OnProgress(new MenuWriteProgress(Written: 1, Total: 4, Phase: "Packing…"));

        Assert.Equal(0.25, vm.Progress, 3);
        Assert.Equal("Packing…", vm.Phase);
        vm.CancelCommand.Execute(null);
        await run;
    }

    private static ExplorerViewModel Explorer(LoadedCookBook book, FakeDialogs dialogs, bool unlock)
    {
        var nav = new FakeNav();
        var session = new CookBookSession();
        var vm = new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs), session,
            new FilePickerService(), ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs),
            new StatusService());
        vm.SelectedNode = vm.Root;
        if (unlock) vm.IsEditing = true;
        return vm;
    }

    [AvaloniaFact]
    public void The_button_opens_the_card()
    {
        // A real click on the real button, on a LOCKED book: the export reads and edits nothing.
        using var book = CookDialogModelsTests.Book();
        var dialogs = new FakeDialogs();
        using var explorer = Explorer(book, dialogs, unlock: false);
        Assert.False(explorer.IsEditing);
        var detail = Assert.IsType<CookBookDetailViewModel>(explorer.CurrentDetail);
        var view = new Views.CookBookDetailView { DataContext = detail };
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var button = view.GetVisualDescendants().OfType<Button>()
                .Single(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Export Menu…"));
            Assert.True(button.IsEffectivelyEnabled);
            // A real pointer press and release over the button, not a call to its command.
            var at = button.TranslatePoint(new Avalonia.Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(at, Avalonia.Input.MouseButton.Left);
            window.MouseUp(at, Avalonia.Input.MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.IsType<MenuExportDialogViewModel>(dialogs.Active);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task The_menu_export_works_on_a_locked_book()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        string cbk = Path.Combine(dir, "Blocks.cbk");
        using var book = CookDialogModelsTests.Book();
        var dialogs = new FakeDialogs();
        using var explorer = Explorer(book, dialogs, unlock: false);
        Assert.False(explorer.IsEditing);

        // Through the real seam: the card the Explorer builds for a locked book, over a session whose
        // source path is the .cbk.
        var vm = new MenuExportDialogViewModel(book, cbk, new SavePicker(null), dialogs, new StatusService());
        Assert.True(vm.ExportCommand.CanExecute(null));
        await vm.ExportCommand.ExecuteAsync(null);
        Assert.True(File.Exists(Path.Combine(dir, "Blocks.mnu")));
    }

    [AvaloniaFact]
    public async Task The_menu_export_is_disabled_on_an_invalid_book_with_a_reason()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        // Zero recipes: Validator reports a problem, MenuWriter would throw.
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Empty", new Dimensions(4, 4),
                new Collection("Empty", "", "EM"), new Dictionary<string, double>()),
            Recipes = Array.Empty<LoadedRecipe>(),
        };
        string mnu = Path.Combine(dir, "Empty.mnu");
        var vm = Vm(book, mnu);

        Assert.False(vm.ExportCommand.CanExecute(null));
        Assert.True(vm.MessageIsProblem);
        Assert.Contains("cannot be written", vm.Message);
        Assert.Contains(Validator.Validate(book)[0], vm.Message);
        // A disabled button is a label: invoking the command anyway writes nothing.
        await vm.ExportCommand.ExecuteAsync(null);
        Assert.False(File.Exists(mnu));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_menu_card_fits_the_smallest_window(bool refused)
    {
        // The tallest states: the long atlas refusal filling its reserved line, and the invalid book's
        // reason. Both must live inside the reserved room, so the card is one height.
        using var book = refused ? WideBook() : CookDialogModelsTests.Book();
        var vm = refused ? RefusedVm(book) : Vm(book, Path.Combine(Path.GetTempPath(), "x.mnu"));
        var view = new Views.MenuExportDialogView { DataContext = vm };
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
                $"the Menu card needs {needW:0} x {needH:0}; the minimum window is " +
                $"{ShellViewModel.MinWindowWidth:0} x {ShellViewModel.MinWindowHeight:0}");

            // Reserve space, toggle ink: the refused state is the same size as the quiet one.
            var message = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("msg"));
            Assert.Equal(66, message.Bounds.Height, 0.5);
            Assert.True(message.DesiredSize.Height <= message.Height + 0.5);
        }
        finally { window.Close(); }
    }
}
