using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The window-close guard. Closing used to ask nothing: the titlebar button, Alt+F4 and the OS
/// ending the session all dropped an editor's unsaved strokes or Rise's unsaved depth on the floor.
/// The shell now asks every page on the stack that holds work, not only the one on top.
/// </summary>
public class CloseGuardTests
{
    /// <summary>Answers every leave card with a scripted choice and records what was shown.</summary>
    private sealed class Scripted(LeaveChoice choice) : IDialogService
    {
        public List<ViewModelBase> Shown { get; } = new();
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        {
            Shown.Add(dialog);
            return Task.FromResult(dialog is LeaveDialogViewModel ? (TResult?)(object)choice : default);
        }
        public void Close(object? result) { }
    }

    /// <summary>A page with no unsaved work of its own, to sit on top of the one that has.</summary>
    private sealed class PlainPage : ViewModelBase { }

    /// <summary>Runs a callback at the moment the save swaps the book in: the archive is written and
    /// the save provably still in flight.</summary>
    private sealed class HookedSession(CookBookSession inner) : ICookBookSession
    {
        public Action? OnReplace;
        public LoadedCookBook? Current => inner.Current;
        public string? SourcePath => inner.SourcePath;
        public event Action? Changed { add => inner.Changed += value; remove => inner.Changed -= value; }
        public void Open(LoadedCookBook book, string? sourcePath = null) => inner.Open(book, sourcePath);
        public void Replace(LoadedCookBook book) { inner.Replace(book); OnReplace?.Invoke(); }
        public void Close() => inner.Close();
        public void Dispose() => inner.Dispose();
    }

    private static ShellViewModel Shell(INavigationService nav, IDialogService dialogs, StatusService? status = null) =>
        new(nav, dialogs, new ThemeService(), status ?? new StatusService());

