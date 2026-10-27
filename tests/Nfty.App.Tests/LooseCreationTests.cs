using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// "New recipe" / "New ingredient" as a loose part, from BOTH screens that offer it. The flow used
/// to be written twice and the copies drifted (only the Explorer used an open Kitchen, survived a
/// throwing picker and refused an empty id; only Landing opened what it wrote), so every fact here is
/// asserted for Landing and the Explorer side by side - a copy that drifts again fails its own row.
/// </summary>
public class LooseCreationTests
{
    public enum Screen { Landing, Explorer }
    public enum Part { Recipe, Ingredient }

    public static TheoryData<Screen, Part> Everywhere => new()
    {
        { Screen.Landing, Part.Recipe }, { Screen.Landing, Part.Ingredient },
        { Screen.Explorer, Part.Recipe }, { Screen.Explorer, Part.Ingredient },
    };

    private sealed class Picker : IFilePickerService
    {
        private readonly bool _throws;
        public string? Target { get; set; }
        public int SaveCalls { get; private set; }
        public Picker(string? save, bool throws = false) { Target = save; _throws = throws; }
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string defaultExtension)
        {
            SaveCalls++;
            if (_throws) throw new InvalidOperationException("picker blew up");
            return Task.FromResult(Target);
        }
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    /// <summary>Fills whichever wizard opens with the given name and the loose destination.</summary>
    private sealed class Dialogs : IDialogService
    {
        private readonly string _name;
        public string? ErrorTitle { get; private set; }
        public bool Confirm { get; set; } = true;
        public int Confirms { get; private set; }
        public Dialogs(string name) => _name = name;
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        {
            switch (dialog)
            {
                case NewRecipeViewModel r:
                    r.Name = _name; r.Destination = RecipeDestination.LooseKitchen;
                    return Task.FromResult((TResult?)(object?)r);
                case NewIngredientViewModel i:
                    i.Name = _name; i.Kind = LayerKind.Dynamic; i.CanvasSize = "8x8";
                    i.Destination = RecipeDestination.LooseKitchen;
                    return Task.FromResult((TResult?)(object?)i);
                case ConfirmDialogViewModel:
                    Confirms++;
                    return Task.FromResult((TResult?)(object?)Confirm);
                case ErrorDialogViewModel e:
                    ErrorTitle = e.Title; break;
            }
            return Task.FromResult(default(TResult));
        }
        public void Close(object? result) { }
    }

    private sealed class Rig : IDisposable
    {
        public FakeNav Nav = new();
        public Dialogs Dlg = default!;
        public Picker Pick = default!;
        public RecentsService Recents = default!;
        public string Dir = Directory.CreateTempSubdirectory().FullName;
        public Func<Task> Run = default!;
        public CookBookSession? Session;
        public string? BookDir;

        public void Dispose()
        {
            Session?.Dispose();
            Directory.Delete(Dir, recursive: true);
            if (BookDir is not null) Directory.Delete(BookDir, recursive: true);
        }
    }

