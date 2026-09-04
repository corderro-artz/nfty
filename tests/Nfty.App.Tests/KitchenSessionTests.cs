using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The Kitchen as the app sees it: the open workspace, the titlebar chip that names it, and
/// the folder loose items are saved into.
///
/// Working WITHOUT a Kitchen has to stay completely normal — a CookBook opened from anywhere on disk
/// never needed a workspace and still does not — so several of these assert the null case as a
/// first-class state rather than an error.</summary>
public class KitchenSessionTests
{
    private sealed class StubTheme : IThemeService
    { public bool IsDark { get; private set; } public void Toggle() => IsDark = !IsDark; }

    private sealed class SavePicker : IFilePickerService
    {
        private readonly string? _save;
        public int SaveCalls { get; private set; }
        public SavePicker(string? save) => _save = save;
        public Task<string?> OpenFileAsync(string t, params string[] e) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string t, string e) { SaveCalls++; return Task.FromResult(_save); }
        public Task<string?> PickFolderAsync(string t) => Task.FromResult<string?>(null);
    }

    private static string MakeKitchen(string dir, string name = "VaporStudio")
    {
        var path = Path.Combine(dir, name + Kitchen.Extension);
        Kitchen.Create(path, new KitchenManifest(name.ToLowerInvariant(), name));
        return path;
    }

    // ---- the session -----------------------------------------------------------------------------

    [Fact]
    public void No_kitchen_is_open_until_one_is_opened()
    {
        var session = new KitchenSession();
        Assert.Null(session.Current);
        Assert.Null(session.Path);
    }

    [Fact]
    public void Opening_a_kitchen_publishes_it_and_raises_changed()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = MakeKitchen(dir);
        var session = new KitchenSession();
        var raised = 0;
        session.Changed += () => raised++;

        session.Open(path);

        Assert.Equal("VaporStudio", session.Current!.Manifest.Name);
        Assert.Equal(path, session.Path);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Opening_a_second_kitchen_replaces_the_first()
    {
        // "changes only when you close this Kitchen and open another" — one at a time, by design.
        var a = MakeKitchen(Directory.CreateTempSubdirectory().FullName, "One");
        var b = MakeKitchen(Directory.CreateTempSubdirectory().FullName, "Two");
        var session = new KitchenSession();

        session.Open(a);
        session.Open(b);

        Assert.Equal("Two", session.Current!.Manifest.Name);
        Assert.Equal(b, session.Path);
    }

    [Fact]
    public void Closing_clears_it()
    {
        var session = new KitchenSession();
        session.Open(MakeKitchen(Directory.CreateTempSubdirectory().FullName));
        session.Close();

        Assert.Null(session.Current);
        Assert.Null(session.Path);
    }

    [Fact]
    public void Refresh_picks_up_a_file_added_since_open()
    {
        // Membership is discovered, not recorded, so anything writing into the workspace refreshes
        // rather than trying to keep a list in step.
        var dir = Directory.CreateTempSubdirectory().FullName;
        var session = new KitchenSession();
        session.Open(MakeKitchen(dir));
        Assert.True(session.Current!.IsEmpty);

        File.Copy(session.Path!, Path.Combine(dir, "copy.cbk"));   // any real .cbk-named archive
        session.Refresh();

        Assert.False(session.Current!.IsEmpty);
    }

    [Fact]
    public void Refresh_without_an_open_kitchen_is_a_no_op_rather_than_a_throw()
    {
        var session = new KitchenSession();
        session.Refresh();          // must not throw
        Assert.Null(session.Current);
    }

    // ---- the titlebar chip -----------------------------------------------------------------------

    [AvaloniaFact]
    public void The_chip_hides_when_no_kitchen_is_open()
    {
        var shell = new ShellViewModel(new FakeNav(), new FakeDialogs(),
            new StubTheme(), new StatusService(), new KitchenSession());

        Assert.False(shell.HasKitchen);
        Assert.Null(shell.KitchenName);
    }

    [AvaloniaFact]
    public void The_chip_names_the_open_kitchen_and_updates_when_it_changes()
    {
        var session = new KitchenSession();
        var shell = new ShellViewModel(new FakeNav(), new FakeDialogs(),
            new StubTheme(), new StatusService(), session);

        var changes = 0;
        shell.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(shell.KitchenName)) changes++; };

        session.Open(MakeKitchen(Directory.CreateTempSubdirectory().FullName, "VaporStudio"));

        Assert.True(shell.HasKitchen);
        Assert.Equal("VaporStudio", shell.KitchenName);
        Assert.True(changes > 0);   // the chip actually repaints

        session.Close();
        Assert.False(shell.HasKitchen);
    }

    // ---- loose items go into the open Kitchen ----------------------------------------------------

    private sealed class LooseRecipeDialogs : IDialogService
    {
        public string? ErrorTitle { get; private set; }
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        {
            if (dialog is NewRecipeViewModel w)
            {
                w.Name = "Fox";
                w.Destination = RecipeDestination.LooseKitchen;
                return Task.FromResult((TResult?)(object?)w);
            }
            if (dialog is ErrorDialogViewModel e) { ErrorTitle = e.Title; return Task.FromResult(default(TResult)); }
            return Task.FromResult(default(TResult));
        }
        public void Close(object? result) { }
    }

    private static ExplorerViewModel Explorer(IDialogService dialogs, IFilePickerService picker,
        IKitchenSession? kitchen, out CookBookSession session, out string cbkPath)
    {
        (cbkPath, session, _, _) = IngredientEditorSaveTests.OnDisk();
        var nav = new FakeNav();
        return new ExplorerViewModel(session.Current!, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session, picker,
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService(), kitchen);
    }

    /// <summary>The whole point of the concept: the Kitchen IS the loose-items folder, so a loose
    /// save lands in it without a second question.</summary>
    [AvaloniaFact]
    public async Task A_loose_recipe_is_saved_into_the_open_kitchen_without_asking()
    {
        var kdir = Directory.CreateTempSubdirectory().FullName;
        var kitchen = new KitchenSession();
        kitchen.Open(MakeKitchen(kdir));

        var picker = new SavePicker(null);   // if it asks, there is nowhere to save and this fails
        var dialogs = new LooseRecipeDialogs();
        var vm = Explorer(dialogs, picker, kitchen, out var session, out var cbkPath);
        try
        {
            vm.ToggleLockCommand.Execute(null);
            vm.SelectNodeCommand.Execute(vm.Root);
            await vm.AddCommand.ExecuteAsync(null);

            Assert.Equal(0, picker.SaveCalls);                       // never asked
            Assert.True(File.Exists(Path.Combine(kdir, "fox.rcp"))); // landed in the workspace
            Assert.Null(dialogs.ErrorTitle);
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(cbkPath)!, true); Directory.Delete(kdir, true); }
    }

    [AvaloniaFact]
    public async Task Without_a_kitchen_it_still_asks_where_to_save()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var target = Path.Combine(dir, "fox.rcp");
        var picker = new SavePicker(target);
        var vm = Explorer(new LooseRecipeDialogs(), picker, kitchen: null, out var session, out var cbkPath);
        try
        {
            vm.ToggleLockCommand.Execute(null);
            vm.SelectNodeCommand.Execute(vm.Root);
            await vm.AddCommand.ExecuteAsync(null);

            Assert.Equal(1, picker.SaveCalls);   // no workspace, so it must ask
            Assert.True(File.Exists(target));
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(cbkPath)!, true); Directory.Delete(dir, true); }
    }

    /// <summary>Silently replacing something already in the workspace is not a save, it is a
    /// deletion — so a name collision falls back to the picker where the user can see it.</summary>
    [AvaloniaFact]
    public async Task A_name_collision_in_the_kitchen_falls_back_to_asking()
    {
        var kdir = Directory.CreateTempSubdirectory().FullName;
        var kitchen = new KitchenSession();
        kitchen.Open(MakeKitchen(kdir));

        var occupied = Path.Combine(kdir, "fox.rcp");
        File.WriteAllText(occupied, "already here");
        var before = File.ReadAllText(occupied);

        var elsewhere = Path.Combine(Directory.CreateTempSubdirectory().FullName, "fox.rcp");
        var picker = new SavePicker(elsewhere);
        var vm = Explorer(new LooseRecipeDialogs(), picker, kitchen, out var session, out var cbkPath);
        try
        {
            vm.ToggleLockCommand.Execute(null);
            vm.SelectNodeCommand.Execute(vm.Root);
            await vm.AddCommand.ExecuteAsync(null);

            Assert.Equal(1, picker.SaveCalls);                  // it asked rather than clobbering
            Assert.Equal(before, File.ReadAllText(occupied));   // and the original is untouched
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(cbkPath)!, true); Directory.Delete(kdir, true); }
    }
}