    /// <summary>An editor over a book on disk with one painted stroke, so it is dirty.</summary>
    private static (IngredientEditorViewModel Vm, string Path, CookBookSession Session) DirtyEditor(
        FakeNav nav, IDialogService dialogs)
    {
        var (path, session, recipe, ing) = IngredientEditorSaveTests.OnDisk();
        var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
            nav, session, dialogs, new FilePickerService());
        vm.ActiveTool = EditorTool.Fill;
        vm.BrushValue = 10;
        vm.ApplyToolStroke(new[] { (0, 0) });
        Assert.True(vm.IsDirty);
        return (vm, path, session);
    }

    private static void Cleanup(string path, CookBookSession session)
    {
        session.Dispose();
        try { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); } catch { }
    }

    [AvaloniaFact]
    public async Task A_clean_app_closes_without_asking()
    {
        var nav = new FakeNav();
        var dialogs = new Scripted(LeaveChoice.Stay);
        nav.To(new PlainPage());
        var shell = Shell(nav, dialogs);

        Assert.True(await shell.ConfirmCloseAsync());
        Assert.Empty(dialogs.Shown);
    }

    [AvaloniaFact]
    public async Task A_dirty_editor_asks_and_stay_keeps_the_app_open()
    {
        var nav = new FakeNav();
        var dialogs = new Scripted(LeaveChoice.Stay);
        var (vm, path, session) = DirtyEditor(nav, dialogs);
        try
        {
            nav.To(vm);
            var shell = Shell(nav, dialogs);

            Assert.False(await shell.ConfirmCloseAsync());
            Assert.IsType<LeaveDialogViewModel>(Assert.Single(dialogs.Shown));
            Assert.True(vm.IsDirty);
        }
        finally { vm.Dispose(); Cleanup(path, session); }
    }

    [AvaloniaFact]
    public async Task A_dirty_editor_discard_lets_the_app_close()
    {
        var nav = new FakeNav();
        var dialogs = new Scripted(LeaveChoice.Discard);
        var (vm, path, session) = DirtyEditor(nav, dialogs);
        try
        {
            nav.To(vm);
            var shell = Shell(nav, dialogs);
            var before = File.ReadAllBytes(path);

            Assert.True(await shell.ConfirmCloseAsync());
            Assert.Equal(before, File.ReadAllBytes(path));     // discarding writes nothing
        }
        finally { vm.Dispose(); Cleanup(path, session); }
    }

    [AvaloniaFact]
    public async Task A_dirty_editor_save_and_leave_writes_then_closes()
    {
        var nav = new FakeNav();
        var dialogs = new Scripted(LeaveChoice.SaveAndLeave);
        var (vm, path, session) = DirtyEditor(nav, dialogs);
        try
        {
            nav.To(vm);
            var shell = Shell(nav, dialogs);
            var before = File.ReadAllBytes(path);

            Assert.True(await shell.ConfirmCloseAsync());

            Assert.False(vm.IsDirty);
            Assert.NotEqual(before, File.ReadAllBytes(path));
        }
        finally { vm.Dispose(); Cleanup(path, session); }
    }

    [AvaloniaFact]
    public async Task A_dirty_page_below_the_top_is_asked_too()
    {
        var nav = new FakeNav();
        var dialogs = new Scripted(LeaveChoice.Stay);
        var (vm, path, session) = DirtyEditor(nav, dialogs);
        try
        {
            nav.To(vm);
            nav.To(new PlainPage());               // the editor is no longer the page on top
            var shell = Shell(nav, dialogs);

            Assert.False(await shell.ConfirmCloseAsync());
            Assert.IsType<LeaveDialogViewModel>(Assert.Single(dialogs.Shown));
        }
        finally { vm.Dispose(); Cleanup(path, session); }
    }

    [AvaloniaFact]
    public async Task Closing_while_a_rise_save_runs_waits_for_it_before_asking()
    {
        var (s, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        var hooked = new HookedSession(s);
        var recipe = s.Current!.Recipes.Single(r => r.Manifest.Id == "chest");
        var body = recipe.Ingredients.Single(i => i.Manifest.Id == "body");
        var source = new VariantRiseSource(recipe, body, "oak", new ImageBridge(), hooked, () => true);
        var nav = new FakeNav();
        var dialogs = new Scripted(LeaveChoice.Stay);      // a card shown here would fail the close
        using var rise = new RiseViewModel(source, new ImageBridge(), nav, dialogs,
            new FilePickerService(), new StatusService());
        nav.To(rise);
        var shell = Shell(nav, dialogs);
        try
        {
            rise.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);
            var saving = rise.SaveCommand.ExecuteAsync(null);
            Assert.False(saving.IsCompleted);

            Task<bool>? closing = null;
            int cardsWhileWriting = -1;
            bool closingDoneWhileWriting = true;
            hooked.OnReplace = () =>
            {
                // The archive is written, the save is not finished: nothing may have been asked.
                cardsWhileWriting = dialogs.Shown.Count;
                closingDoneWhileWriting = closing!.IsCompleted;
            };
            closing = shell.ConfirmCloseAsync();
            Assert.Empty(dialogs.Shown);                   // no card yet

            await saving;
            Assert.True(await closing);                    // a clean result, with no card ever shown
            Assert.Equal(0, cardsWhileWriting);
            Assert.False(closingDoneWhileWriting);
            Assert.Empty(dialogs.Shown);
            Assert.False(rise.HasUnsavedWork);
        }
        finally { s.Dispose(); try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { } }
    }

    [AvaloniaFact]
    public async Task Closing_with_a_dialog_open_and_unsaved_work_is_refused_with_a_reason()
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var (vm, path, session) = DirtyEditor(nav, dialogs);
        try
        {
            nav.To(vm);
            var status = new StatusService();
            var shell = Shell(nav, dialogs, status);
            _ = dialogs.ShowAsync<object>(new HelpViewModel(dialogs));
            Assert.True(shell.HasDialog);

            Assert.False(await shell.ConfirmCloseAsync());

            Assert.Equal("Close the open dialog first — 1 unsaved change would be lost.", status.Last);
            Assert.IsType<HelpViewModel>(dialogs.Active);   // nothing replaced the open card
        }
        finally { vm.Dispose(); Cleanup(path, session); }
    }

    [AvaloniaFact]
    public async Task The_editor_back_button_offers_save_as_well_as_discard()
    {
        var nav = new FakeNav();
        var dialogs = new Scripted(LeaveChoice.Stay);
        var (vm, path, session) = DirtyEditor(nav, dialogs);
        try
        {
            await vm.BackCommand.ExecuteAsync(null);

            var card = Assert.IsType<LeaveDialogViewModel>(Assert.Single(dialogs.Shown));
            Assert.True(card.CanSave);
            Assert.Equal(0, nav.BackCount);                // Stay keeps the page
        }
        finally { vm.Dispose(); Cleanup(path, session); }
    }

    /// <summary>An editor over a hooked session, dirty, with its save about to run.</summary>
    private static (IngredientEditorViewModel Vm, HookedSession Hooked, string Path, CookBookSession Session)
        DirtyEditorOnHook(FakeNav nav, IDialogService dialogs)
    {
        var (path, session, recipe, ing) = IngredientEditorSaveTests.OnDisk();
        var hooked = new HookedSession(session);
        var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
            nav, hooked, dialogs, new FilePickerService());
        vm.ActiveTool = EditorTool.Fill;
        vm.BrushValue = 10;
        vm.ApplyToolStroke(new[] { (0, 0) });
        return (vm, hooked, path, session);
    }

    [AvaloniaFact]
    public async Task Closing_during_an_editor_save_waits_for_it_before_asking()
    {
        var nav = new FakeNav();
        var dialogs = new Scripted(LeaveChoice.Stay);      // a card shown here would fail the close
        var (vm, hooked, path, session) = DirtyEditorOnHook(nav, dialogs);
        try
        {
            nav.To(vm);
            var shell = Shell(nav, dialogs);
            var saving = vm.SaveCommand.ExecuteAsync(null);
            Assert.False(saving.IsCompleted);

            Task<bool>? closing = null;
            int cards = -1; bool closingDone = true;
            hooked.OnReplace = () => { cards = dialogs.Shown.Count; closingDone = closing!.IsCompleted; };
            closing = shell.ConfirmCloseAsync();
            Assert.Empty(dialogs.Shown);

            await saving;
            Assert.True(await closing);
            Assert.Equal(0, cards);
            Assert.False(closingDone);
            Assert.Empty(dialogs.Shown);
        }
        finally { vm.Dispose(); Cleanup(path, session); }
    }

    [AvaloniaFact]
    public async Task Discard_never_closes_the_window_in_the_middle_of_a_write()
    {
        var nav = new FakeNav();
        var dialogs = new Scripted(LeaveChoice.Discard);
        var (vm, hooked, path, session) = DirtyEditorOnHook(nav, dialogs);
        try
        {
            nav.To(vm);
            var shell = Shell(nav, dialogs);
            var saving = vm.SaveCommand.ExecuteAsync(null);
            Task<bool>? closing = null;
            bool closedMidWrite = true;
            hooked.OnReplace = () => closedMidWrite = closing!.IsCompleted;
            closing = shell.ConfirmCloseAsync();

            await saving;
            await closing;
            Assert.False(closedMidWrite);
        }
        finally { vm.Dispose(); Cleanup(path, session); }
    }

    [AvaloniaFact]
    public async Task A_second_save_while_one_runs_joins_it_and_writes_once()
    {
        var nav = new FakeNav();
        var (vm, hooked, path, session) = DirtyEditorOnHook(nav, new Scripted(LeaveChoice.Stay));
        try
        {
            int writes = 0;
            Task? second = null;
            hooked.OnReplace = () =>
            {
                writes++;
                // The save is provably mid-flight here: asking again must join it, not start another.
                second = vm.SaveCommand.ExecuteAsync(null);
            };
            var first = vm.SaveCommand.ExecuteAsync(null);
            Assert.False(first.IsCompleted);
            var joined = vm.SaveCommand.ExecuteAsync(null);     // pressed again before the write lands

            await Task.WhenAll(first, joined);
            await second!;
            Assert.Equal(1, writes);
        }
        finally { vm.Dispose(); Cleanup(path, session); }
    }

    [AvaloniaFact]
    public async Task A_rise_export_in_progress_refuses_the_close_even_with_nothing_unsaved()
    {
        var nav = new FakeNav();
        var status = new StatusService();
        var dialogs = new Scripted(LeaveChoice.Discard);
        var (s, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        var shell = Shell(nav, dialogs, status);
        Task<bool>? attempt = null;
        var picker = new ClosingPicker(() => attempt = shell.ConfirmCloseAsync());
        using var rise = new RiseViewModel(RiseFixtures.Variant(s), new ImageBridge(), nav, dialogs, picker, status);
        nav.To(rise);
        try
        {
            Assert.False(rise.HasUnsavedWork);
            await rise.ExportCommand.ExecuteAsync(null);

            Assert.True(attempt!.IsCompleted);
            Assert.False(await attempt);
            Assert.Equal("Wait for the export to finish.", status.Last);
            Assert.Empty(dialogs.Shown);
        }
        finally { s.Dispose(); try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { } }
    }

    /// <summary>Runs a callback while the export's save dialog is open, which is while it is running.</summary>
    private sealed class ClosingPicker(Action whileOpen) : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => throw new InvalidOperationException();
        public Task<string?> SaveFileAsync(string title, string defaultExtension) { whileOpen(); return Task.FromResult<string?>(null); }
        public Task<string?> PickFolderAsync(string title) { whileOpen(); return Task.FromResult<string?>(null); }
    }

    [AvaloniaFact]
    public async Task BeginClose_remembers_the_check_it_started()
    {
        var nav = new FakeNav();
        var shell = Shell(nav, new Scripted(LeaveChoice.Stay));
        Assert.Null(shell.PendingClose);

        var asked = shell.BeginClose();

        Assert.Same(asked, shell.PendingClose);
        Assert.True(await asked);
    }
}