    private static Rig Build(Screen screen, Part part, string name, Picker picker, IKitchenSession? kitchen)
    {
        var rig = new Rig { Dlg = new Dialogs(name), Pick = picker };
        rig.Recents = new RecentsService(Directory.CreateTempSubdirectory().FullName);
        var nav = rig.Nav; var dialogs = rig.Dlg;
        if (screen == Screen.Landing)
        {
            var session = new CookBookSession(); rig.Session = session;
            var vm = new LandingViewModel(nav, dialogs, picker, rig.Recents, session,
                book => new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
                    ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
                    ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
                    ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService()),
                set => new SetBrowserViewModel(set),
                ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), kitchen);
            rig.Run = part == Part.Recipe
                ? () => vm.NewRecipeCommand.ExecuteAsync(null)
                : () => vm.NewIngredientCommand.ExecuteAsync(null);
        }
        else
        {
            (var cbk, var session, _, _) = IngredientEditorSaveTests.OnDisk();
            rig.Session = session; rig.BookDir = Path.GetDirectoryName(cbk);
            Func<LoadedCookBook, ExplorerViewModel> opener = null!;
            ExplorerViewModel Make(LoadedCookBook b) => new(b, nav, dialogs, new ImageBridge(),
                ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
                ExplorerViewModelTests.CookFactory(dialogs), session, picker,
                ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService(),
                kitchen, null, null, null, opener, rig.Recents);
            opener = b => Make(b);
            var vm = Make(session.Current!);
            vm.ToggleLockCommand.Execute(null);
            rig.Run = () =>
            {
                vm.SelectNodeCommand.Execute(part == Part.Recipe ? vm.Root : vm.Root.Children[0]);
                return vm.AddCommand.ExecuteAsync(null);
            };
        }
        return rig;
    }

    private static string Ext(Part p) => p == Part.Recipe ? ".rcp" : ".igt";

    [AvaloniaTheory]
    [MemberData(nameof(Everywhere))]
    public async Task With_a_kitchen_open_both_screens_write_into_it_without_asking(Screen screen, Part part)
    {
        var (kitchen, kdir) = IngredientEditorReferencesTests.TempKitchen();
        var picker = new Picker(null, throws: true);   // would blow up if asked
        using var rig = Build(screen, part, "Hat", picker, kitchen);
        try
        {
            await rig.Run();

            Assert.True(File.Exists(Path.Combine(kdir, "hat" + Ext(part))));
            Assert.Equal(0, picker.SaveCalls);
            Assert.Null(rig.Dlg.ErrorTitle);
        }
        finally { Directory.Delete(kdir, recursive: true); }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Everywhere))]
    public async Task Without_a_kitchen_both_screens_ask_for_a_path(Screen screen, Part part)
    {
        using var rig = Build(screen, part, "Hat", new Picker(null), null);
        using var asked = Build(screen, part, "Hat", new Picker(null), null);
        var chosen = Path.Combine(asked.Dir, "chosen" + Ext(part));
        asked.Pick.Target = chosen;

        await asked.Run();

        Assert.Equal(1, asked.Pick.SaveCalls);
        Assert.True(File.Exists(chosen));                 // written exactly where the picker said

        await rig.Run();                                  // a canceled picker writes nothing, says nothing
        Assert.Equal(1, rig.Pick.SaveCalls);
        Assert.Null(rig.Dlg.ErrorTitle);
        Assert.Empty(Directory.GetFiles(rig.Dir));
    }

    [AvaloniaTheory]
    [MemberData(nameof(Everywhere))]
    public async Task A_picker_that_throws_shows_an_error_and_writes_nothing(Screen screen, Part part)
    {
        using var rig = Build(screen, part, "Hat", new Picker(null, throws: true), null);

        await rig.Run();                                  // must not escape as an exception

        Assert.Equal("Could not save", rig.Dlg.ErrorTitle);
        Assert.Empty(Directory.GetFiles(rig.Dir));
    }

    [AvaloniaTheory]
    [MemberData(nameof(Everywhere))]
    public async Task An_empty_id_is_refused_on_both_screens(Screen screen, Part part)
    {
        var (kitchen, kdir) = IngredientEditorReferencesTests.TempKitchen();
        using var rig = Build(screen, part, "", new Picker(null), kitchen);
        try
        {
            var before = Directory.GetFiles(kdir).Length;

            await rig.Run();

            Assert.NotNull(rig.Dlg.ErrorTitle);
            Assert.Equal(before, Directory.GetFiles(kdir).Length);   // nothing written, not even ".rcp"
            Assert.False(File.Exists(Path.Combine(kdir, Ext(part))));
        }
        finally { Directory.Delete(kdir, recursive: true); }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Everywhere))]
    public async Task Both_screens_open_the_new_part_after_saving(Screen screen, Part part)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "hat" + Ext(part));
        using var rig = Build(screen, part, "Hat", new Picker(path), null);
        try
        {
            await rig.Run();

            if (part == Part.Recipe)
            {
                Assert.IsType<ExplorerViewModel>(rig.Nav.Current);
                Assert.Contains(rig.Recents.Items, r => r.Path == path);   // and remembered
            }
            else
            {
                Assert.IsType<IngredientEditorViewModel>(rig.Nav.Current);
            }
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    // ---- Leaving the open book to open the new recipe -------------------------------------------
    // The Explorer's page stays on the stack under whatever it pushes, and the session holds ONE
    // book that Open() disposes. Swapping the session under a live Explorer leaves that page on
    // disposed images, with a session that no longer names its file.

    private sealed class Blank : ViewModelBase { }

    private sealed class Holder(bool allow) : ViewModelBase, IUnsavedWork
    {
        public int Asked { get; private set; }
        public bool HasUnsavedWork => true;
        public bool IsBusy => false;
        public Task<bool> ConfirmLeaveAsync() { Asked++; return Task.FromResult(allow); }
    }

    private sealed class LeaveRig : IDisposable
    {
        public NavigationService Nav = new();
        public Dialogs Dlg = new("Hat");
        public CookBookSession Session = default!;
        public ExplorerViewModel Vm = default!;
        public LoadedCookBook Original = default!;
        public StatusService Status = new();
        public string Target = Path.Combine(Directory.CreateTempSubdirectory().FullName, "hat.rcp");
        public string BookDir = default!;
        public void Dispose()
        {
            Nav.Dispose(); Session.Dispose();
            Directory.Delete(Path.GetDirectoryName(Target)!, recursive: true);
            Directory.Delete(BookDir, recursive: true);
        }
    }

    private static LeaveRig LeaveBuild(ViewModelBase? below = null)
    {
        var rig = new LeaveRig();
        (var cbk, var session, _, _) = IngredientEditorSaveTests.OnDisk();
        rig.Session = session; rig.BookDir = Path.GetDirectoryName(cbk)!; rig.Original = session.Current!;
        var nav = rig.Nav; var dialogs = rig.Dlg;
        Func<LoadedCookBook, ExplorerViewModel> opener = null!;
        ExplorerViewModel Make(LoadedCookBook b) => new(b, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session, new Picker(rig.Target),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), rig.Status,
            null, null, null, null, opener, new RecentsService(Directory.CreateTempSubdirectory().FullName));
        opener = b => Make(b);
        nav.To(new Blank());                        // Landing, the root
        if (below is not null) nav.To(below);
        rig.Vm = Make(session.Current!);
        nav.To(rig.Vm);
        rig.Vm.ToggleLockCommand.Execute(null);
        rig.Vm.SelectNodeCommand.Execute(rig.Vm.Root);
        return rig;
    }

    [AvaloniaFact]
    public async Task Opening_a_loose_recipe_from_a_book_closes_that_book_instead_of_stranding_its_page()
    {
        using var rig = LeaveBuild();

        await rig.Vm.AddCommand.ExecuteAsync(null);

        Assert.Equal(1, rig.Dlg.Confirms);                       // it asked
        var explorers = rig.Nav.Pages.OfType<ExplorerViewModel>().ToList();
        var only = Assert.Single(explorers);                      // the old book's page is gone, not buried
        Assert.Same(rig.Nav.Current, only);
        Assert.NotSame(rig.Original, rig.Session.Current);
        Assert.Same(rig.Session.Current, only.BookForTest);       // the page and the session agree
        Assert.Null(rig.Session.SourcePath);                      // a loose recipe: read-only, no .cbk
    }

    [AvaloniaFact]
    public async Task Declining_leaves_the_book_open_and_the_recipe_saved()
    {
        using var rig = LeaveBuild();
        rig.Dlg.Confirm = false;

        await rig.Vm.AddCommand.ExecuteAsync(null);

        Assert.True(File.Exists(rig.Target));                     // saved
        Assert.Same(rig.Vm, rig.Nav.Current);                     // nothing navigated
        Assert.Same(rig.Original, rig.Session.Current);           // and the book is still the session's
        Assert.NotNull(rig.Session.SourcePath);
        Assert.Contains("Saved", rig.Status.Last);
    }

    [AvaloniaFact]
    public async Task A_page_holding_unsaved_work_is_asked_first_and_can_keep_the_book()
    {
        var holder = new Holder(allow: false);
        using var rig = LeaveBuild(below: holder);

        await rig.Vm.AddCommand.ExecuteAsync(null);

        Assert.Equal(1, holder.Asked);
        Assert.Equal(0, rig.Dlg.Confirms);                        // refused before any card
        Assert.Same(rig.Vm, rig.Nav.Current);
        Assert.Same(rig.Original, rig.Session.Current);
        Assert.True(File.Exists(rig.Target));
    }
}
