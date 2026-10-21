using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>An export reads a snapshot, keeps the page inert while it runs, streams with a counted,
/// cancellable card, and a Set's page lets go of the assets it has left.</summary>
public class RiseExportSafetyTests
{
    private sealed class Picker(string? save = null, string? folder = null, Action? whileOpen = null) : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => throw new InvalidOperationException();
        public Task<string?> SaveFileAsync(string title, string defaultExtension) { whileOpen?.Invoke(); return Task.FromResult(save); }
        public Task<string?> PickFolderAsync(string title) { whileOpen?.Invoke(); return Task.FromResult(folder); }
    }

    /// <summary>Records every card shown; optionally presses Cancel on a busy card the moment it appears.
    /// A leave card is answered Discard, so a Back that got through would really leave.</summary>
    private sealed class Dialogs(bool cancelBusy = false) : IDialogService
    {
        public List<ViewModelBase> Shown { get; } = new();
        public List<BusyViewModel> Busy { get; } = new();
        public ViewModelBase? Active { get; private set; }
        public event Action? Changed;
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        {
            Shown.Add(dialog);
            Active = dialog;
            Changed?.Invoke();
            if (dialog is BusyViewModel b)
            {
                Busy.Add(b);
                if (cancelBusy) b.CancelCommand.Execute(null);
            }
            if (dialog is LeaveDialogViewModel) return Task.FromResult((TResult?)(object)LeaveChoice.Discard);
            return Task.FromResult<TResult?>(default);
        }
        public void Close(object? result) { Active = null; Changed?.Invoke(); }
    }

    private static string Temp() => Directory.CreateTempSubdirectory("nfty-rise-safe-").FullName;

    private static RiseViewModel Vm(RiseSource src, IFilePickerService picker, IDialogService? dialogs = null,
        StatusService? status = null, INavigationService? nav = null) =>
        new(src, new ImageBridge(), nav ?? new FakeNav(), dialogs ?? new FakeDialogs(), picker, status ?? new StatusService());

    private static RiseViewModel BodyVm(IFilePickerService picker, IDialogService? dialogs = null, StatusService? status = null,
        INavigationService? nav = null)
    {
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook(withPine: true));
        return Vm(RiseFixtures.Variant(s), picker, dialogs, status, nav);
    }

    private static DepthMap FlatDepth(byte front) =>
        DepthMap.FromBytes(8, 8, Enumerable.Range(0, 64).SelectMany(_ => new[] { front, (byte)0 }).ToArray());

    private static string CookedBesideBook(int count = 20)
    {
        var (s, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook(FlatDepth(2)));
        var setDir = Path.Combine(Path.GetDirectoryName(path)!, "Chest Set");
        using (var cooked = Generator.Generate(s.Current!, new GenerateOptions(count, "launch", EnforceUniqueDna: false)))
            SetWriter.Write(cooked, setDir, pack: false);
        s.Dispose();
        return setDir;
    }

    // ------------------------------------------------------------------ the snapshot

    [AvaloniaFact]
    public void A_snapshot_is_what_the_page_held_when_it_was_taken_whatever_the_page_does_after()
    {
        using var vm = BodyVm(new Picker());
        vm.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);
        var subject = vm.Subject;
        using var snapshot = vm.Source.SnapshotExport(new[] { vm.SelectedItem });

        vm.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);   // after the snapshot
        vm.Source.Dispose();                                             // and the page's picture freed
        Assert.True(subject.IsDisposed);

        var input = snapshot.Open(0, "x");
        Assert.Equal(1, input.Depth!.Front(2, 1));
        Assert.Equal(178, input.Art[2, 1].R);           // a clone: readable though the subject's art is gone
        RiseExportSnapshot.Release(input);
    }

    [AvaloniaFact]
    public async Task While_an_export_runs_the_page_is_inert_and_its_commands_refuse_even_when_executed()
    {
        var nav = new FakeNav();
        RiseViewModel? vm = null;
        RiseItem? pine = null;
        int frontBefore = -1;
        var checks = new List<bool>();
        void WhilePickerOpen()
        {
            var v = vm!;
            checks.Add(v.IsExporting);
            checks.Add(!v.IsPaintable);
            checks.Add(!v.UndoCommand.CanExecute(null));
            checks.Add(!v.BackCommand.CanExecute(null));
            checks.Add(!v.SaveCommand.CanExecute(null));
            checks.Add(!v.FillAllCommand.CanExecute(null));
            checks.Add(!v.SelectItemCommand.CanExecute(pine));
            // Executed anyway (a disabled button is a label): nothing happens.
            frontBefore = v.Surface.Get(2, 1).Front;
            v.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);
            v.UndoCommand.Execute(null);
            v.FillAllCommand.Execute(null);
            v.SelectItemCommand.Execute(pine);
            v.BackCommand.Execute(null);
        }
        vm = BodyVm(new Picker(save: Path.Combine(Temp(), "oak.glb"), whileOpen: WhilePickerOpen), new Dialogs(), nav: nav);
        using (vm)
        {
            pine = vm.Items.Single(i => i.Id == "pine");
            vm.ApplyDepthStroke(new[] { (3, 2) }, DepthSide.Front, false);     // something Undo could take back
            var oak = vm.SelectedItem;
            await vm.ExportCommand.ExecuteAsync(null);

            Assert.All(checks, Assert.True);
            Assert.Equal(frontBefore, vm.Surface.Get(2, 1).Front);
            Assert.Equal(1, vm.Surface.Get(3, 2).Front);                      // the undo did not run
            Assert.Same(oak, vm.SelectedItem);
            Assert.Equal(0, nav.BackCount);
            Assert.False(vm.IsExporting);
            Assert.True(vm.IsPaintable);
            Assert.True(vm.BackCommand.CanExecute(null));
        }
    }

    [AvaloniaFact]
    public async Task A_recipe_cannot_be_rerolled_while_its_roll_is_exported()
    {
        using var book = RiseFixtures.ChestBook();
        var src = new RecipeRiseSource(book, book.Recipes[0], seed: 7, new ImageBridge());
        RiseViewModel? vm = null;
        bool couldReroll = true;
        var dir = Temp();
        vm = Vm(src, new Picker(save: Path.Combine(dir, "roll.glb"), whileOpen: () =>
        {
            couldReroll = vm!.RerollCommand.CanExecute(null);
            vm.RerollCommand.Execute(null);
        }));
        using (vm)
        {
            await vm.ExportCommand.ExecuteAsync(null);
            Assert.False(couldReroll);
            Assert.Equal(7, src.Seed);
            Assert.True(vm.RerollCommand.CanExecute(null));
        }
    }

    [AvaloniaFact]
    public async Task Closing_the_page_during_an_export_frees_the_source_only_when_the_export_ends()
    {
        RiseViewModel? vm = null;
        RiseSubject? subject = null;
        bool freedWhileExporting = true;
        var dir = Temp();
        vm = BodyVm(new Picker(save: Path.Combine(dir, "oak.glb"), whileOpen: () =>
        {
            subject = vm!.Subject;
            vm.Dispose();                                   // the page goes while the export is underway
            freedWhileExporting = subject.IsDisposed;
        }));
        await vm.ExportCommand.ExecuteAsync(null);

        Assert.False(freedWhileExporting);
        Assert.True(subject!.IsDisposed);                   // freed once the export ended
        Assert.True(File.Exists(Path.Combine(dir, "oak.glb")));
    }

    // ------------------------------------------------------------------ a Set: the book, the stream

    [AvaloniaFact]
    public async Task A_set_export_waits_for_the_book_so_it_writes_the_depth_the_page_will_show()
    {
        var setDir = CookedBesideBook();
        using var set = SetReader.Read(setDir);
        using var gate = new ManualResetEventSlim(false);
        var src = new SetRiseSource(set, set.Items[0].Number, () => Array.Empty<string>(), new ImageBridge(),
            readBook: p => { gate.Wait(TimeSpan.FromSeconds(30)); return CookBookArchive.Read(p); });
        var dialogs = new Dialogs();
        var root = Temp();
        using var vm = Vm(src, new Picker(folder: root), dialogs);
        vm.Packed = false;
        vm.ToggleCheckAllCommand.Execute(null);
        Assert.StartsWith("Looking for", src.DepthNote);

        var exporting = vm.ExportCommand.ExecuteAsync(null);
        Assert.Equal("Looking for the CookBook that cooked this Set…", dialogs.Busy.Single().PhaseText);
        Assert.False(exporting.IsCompleted);
        gate.Set();
        await exporting;

        // Every model carries the book's depth, none is paper: a paper 8x8 slab is a fixed size, the
        // book's oak (depth 2) is not.
        var folder = Directory.GetDirectories(root).Single();
        Assert.Equal(20, Directory.GetFiles(folder).Length);
        using var paper = RiseFixtures.Picture(RiseFixtures.Oak);
        var paperBytes = Nfty.Model.ModelBatch.Build(new[] { new Nfty.Model.ModelInput("p", paper, null) },
            vm.ExportOptions).Files.Single().Bytes.Length;
        Assert.DoesNotContain(Directory.GetFiles(folder), f => new FileInfo(f).Length == paperBytes);
        Assert.StartsWith("Depth comes from", src.DepthNote);
    }

    [AvaloniaFact]
    public async Task A_batch_card_counts_models_and_ends_full()
    {
        var setDir = CookedBesideBook();
        using var set = SetReader.Read(setDir);
        var src = new SetRiseSource(set, set.Items[0].Number, () => Array.Empty<string>(), new ImageBridge());
        await src.BookSearch;
        Dispatcher.UIThread.RunJobs();
        var dialogs = new Dialogs();
        var root = Temp();
        using var vm = Vm(src, new Picker(save: Path.Combine(root, "m.zip")), dialogs);
        vm.ToggleCheckAllCommand.Execute(null);

        await vm.ExportCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();          // Progress<T> posts its last report

        var busy = dialogs.Busy.Single();
        Assert.True(busy.CanCancel);
        Assert.False(busy.IsIndeterminate);
        Assert.Equal(1.0, busy.Progress);
        Assert.Equal("Rising 20 of 20 models…", busy.PhaseText);
        using var zip = ZipFile.OpenRead(Path.Combine(root, "m.zip"));
        Assert.Equal(20, zip.Entries.Count);
    }

    [AvaloniaFact]
    public async Task Canceling_a_zip_batch_writes_no_zip_and_says_so()
    {
        var setDir = CookedBesideBook();
        using var set = SetReader.Read(setDir);
        var src = new SetRiseSource(set, set.Items[0].Number, () => Array.Empty<string>(), new ImageBridge());
        await src.BookSearch;
        Dispatcher.UIThread.RunJobs();
        var status = new StatusService();
        var root = Temp();
        using var vm = Vm(src, new Picker(save: Path.Combine(root, "m.zip")), new Dialogs(cancelBusy: true), status);
        vm.ToggleCheckAllCommand.Execute(null);

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.Empty(Directory.GetFiles(root));
        Assert.Equal("Export canceled. No zip was written.", status.Last);
        Assert.False(vm.IsExporting);
    }

    [AvaloniaFact]
    public async Task Cancel_stops_a_batch_still_waiting_for_the_book()
    {
        var setDir = CookedBesideBook();
        using var set = SetReader.Read(setDir);
        using var gate = new ManualResetEventSlim(false);
        var src = new SetRiseSource(set, set.Items[0].Number, () => Array.Empty<string>(), new ImageBridge(),
            readBook: p => { gate.Wait(TimeSpan.FromSeconds(30)); return CookBookArchive.Read(p); });
        var status = new StatusService();
        var root = Temp();
        using var vm = Vm(src, new Picker(save: Path.Combine(root, "m.zip")), new Dialogs(cancelBusy: true), status);
        vm.ToggleCheckAllCommand.Execute(null);
        try
        {
            var exporting = vm.ExportCommand.ExecuteAsync(null);
            var first = await Task.WhenAny(exporting, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.Same(exporting, first);          // stopped at once, not when the search ended
            Assert.Equal("Export canceled. No zip was written.", status.Last);
            Assert.Empty(Directory.GetFiles(root));
        }
        finally { gate.Set(); }
    }

    [AvaloniaFact]
    public async Task Cancel_stops_a_single_export_still_waiting_for_the_book()
    {
        var setDir = CookedBesideBook();
        using var set = SetReader.Read(setDir);
        using var gate = new ManualResetEventSlim(false);
        var src = new SetRiseSource(set, set.Items[0].Number, () => Array.Empty<string>(), new ImageBridge(),
            readBook: p => { gate.Wait(TimeSpan.FromSeconds(30)); return CookBookArchive.Read(p); });
        var status = new StatusService();
        var dialogs = new Dialogs(cancelBusy: true);
        var root = Temp();
        using var vm = Vm(src, new Picker(save: Path.Combine(root, "one.glb")), dialogs, status);
        Assert.Equal(1, vm.ExportCount);
        try
        {
            var exporting = vm.ExportCommand.ExecuteAsync(null);
            var first = await Task.WhenAny(exporting, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.Same(exporting, first);
            Assert.True(dialogs.Busy.Single().CanCancel);
            Assert.Equal("Export canceled. Nothing was written.", status.Last);
            Assert.Empty(Directory.GetFiles(root));
            Assert.DoesNotContain(dialogs.Shown, d => d is ErrorDialogViewModel);
        }
        finally { gate.Set(); }
    }

    [AvaloniaFact]
    public async Task A_folder_batch_canceled_before_any_model_says_none_was_written()
    {
        var setDir = CookedBesideBook();
        using var set = SetReader.Read(setDir);
        var src = new SetRiseSource(set, set.Items[0].Number, () => Array.Empty<string>(), new ImageBridge());
        await src.BookSearch;
        Dispatcher.UIThread.RunJobs();
        var status = new StatusService();
        var root = Temp();
        using var vm = Vm(src, new Picker(folder: root), new Dialogs(cancelBusy: true), status);
        vm.Packed = false;
        vm.ToggleCheckAllCommand.Execute(null);

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.Equal("Export canceled. No models were written.", status.Last);
        Assert.Empty(Directory.GetFileSystemEntries(root));
    }

    [AvaloniaFact]
    public async Task A_folder_batch_stopped_after_a_model_landed_names_the_folder_holding_it()
    {
        var setDir = CookedBesideBook();
        using var set = SetReader.Read(setDir);
        var src = new SetRiseSource(set, set.Items[0].Number, () => Array.Empty<string>(), new ImageBridge());
        await src.BookSearch;
        Dispatcher.UIThread.RunJobs();
        var status = new StatusService();
        var dialogs = new Dialogs();
        var root = Temp();
        using var vm = Vm(src, new Picker(folder: root), dialogs, status);
        vm.Packed = false;
        vm.ToggleCheckAllCommand.Execute(null);
        vm.FileWritten = _ => dialogs.Busy.Single().CancelCommand.Execute(null);   // stop after the first file

        await vm.ExportCommand.ExecuteAsync(null);

        var folder = Directory.GetDirectories(root).Single();
        Assert.Single(Directory.GetFiles(folder));
        Assert.Equal($"Export canceled. The models finished before the stop are in {folder}.", status.Last);
    }

    [Fact]
    public void A_stopped_folder_batch_names_its_folder_only_when_a_file_reached_it()
    {
        // Skipped models are done but write nothing, and the folder is created by the first file: a
        // stop after only skips must not point at a folder that may not exist.
        Assert.Equal("Export canceled. No models were written.", RiseViewModel.CanceledText(false, 0, @"C:\out"));
        Assert.Equal(@"Export canceled. The models finished before the stop are in C:\out.",
            RiseViewModel.CanceledText(false, 2, @"C:\out"));
        Assert.Equal("Export canceled. No zip was written.", RiseViewModel.CanceledText(true, 5, @"C:\m.zip"));
    }

    // ------------------------------------------------------------------ a Set lets go of what it left

    [AvaloniaFact]
    public async Task Browsing_a_set_keeps_one_asset_loaded_and_frees_every_one_it_left()
    {
        var setDir = CookedBesideBook();
        using var set = SetReader.Read(setDir);
        var src = new SetRiseSource(set, set.Items[0].Number, () => Array.Empty<string>(), new ImageBridge());
        await src.BookSearch;
        Dispatcher.UIThread.RunJobs();
        using var vm = Vm(src, new Picker());

        var seen = new List<RiseSubject> { vm.Subject };
        foreach (var item in vm.Items.Skip(1))
        {
            vm.SelectItemCommand.Execute(item);
            seen.Add(vm.Subject);
            Assert.True(vm.Items.Count(i => i.Subject is not null) <= 1);
        }

        Assert.Equal(vm.Items.Count, seen.Count);
        Assert.Equal(vm.Items.Count - 1, seen.Count(s => s.IsDisposed));
        Assert.False(vm.Subject.IsDisposed);
        // What was learned about a left asset stays on its row.
        Assert.All(vm.Items, i => Assert.True(i.IsChipShown));
    }

    [AvaloniaFact]
    public async Task A_left_asset_that_cannot_rise_stays_listed_as_2d_only()
    {
        var setDir = CookedBesideBook();
        using var set = SetReader.Read(setDir);
        using (var damaged = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(set.Items[1].ImagePath))
        {
            damaged[3, 3] = new SixLabors.ImageSharp.PixelFormats.Rgba32(200, 40, 40, 128);
            SixLabors.ImageSharp.ImageExtensions.SaveAsPng(damaged, set.Items[1].ImagePath);
        }
        var src = new SetRiseSource(set, set.Items[0].Number, () => Array.Empty<string>(), new ImageBridge());
        await src.BookSearch;
        Dispatcher.UIThread.RunJobs();
        using var vm = Vm(src, new Picker());

        var flat = vm.Items[1];
        vm.SelectItemCommand.Execute(flat);
        Assert.Single(vm.PartialAlphaRows);
        vm.SelectItemCommand.Execute(vm.Items[0]);

        Assert.Null(flat.Subject);
        Assert.Equal("1 px", vm.PartialAlphaRows.Single().CountText);
        Assert.Equal("2D only", flat.DepthChip);
        Assert.False(flat.IsRisableOrUnknown);
    }

    // ------------------------------------------------------------------ the manifest and the status

    [AvaloniaFact]
    public void Going_back_to_one_checked_model_counts_its_size_again()
    {
        using var vm = BodyVm(new Picker());
        vm.ShowExportTabCommand.Execute(null);
        var size = vm.ManifestSize;
        Assert.NotEqual("", size);

        var pine = vm.Items.Single(i => i.Id == "pine");
        pine.IsChecked = true;                  // two: a batch states no size
        Assert.Equal("", vm.ManifestSize);
        pine.IsChecked = false;                 // one again
        Assert.Equal(size, vm.ManifestSize);

        vm.SelectedItem.IsChecked = false;      // none
        vm.SelectedItem.IsChecked = true;       // the same one, ticked again
        Assert.Equal(size, vm.ManifestSize);
    }

    [AvaloniaFact]
    public async Task A_bin_the_picker_never_asked_about_is_named_when_it_is_replaced()
    {
        var dir = Temp();
        File.WriteAllBytes(Path.Combine(dir, "oak.bin"), new byte[] { 1 });
        var status = new StatusService();
        using var vm = BodyVm(new Picker(save: Path.Combine(dir, "oak.gltf")), status: status);
        vm.Format = Nfty.Model.ModelFormat.GltfWithBin;
        await vm.ExportCommand.ExecuteAsync(null);
        Assert.EndsWith(" Replaced oak.bin.", status.Last);
    }

    [AvaloniaFact]
    public async Task A_folder_batch_over_an_earlier_one_says_how_many_files_it_replaced()
    {
        var root = Temp();
        var status = new StatusService();
        using var vm = BodyVm(new Picker(folder: root), status: status);
        vm.Packed = false;
        vm.ToggleCheckAllCommand.Execute(null);
        await vm.ExportCommand.ExecuteAsync(null);
        Assert.DoesNotContain("Replaced", status.Last);
        await vm.ExportCommand.ExecuteAsync(null);
        Assert.Contains(" Replaced 2 existing files.", status.Last);
    }
}
