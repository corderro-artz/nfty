using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Publish;
using Nfty.Core.Publish.Ipfs;
using Xunit;

namespace Nfty.App.Tests;

public class WorkflowRegressionTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Close_rechecks_a_new_session_writer_after_waiting_for_the_previous_one(bool windowClose)
    {
        using var session = new CookBookSession();
        session.Open(CookDialogModelsTests.Book());
        using var nav = new NavigationService();
        var dialogs = new DialogService();
        var shell = new ShellViewModel(nav, dialogs, new ThemeService(), new StatusService(), session: session);
        nav.To(new HelpViewModel(dialogs));
        var explorer = new ExplorerViewModel(session.Current!, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs), ExplorerViewModelTests.CookFactory(dialogs),
            session, new FilePickerService(), ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs),
            new StatusService());
        nav.To(explorer);
        var first = session.TryBeginWrite()!;
        Task<bool>? closingWindow = windowClose ? shell.ConfirmCloseAsync() : null;
        var closingDocument = windowClose ? Task.CompletedTask : shell.CloseDocumentCommand.ExecuteAsync(null);
        first.Dispose();
        using var second = session.TryBeginWrite();
        Assert.NotNull(second);
        await closingDocument;
        if (closingWindow is not null) Assert.False(await closingWindow);
        Assert.Same(explorer, nav.Current);
        Assert.NotNull(session.Current);
        _ = session.Current!.Recipes[0].Ingredients[0].VariantImages.Values.First()[0, 0];
    }

    [AvaloniaFact]
    public async Task Menu_close_button_reenables_after_canceled_writer_finishes()
    {
        using var book = CookDialogModelsTests.Book();
        var dialogs = new DialogService();
        var directory = Directory.CreateTempSubdirectory("nfty-menu-close-");
        string path = Path.Combine(directory.FullName, "book.cbk");
        var menu = new MenuExportDialogViewModel(book, path, new FilePickerService(), dialogs, new StatusService());
        var release = new TaskCompletionSource();
        menu.Writer = async (_, _, _, _, token) =>
        {
            await release.Task;
            token.ThrowIfCancellationRequested();
        };
        var answer = dialogs.ShowAsync<object>(menu);
        var view = new Views.MenuExportDialogView { DataContext = menu };
        var window = new Window { Content = view, Width = 1280, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Task? run = null;
        try
        {
            var close = view.GetVisualDescendants().OfType<Button>()
                .Single(b => ReferenceEquals(b.Command, menu.CloseCommand));
            Assert.True(close.IsEffectivelyEnabled);
            run = menu.ExportCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(close.IsEffectivelyEnabled);
            menu.CloseCommand.Execute(null); // execution must also honor the writer's lifetime
            Assert.Same(menu, dialogs.Active);
            var cancel = menu.CancelAndWaitAsync();
            release.SetResult();
            await run;
            await cancel;
            Dispatcher.UIThread.RunJobs();
            Assert.True(close.IsEffectivelyEnabled);
            menu.CloseCommand.Execute(null);
            await answer;
        }
        finally
        {
            release.TrySetResult();
            if (run is not null) await run;
            window.Close();
            directory.Delete(true);
        }
    }

    [AvaloniaFact]
    public async Task Cook_close_button_tracks_picker_and_command_completion()
    {
        using var book = CookDialogModelsTests.Book();
        var dialogs = new DialogService();
        var picker = new HeldPicker();
        var cook = new CookDialogViewModel(book, picker, new NoopFolderRevealer(), dialogs);
        await cook.ScanTask;
        var answer = dialogs.ShowAsync<object>(cook);
        var view = new Views.CookDialogView { DataContext = cook };
        var window = new Window { Content = view, Width = 1280, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Task? run = null;
        try
        {
            var close = view.GetVisualDescendants().OfType<Button>()
                .Single(b => ReferenceEquals(b.Command, cook.CloseCommand));
            run = cook.CookCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(close.IsEffectivelyEnabled);
            cook.CloseCommand.Execute(null);
            Assert.Same(cook, dialogs.Active);
            picker.Folder.SetResult(null);
            await run;
            Dispatcher.UIThread.RunJobs();
            Assert.True(close.IsEffectivelyEnabled);
            cook.CloseCommand.Execute(null);
            await answer;
        }
        finally { picker.Folder.TrySetResult(null); if (run is not null) await run; window.Close(); }
    }

    [Fact]
    public async Task Nested_dialog_restores_parent_and_completes_both_answers()
    {
        var dialogs = new DialogService();
        var parent = new HelpViewModel(dialogs);
        var outer = dialogs.ShowAsync<string>(parent);
        var inner = dialogs.ShowAsync<bool>(new HelpViewModel(dialogs));
        dialogs.Close(true);
        Assert.True(await inner);
        Assert.Same(parent, dialogs.Active);
        Assert.False(outer.IsCompleted);
        dialogs.Close("done");
        Assert.Equal("done", await outer);
        Assert.Null(dialogs.Active);
    }

    [AvaloniaFact]
    public async Task Chrome_cannot_replace_an_open_dialog_or_orphan_its_answer()
    {
        var dialogs = new DialogService();
        var shell = new ShellViewModel(new FakeNav(), dialogs, new ThemeService(), new StatusService());
        var card = new HelpViewModel(dialogs);
        var answer = dialogs.ShowAsync<object>(card);
        Assert.False(shell.ShowHelpCommand.CanExecute(null));
        Assert.False(shell.ShowSettingsCommand.CanExecute(null));
        shell.ShowHelpCommand.Execute(null);
        shell.ShowSettingsCommand.Execute(null);
        Assert.Same(card, dialogs.Active);
        shell.CloseDialogCommand.Execute(null);
        await answer;
        Assert.True(shell.ShowHelpCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Models_checkbox_click_changes_plan_and_exported_files()
    {
        string source = ExportDialogModelsTests.SetDir(true);
        var vm = new ExportDialogViewModel(source, new FilePickerService(), new NoopFolderRevealer(), new FakeDialogs());
        var (window, view) = ExportDialogModelsTests.Show(vm);
        try
        {
            var box = view.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content as string == "3D models");
            void Click()
            {
                typeof(CheckBox).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(box, null);
                Dispatcher.UIThread.RunJobs();
            }
            Click();
            Assert.False(vm.Options.Models);
            Assert.DoesNotContain(vm.Parts, p => p.StartsWith("3D models", StringComparison.Ordinal));
            string destination = Directory.CreateTempSubdirectory("nfty-model-toggle-").FullName;
            var result = SetExporter.Export(source, destination, vm.Options with { Shape = ExportShape.Folder });
            Assert.Empty(Directory.GetFiles(result.Path, "*.glb", SearchOption.AllDirectories));
            Click();
            Assert.True(vm.Options.Models);
            Assert.Contains(vm.Parts, p => p.StartsWith("3D models", StringComparison.Ordinal));
            string withModels = Directory.CreateTempSubdirectory("nfty-model-toggle-on-").FullName;
            var restored = SetExporter.Export(source, withModels, vm.Options with { Shape = ExportShape.Folder });
            Assert.Single(Directory.GetFiles(restored.Path, "*.glb", SearchOption.AllDirectories));
            vm.ApplyPresetCommand.Execute(vm.Presets.Single(p => p.Preset == ExportPreset.Marketplace));
            Assert.True(box.IsChecked);
        }
        finally { window.Close(); }
    }

    [Fact]
    public async Task Reentrant_changed_handler_cannot_take_another_dialogs_answer()
    {
        var dialogs = new DialogService();
        var first = new HelpViewModel(dialogs);
        var answer = dialogs.ShowAsync<string>(first);
        Task<string?>? later = null;
        dialogs.Changed += () =>
        {
            if (dialogs.Active is null)
                later = dialogs.ShowAsync<string>(new HelpViewModel(dialogs));
        };
        dialogs.Close("first");
        Assert.Equal("first", await answer);
        Assert.NotNull(later);
        Assert.False(later.IsCompleted);
        // Remove the reentrant handler's trigger by closing a captured frame while its child stays.
        var child = dialogs.ShowAsync<object>(new HelpViewModel(dialogs));
        var parent = dialogs.OpenDialogs[1];
        dialogs.Close(parent, "later");
        Assert.Equal("later", await later);
        Assert.False(child.IsCompleted);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Escape_and_scrim_keep_held_menu_until_cancellation_cleanup(bool scrim)
    {
        string dir = Directory.CreateTempSubdirectory("nfty-held-menu-").FullName;
        using var book = CookDialogModelsTests.Book();
        var dialogs = new DialogService();
        var shell = new ShellViewModel(new FakeNav(), dialogs, new ThemeService(), new StatusService());
        string path = Path.Combine(dir, "book.mnu");
        byte[] original = [8, 7, 6];
        File.WriteAllBytes(path, original);
        var menu = new MenuExportDialogViewModel(book, Path.ChangeExtension(path, ".cbk"),
            new FilePickerService(), dialogs, new StatusService());
        var started = new TaskCompletionSource();
        var canceled = new TaskCompletionSource();
        var cleanup = new TaskCompletionSource();
        menu.Writer = async (_, temporary, _, _, token) =>
        {
            await File.WriteAllBytesAsync(temporary, [1, 2], token);
            using var registration = token.Register(() => canceled.TrySetResult());
            started.SetResult();
            await cleanup.Task;
            token.ThrowIfCancellationRequested();
        };
        var answer = dialogs.ShowAsync<object>(menu);
        var run = menu.ExportCommand.ExecuteAsync(null);
        await started.Task;
        var view = new Views.ShellChromeView { DataContext = shell };
        var window = new Window { Content = view, Width = 1280, Height = 720 };
        window.KeyBindings.Add(new Avalonia.Input.KeyBinding
        {
            Gesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.Escape),
            Command = shell.CloseDialogCommand,
        });
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            if (scrim)
            {
                var panel = view.FindControl<Panel>("DialogScrim")!;
                panel.RaiseEvent(new Avalonia.Input.PointerPressedEventArgs(panel,
                    new Avalonia.Input.Pointer(1, Avalonia.Input.PointerType.Mouse, true), panel,
                    new Avalonia.Point(1, 1), 0,
                    new Avalonia.Input.PointerPointProperties(Avalonia.Input.RawInputModifiers.LeftMouseButton,
                        Avalonia.Input.PointerUpdateKind.LeftButtonPressed), Avalonia.Input.KeyModifiers.None));
            }
            else
            {
                Avalonia.Headless.HeadlessWindowExtensions.KeyPress(window, Avalonia.Input.Key.Escape,
                    Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
            }
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(menu, dialogs.Active);
            Assert.False(answer.IsCompleted);
            Assert.False(await shell.ConfirmCloseAsync());
            Assert.False(shell.CloseDialogCommand.ExecutionTask!.IsCompleted);
            cleanup.SetResult();
            await run;
            await shell.CloseDialogCommand.ExecutionTask!;
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Equal([path], Directory.GetFiles(dir));
            Assert.Same(menu, dialogs.Active);
            Assert.False(menu.IsBusy);
            shell.CloseDialogCommand.Execute(null);
            await answer;
        }
        finally
        {
            cleanup.TrySetResult();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Chrome_document_close_keeps_a_modal_borrowed_book_alive()
    {
        using var session = new CookBookSession();
        session.Open(CookDialogModelsTests.Book());
        using var nav = new NavigationService();
        var dialogs = new DialogService();
        var shell = new ShellViewModel(nav, dialogs, new ThemeService(), new StatusService(), session: session);
        nav.To(new HelpViewModel(dialogs));
        var explorer = new ExplorerViewModel(session.Current!, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs), ExplorerViewModelTests.CookFactory(dialogs),
            session, new FilePickerService(), ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs),
            new StatusService());
        nav.To(explorer);
        var image = session.Current!.Recipes[0].Ingredients[0].VariantImages.Values.First();
        var menu = new MenuExportDialogViewModel(session.Current!, null, new FilePickerService(), dialogs, new StatusService());
        var answer = dialogs.ShowAsync<object>(menu);
        var view = new Views.ShellChromeView { DataContext = shell };
        var window = new Window { Content = view, Width = 1280, Height = 720 };
        window.KeyBindings.Add(new Avalonia.Input.KeyBinding
        {
            Gesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.W, Avalonia.Input.KeyModifiers.Control),
            Command = shell.CloseDocumentCommand,
        });
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var logo = view.GetVisualDescendants().OfType<Button>()
                .Single(b => ReferenceEquals(b.Command, shell.CloseDocumentCommand));
            Assert.False(logo.IsEffectivelyEnabled);
            var point = logo.TranslatePoint(new Avalonia.Point(logo.Bounds.Width / 2, logo.Bounds.Height / 2), window)!.Value;
            Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window, point, Avalonia.Input.MouseButton.Left);
            Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window, point, Avalonia.Input.MouseButton.Left);
            Avalonia.Headless.HeadlessWindowExtensions.KeyPress(window, Avalonia.Input.Key.W,
                Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.W, "w");
            await shell.CloseDocumentCommand.ExecuteAsync(null);
            Assert.Same(explorer, nav.Current);
            Assert.NotNull(session.Current);
            _ = image[0, 0];
            Assert.False(answer.IsCompleted);
            dialogs.Close(menu, null);
            await answer;
            await shell.CloseDocumentCommand.ExecuteAsync(null);
            Assert.Null(session.Current);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Busy_finally_closes_its_own_frame_and_keeps_a_newer_child()
    {
        var dialogs = new DialogService();
        var shell = new ShellViewModel(new FakeNav(), dialogs, new ThemeService(), new StatusService());
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var job = BusyViewModel.RunAsync(dialogs, "Read", "Working", async (_, _) =>
        {
            started.SetResult();
            await release.Task;
            return 42;
        });
        await started.Task;
        var child = new HelpViewModel(dialogs);
        var childAnswer = dialogs.ShowAsync<object>(child);
        Assert.False(await shell.ConfirmCloseAsync());
        release.SetResult();
        Assert.Equal(42, await job);
        Assert.Same(child, dialogs.Active);
        Assert.False(childAnswer.IsCompleted);
        dialogs.Close(child, null);
        await childAnswer;
    }

    private sealed class HeldPicker : IFilePickerService
    {
        public TaskCompletionSource<string?> Folder { get; } = new();
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string extension) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Folder.Task;
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Dismissing_during_destination_picker_waits_and_cancels_before_any_write(bool cook)
    {
        using var book = CookDialogModelsTests.Book();
        var dialogs = new DialogService();
        var shell = new ShellViewModel(new FakeNav(), dialogs, new ThemeService(), new StatusService());
        var picker = new HeldPicker();
        var cooking = new CookDialogViewModel(book, picker, new NoopFolderRevealer(), dialogs) { Count = 1 };
        await cooking.ScanTask;
        var exporting = new ExportDialogViewModel(ExportDialogModelsTests.SetDir(false), picker, new NoopFolderRevealer(), dialogs);
        ViewModelBase card = cook ? cooking : exporting;
        var answer = dialogs.ShowAsync<object>(card);
        var run = cook ? cooking.CookCommand.ExecuteAsync(null) : exporting.ExportCommand.ExecuteAsync(null);
        Assert.False(await shell.ConfirmCloseAsync());
        var dismiss = shell.CloseDialogCommand.ExecuteAsync(null);
        Assert.False(dismiss.IsCompleted);
        string destination = Directory.CreateTempSubdirectory("nfty-canceled-picker-").FullName;
        picker.Folder.SetResult(destination);
        await run;
        await dismiss;
        Assert.Empty(Directory.GetFileSystemEntries(destination));
        Assert.False(cook ? cooking.IsDone : exporting.IsDone);
        Assert.Same(card, dialogs.Active);
        dialogs.Close(card, null);
        await answer;
    }

    private sealed class FolderPicker(string folder) : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string extension) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(folder);
    }

    private sealed class HeldUpload : IPinningService
    {
        public string Provider => "held upload";
        public TaskCompletionSource Started { get; } = new();
        public TaskCompletionSource Canceled { get; } = new();
        public TaskCompletionSource Cleanup { get; } = new();
        public Task<PinningStatus> StatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PinningStatus(true, Provider, "connected", "Ready"));
        public async Task<string> AddFolderAsync(string folder, string label, IProgress<PinProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            using var registration = cancellationToken.Register(() => Canceled.TrySetResult());
            Started.SetResult();
            await Cleanup.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return "bafyheld0000000000000000";
        }
        public Task<bool> RemoveAsync(string cid, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    [AvaloniaFact]
    public async Task Upload_dismissal_waits_for_provider_cleanup_and_preserves_existing_output()
    {
        string source = ExportDialogModelsTests.SetDir(false);
        string destination = Directory.CreateTempSubdirectory("nfty-held-upload-").FullName;
        string marker = Path.Combine(destination, "keep.txt");
        File.WriteAllText(marker, "original");
        var provider = new HeldUpload();
        var publishing = new PublishingSettingsService(StateStore.InMemory(), new InMemorySecretStore(),
            (_, _, _) => provider);
        var dialogs = new DialogService();
        var shell = new ShellViewModel(new FakeNav(), dialogs, new ThemeService(), new StatusService());
        var vm = new ExportDialogViewModel(source, new FolderPicker(destination), new NoopFolderRevealer(),
            dialogs, publishing: publishing);
        vm.ApplyPresetCommand.Execute(vm.Presets.Single(p => p.Preset == ExportPreset.Marketplace));
        await vm.PendingUploadCheck;
        var answer = dialogs.ShowAsync<object>(vm);
        var run = vm.UploadCommand.ExecuteAsync(null);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var dismiss = shell.CloseDialogCommand.ExecuteAsync(null);
        await provider.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(dismiss.IsCompleted);
        Assert.Same(vm, dialogs.Active);
        Assert.False(await shell.ConfirmCloseAsync());
        provider.Cleanup.SetResult();
        await run;
        await dismiss;
        Assert.False(vm.IsRunning);
        Assert.False(vm.IsDone);
        Assert.Equal("Canceled. Pre-existing content stays pinned.", vm.PhaseText);
        Assert.Equal("original", File.ReadAllText(marker));
        Assert.Equal([marker], Directory.GetFiles(destination));
        Assert.False(answer.IsCompleted);
        dialogs.Close(vm, null);
        await answer;
    }

    private sealed class HeldPinning : IPinningService
    {
        public string Provider => "held";
        public TaskCompletionSource<PinningStatus> Answer { get; } = new();
        public Task<PinningStatus> StatusAsync(CancellationToken cancellationToken = default) => Answer.Task;
        public Task<string> AddFolderAsync(string folder, string label, IProgress<PinProgress>? progress = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RemoveAsync(string cid, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    [AvaloniaFact]
    public async Task Editing_endpoint_rejects_old_response_and_explicit_test_checks_new_endpoint()
    {
        var services = new List<HeldPinning>();
        var publishing = new PublishingSettingsService(StateStore.InMemory(), new InMemorySecretStore(),
            (_, _, _) => { var service = new HeldPinning(); services.Add(service); return service; });
        var vm = new SettingsDialogViewModel(new FakeDialogs(),
            new ParallelismSettingService(StateStore.InMemory(), () => null), publishing: publishing);
        var old = vm.PendingCheck;
        vm.KuboApi = "http://127.0.0.1:5555";
        services[0].Answer.SetResult(new(true, "held", "old", "old endpoint"));
        await old;
        Assert.Equal(ConnectionState.Unchecked, vm.Connection);
        Assert.Equal("http://127.0.0.1:5555", publishing.KuboApi);
        var check = vm.TestConnectionCommand.ExecuteAsync(null);
        services[1].Answer.SetResult(new(true, "held", "new", "new endpoint"));
        await check;
        Assert.Equal("new endpoint", vm.StatusDetail);
        Assert.Equal(ConnectionState.Connected, vm.Connection);
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Superseded_checks_cannot_overwrite_latest_endpoint_or_provider(bool oldFirst)
    {
        var services = new List<HeldPinning>();
        var publishing = new PublishingSettingsService(StateStore.InMemory(), new InMemorySecretStore(),
            (_, _, _) => { var service = new HeldPinning(); services.Add(service); return service; });
        publishing.SavePinataKey("test-only-key");
        var vm = new SettingsDialogViewModel(new FakeDialogs(),
            new ParallelismSettingService(StateStore.InMemory(), () => null), publishing: publishing);
        var original = vm.PendingCheck;
        vm.KuboApi = "http://127.0.0.1:5555";
        vm.KuboApi = "http://127.0.0.1:5556";
        var next = vm.TestConnectionCommand.ExecuteAsync(null);
        vm.UseProviderCommand.Execute(PinningProvider.Pinata);
        var latest = vm.PendingCheck;
        if (oldFirst)
        {
            services[0].Answer.SetResult(new(true, "held", "old", "old"));
            services[1].Answer.SetResult(new(true, "held", "middle", "middle"));
            await original;
            await next;
            Assert.Equal(ConnectionState.Checking, vm.Connection);
        }
        services[2].Answer.SetResult(new(true, "held", "latest", "latest"));
        await latest;
        if (!oldFirst)
        {
            services[0].Answer.SetResult(new(true, "held", "old", "old"));
            services[1].Answer.SetResult(new(true, "held", "middle", "middle"));
            await original;
            await next;
        }
        Assert.Equal("latest", vm.StatusDetail);
        Assert.Equal("http://127.0.0.1:5556", publishing.KuboApi);
        vm.KuboApi = "not an address";
        Assert.Equal("http://127.0.0.1:5556", publishing.KuboApi);
        vm.UseProviderCommand.Execute(PinningProvider.LocalIpfs);
        Assert.NotEmpty(vm.ApiProblem);
        services[^1].Answer.SetResult(new(false, "held", "done", "done"));
        await vm.PendingCheck;
    }

    [AvaloniaFact]
    public void Mix_projection_preserves_order_ties_zero_weights_and_repeated_edits()
    {
        var vm = new NewRecipeViewModel(new FakeDialogs(),
            [("shelved", 0), ("first", 10), ("second", 10), ("third", 10), ("fourth", 10), ("fifth", 10)]);
        for (int edit = 0; edit < 3; edit++)
        {
            vm.Name = "Current " + edit;
            vm.Weight = 50;
            Assert.Equal(["Current " + edit, "shelved", "first", "second", "third", "fourth", "fifth"],
                vm.ShareRows.Select(r => r.Name));
            Assert.Equal(["Current " + edit, "first", "second", "third", "fourth"],
                vm.ShownShareRows.Select(r => r.Name));
            Assert.Equal(50, vm.ShareRows[0].Percent);
            Assert.Equal(0, vm.ShareRows[1].Percent);
            Assert.Equal("+2 more recipes", vm.MoreSharesText);
            vm.Weight = 0;
            Assert.Equal(0, vm.ShareRows[0].Percent);
        }
        var zero = new NewRecipeViewModel(new FakeDialogs(), [("shelved", 0)]) { Weight = 0 };
        Assert.Empty(zero.ShareRows);
        Assert.Empty(zero.ShownShareRows);
        Assert.Equal("", zero.MoreSharesText);
    }

    [AvaloniaFact]
    public async Task Menu_error_is_a_reachable_child_then_parent_can_retry()
    {
        using var book = CookDialogModelsTests.Book();
        string dir = Directory.CreateTempSubdirectory("nfty-menu-retry-").FullName;
        string path = Path.Combine(dir, "retry.mnu");
        var dialogs = new DialogService();
        var shell = new ShellViewModel(new FakeNav(), dialogs, new ThemeService(), new StatusService());
        var menu = new MenuExportDialogViewModel(book, Path.ChangeExtension(path, ".cbk"),
            new FilePickerService(), dialogs, new StatusService());
        var parent = dialogs.ShowAsync<string>(menu);
        var shown = new TaskCompletionSource<ErrorDialogViewModel>();
        dialogs.Changed += () =>
        {
            if (dialogs.Active is ErrorDialogViewModel error) shown.TrySetResult(error);
        };
        menu.Writer = (_, _, _, _, _) => throw new IOException("writer refused");
        var failed = menu.ExportCommand.ExecuteAsync(null);
        var child = await shown.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(parent.IsCompleted);
        Assert.False(await shell.ConfirmCloseAsync());
        child.CloseCommand.Execute(null);
        await failed.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(menu, dialogs.Active);
        menu.Writer = (b, temporary, options, progress, token) =>
            Nfty.Core.Publish.MenuWriter.WriteAsync(b, temporary, options, progress, token);
        await menu.ExportCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(path, await parent.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(File.Exists(path));
        Assert.Null(dialogs.Active);
    }

}
