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

public class RiseSaveTests
{
    /// <summary>Records what was shown and answers the leave card with a scripted choice.</summary>
    private sealed class ScriptedDialogs : IDialogService
    {
        private readonly LeaveChoice _choice;
        public ScriptedDialogs(LeaveChoice choice) => _choice = choice;
        public ViewModelBase? Shown { get; private set; }
        public int Shows { get; private set; }
        public ViewModelBase? Active => null;
        public event System.Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        {
            Shown = dialog; Shows++;
            return Task.FromResult(dialog is LeaveDialogViewModel ? (TResult?)(object)_choice : default);
        }
        public void Close(object? result) { }
    }

    private static RiseViewModel Vm(RiseSource src, FakeNav nav, IDialogService dialogs, StatusService? status = null) =>
        new(src, new ImageBridge(), nav, dialogs, new FilePickerService(), status ?? new StatusService());

    private static int OakFront(string path)
    {
        using var book = CookBookArchive.Read(path);
        var body = book.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "body");
        return body.Depths.TryGetValue("oak", out var m) ? m.Front(2, 1) : 0;
    }

    [AvaloniaFact]
    public async Task Back_with_unsaved_depth_asks_and_Stay_keeps_the_page()
    {
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        var nav = new FakeNav();
        var dialogs = new ScriptedDialogs(LeaveChoice.Stay);
        using var vm = Vm(RiseFixtures.Variant(s), nav, dialogs);
        vm.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);
        await vm.BackCommand.ExecuteAsync(null);
        var card = Assert.IsType<LeaveDialogViewModel>(dialogs.Shown);
        Assert.True(card.CanSave);
        Assert.Contains("1 variant", card.Message);
        Assert.Equal(0, nav.BackCount);
    }

    [AvaloniaFact]
    public async Task Discard_leaves_without_writing()
    {
        var (s, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        var before = File.ReadAllBytes(path);
        var nav = new FakeNav();
        using var vm = Vm(RiseFixtures.Variant(s), nav, new ScriptedDialogs(LeaveChoice.Discard));
        vm.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);
        await vm.BackCommand.ExecuteAsync(null);
        Assert.Equal(1, nav.BackCount);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [AvaloniaFact]
    public async Task Save_and_leave_writes_then_leaves()
    {
        var (s, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        var nav = new FakeNav();
        using var vm = Vm(RiseFixtures.Variant(s), nav, new ScriptedDialogs(LeaveChoice.SaveAndLeave));
        vm.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);
        await vm.BackCommand.ExecuteAsync(null);
        Assert.Equal(1, nav.BackCount);
        Assert.Equal(1, OakFront(path));
    }

    [AvaloniaFact]
    public async Task Save_and_leave_stays_when_the_save_fails_and_says_so()
    {
        var (s, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        var nav = new FakeNav();
        var dialogs = new ScriptedDialogs(LeaveChoice.SaveAndLeave);
        using var vm = Vm(RiseFixtures.Variant(s), nav, dialogs);
        vm.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);      // the write cannot land
        await vm.BackCommand.ExecuteAsync(null);
        Assert.Equal(0, nav.BackCount);
        var err = Assert.IsType<ErrorDialogViewModel>(dialogs.Shown);
        Assert.Equal("Could not save depth", err.Title);
        Assert.True(vm.HasUnsavedDepth);
        Assert.False(vm.IsSaving);
    }

    [AvaloniaFact]
    public async Task A_dismissed_leave_card_keeps_editing()
    {
        // Stay is the enum's zero, so a card closed with no answer (Esc, backdrop) is Stay.
        Assert.Equal(0, (int)LeaveChoice.Stay);
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        var nav = new FakeNav();
        using var vm = Vm(RiseFixtures.Variant(s), nav, new FakeDialogs());   // returns default
        vm.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);
        await vm.BackCommand.ExecuteAsync(null);
        Assert.Equal(0, nav.BackCount);
    }

    [AvaloniaFact]
    public async Task On_a_locked_book_Save_is_disabled_and_executing_it_writes_nothing()
    {
        var (s, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        var before = File.ReadAllBytes(path);
        using var vm = Vm(RiseFixtures.Variant(s, editing: false), new FakeNav(), new FakeDialogs());
        vm.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);
        Assert.False(vm.SaveCommand.CanExecute(null));
        await vm.SaveCommand.ExecuteAsync(null);          // the generated command does not consult CanExecute
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Contains("unlock it in the Explorer", vm.FooterText);
        Assert.True(vm.ShowSave);                          // inked: the reader sees Save is there and why it is off
    }

    [AvaloniaFact]
    public async Task A_locked_book_can_still_be_left_after_the_card_offers_no_save()
    {
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        var dialogs = new ScriptedDialogs(LeaveChoice.Discard);
        var nav = new FakeNav();
        using var vm = Vm(RiseFixtures.Variant(s, editing: false), nav, dialogs);
        vm.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);
        await vm.BackCommand.ExecuteAsync(null);
        Assert.False(((LeaveDialogViewModel)dialogs.Shown!).CanSave);
        Assert.Equal(1, nav.BackCount);
    }

    [AvaloniaFact]
    public void Undoing_back_to_the_saved_depth_is_clean()
    {
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        using var vm = Vm(RiseFixtures.Variant(s), new FakeNav(), new FakeDialogs());
        Assert.Equal("no unsaved depth", vm.FooterText);
        vm.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);
        Assert.True(vm.HasUnsavedDepth);
        Assert.Equal("unsaved depth on 1 variant", vm.FooterText);
        Assert.True(vm.SaveCommand.CanExecute(null));
        vm.UndoCommand.Execute(null);
        Assert.False(vm.HasUnsavedDepth);
        Assert.Equal("no unsaved depth", vm.FooterText);
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void A_read_only_source_leaves_without_asking_and_offers_no_save()
    {
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        var dialogs = new ScriptedDialogs(LeaveChoice.Stay);
        var nav = new FakeNav();
        using var vm = Vm(new RecipeRiseSource(s.Current!, s.Current!.Recipes[0], 1, new ImageBridge()), nav, dialogs);
        vm.BackCommand.Execute(null);
        Assert.Equal(1, nav.BackCount);
        Assert.Null(dialogs.Shown);
        Assert.False(vm.ShowSave);
        Assert.False(vm.SaveCommand.CanExecute(null));
        Assert.Equal("read-only source — nothing to save", vm.FooterText);
    }

    [AvaloniaFact]
    public async Task Save_drops_a_standing_gesture_preview_instead_of_saving_it()
    {
        var (s, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        using var vm = Vm(RiseFixtures.Variant(s), new FakeNav(), new FakeDialogs());
        vm.ApplyDepthStroke(new[] { (3, 2) }, DepthSide.Front, false);       // something to save
        vm.PreviewDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);     // a gesture still down
        Assert.Equal(1, vm.Surface.Get(2, 1).Front);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(0, vm.Surface.Get(2, 1).Front);
        Assert.Equal(0, OakFront(path));
        using var book = CookBookArchive.Read(path);
        var body = book.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "body");
        Assert.Equal(1, body.Depths["oak"].Front(3, 2));
        Assert.False(vm.HasUnsavedDepth);
    }

    [AvaloniaFact]
    public async Task Back_drops_a_standing_gesture_preview_before_deciding_whether_to_ask()
    {
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        var dialogs = new ScriptedDialogs(LeaveChoice.Stay);
        var nav = new FakeNav();
        using var vm = Vm(RiseFixtures.Variant(s), nav, dialogs);
        vm.PreviewDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);      // only a preview: nothing real to lose
        await vm.BackCommand.ExecuteAsync(null);
        Assert.Null(dialogs.Shown);
        Assert.Equal(1, nav.BackCount);
        Assert.Equal(0, vm.Surface.Get(2, 1).Front);
    }

    [AvaloniaFact]
    public async Task Saved_is_forwarded_from_the_variant_source()
    {
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        using var vm = Vm(RiseFixtures.Variant(s), new FakeNav(), new FakeDialogs());
        Nfty.Core.Formats.LoadedCookBook? got = null;
        vm.Saved += b => got = b;
        vm.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(got);
    }

    [AvaloniaFact]
    public void Arriving_on_the_page_says_its_opening_line()
    {
        var nav = new NavigationService();
        var status = new StatusService();
        var shell = new ShellViewModel(nav, new FakeDialogs(), new ThemeService(), status);
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        nav.To(new RiseViewModel(RiseFixtures.Variant(s), new ImageBridge(), nav, new FakeDialogs(),
            new FilePickerService(), status));
        Assert.StartsWith("Rising “Body / Oak”", status.Last);
        Assert.NotNull(shell);
    }

    [AvaloniaFact]
    public async Task The_leave_card_closes_with_its_choice()
    {
        var dialogs = new RecordingClose();
        var card = new LeaveDialogViewModel(dialogs, "Leave Rise?", "msg", canSave: true);
        card.DiscardCommand.Execute(null);
        card.SaveAndLeaveCommand.Execute(null);
        card.StayCommand.Execute(null);
        Assert.Equal(new object[] { LeaveChoice.Discard, LeaveChoice.SaveAndLeave, LeaveChoice.Stay }, dialogs.Results);

        var noSave = new LeaveDialogViewModel(dialogs, "t", "m", canSave: false);
        Assert.False(noSave.SaveAndLeaveCommand.CanExecute(null));
        await Task.CompletedTask;
    }

    private sealed class RecordingClose : IDialogService
    {
        public System.Collections.Generic.List<object?> Results { get; } = new();
        public ViewModelBase? Active => null;
        public event System.Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog) => Task.FromResult<TResult?>(default);
        public void Close(object? result) => Results.Add(result);
    }
}
