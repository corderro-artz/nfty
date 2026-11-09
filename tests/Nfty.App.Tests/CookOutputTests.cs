using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Output;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The Cook dialog refuses unsafe destinations before generating or changing their files.</summary>
public class CookOutputTests
{
    private static void CaptureRefusal(ErrorDialogViewModel error)
    {
        if (Environment.GetEnvironmentVariable("NFTY_CAPTURE") is null
            || Environment.GetEnvironmentVariable("NFTY_CAPTURE_DIR") is not { } destination) return;
        Directory.CreateDirectory(destination);
        foreach (var (theme, name) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
        {
            var view = new Views.ErrorDialogView { DataContext = error };
            var area = RisePages.MinArea;
            var window = new Window { Content = view, Width = area.Width, Height = area.Height,
                RequestedThemeVariant = theme };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                var card = view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("modal"));
                Assert.True(card.Bounds.Height <= area.Height);
                using var frame = window.CaptureRenderedFrame()!;
                using var stream = File.Create(Path.Combine(destination, $"cook-output-refusal-{name}.png"));
                frame.Save(stream, PngBitmapEncoderOptions.Default);
            }
            finally { window.Close(); }
        }
    }

    private sealed class RecordingDialogs : IDialogService
    {
        public ViewModelBase? Active { get; private set; }
        public object? Result { get; private set; }
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog) { Active = dialog; return Task.FromResult<TResult?>(default); }
        public void Close(object? result) { Result = result; Active = null; }
    }

    private static LoadedCookBook NamedBook(string name)
    {
        var book = CoreTestBook.Tiny();
        return new LoadedCookBook { Manifest = book.Manifest with { Name = name }, Recipes = book.Recipes };
    }

    [Theory]
    [InlineData("Chests Demo", "chests-demo")]
    [InlineData("  Chests -- Demo!  ", "chests-demo")]
    [InlineData("../Chests\\Demo: 2026?", "chests-demo-2026")]
    [InlineData("A\tB\nC", "a-b-c")]
    [InlineData("...", "cookbook")]
    [InlineData("雪", "cookbook")]
    [InlineData("CON", "set-con")]
    [InlineData("LPT1", "set-lpt1")]
    public void CookBook_names_become_portable_lowercase_hyphenated_single_folder_names(string name, string expected)
    {
        Assert.Equal(expected, CookOutputDirectory.Slug(name));
    }

    private sealed class Picker(string? folder) : IFilePickerService
    {
        public string? LastTitle { get; private set; }
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) { LastTitle = title; return Task.FromResult(folder); }
    }

    [AvaloniaFact]
    public async Task A_nonempty_non_Set_folder_gets_a_clear_refusal_and_keeps_every_existing_file()
    {
        var temporary = Directory.CreateTempSubdirectory("nfty-cook-output-");
        try
        {
            var desktop = Directory.CreateDirectory(Path.Combine(temporary.FullName, "Desktop"));
            var note = Path.Combine(desktop.FullName, "personal-note.txt");
            await File.WriteAllTextAsync(note, "Keep this unrelated file exactly as it is.");
            var original = await File.ReadAllBytesAsync(note);
            using var book = CoreTestBook.Tiny();
            var dialogs = new FakeDialogs();
            var vm = new CookDialogViewModel(book, new Picker(desktop.FullName), new NoopFolderRevealer(), dialogs)
                { Count = 1, Seed = "output-refusal" };
            Assert.False(vm.NameFolderAfterCookBook);

            await vm.CookCommand.ExecuteAsync(null);

            Assert.False(vm.IsDone);
            var error = Assert.IsType<ErrorDialogViewModel>(dialogs.Active);
            Assert.Contains("not empty", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("folder", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("did not expect", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Name after CookBook", error.Message, StringComparison.Ordinal);
            CaptureRefusal(error);
            Assert.Equal(original, await File.ReadAllBytesAsync(note));
            Assert.Equal(["personal-note.txt"], Directory.EnumerateFileSystemEntries(desktop.FullName).Select(Path.GetFileName));
        }
        finally { temporary.Delete(true); }
    }

    [AvaloniaFact]
    public async Task Named_mode_cooks_and_packs_only_in_the_slug_child_and_extends_that_same_Set()
    {
        var temporary = Directory.CreateTempSubdirectory("nfty-cook-output-");
        try
        {
            var note = Path.Combine(temporary.FullName, "personal-note.txt");
            File.WriteAllText(note, "untouched");
            using var book = NamedBook("Chests Demo");
            var picker = new Picker(temporary.FullName);
            var dialogs = new RecordingDialogs();
            CookDialogViewModel Vm(string seed) => new(book, picker, new NoopFolderRevealer(), dialogs)
                { Count = 1, Seed = seed, NameFolderAfterCookBook = true, Pack = true };
            var first = Vm("first");
            await first.CookCommand.ExecuteAsync(null);
            Assert.True(first.IsDone);
            Assert.False(first.IsExtending);
            var child = Path.Combine(temporary.FullName, "chests-demo");
            Assert.Equal(child, first.OutputPath);
            Assert.Contains("parent", picker.LastTitle!, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(child, "chests-demo.set")));
            Assert.False(File.Exists(Path.Combine(temporary.FullName, SetLayout.ManifestFile)));
            var originalImage = File.ReadAllBytes(Path.Combine(child, "images", "0001.png"));
            first.OpenSetCommand.Execute(null);
            Assert.Equal(child, dialogs.Result);

            var second = Vm("second");
            await second.CookCommand.ExecuteAsync(null);
            Assert.True(second.IsDone);
            Assert.True(second.IsExtending);
            using var written = SetReader.Read(child);
            Assert.Equal(2, written.Items.Count);
            Assert.Equal(2, written.Items.Select(i => i.Dna).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(originalImage, File.ReadAllBytes(Path.Combine(child, "images", "0001.png")));
            Assert.Equal("untouched", File.ReadAllText(note));
            Assert.Equal(["chests-demo", "personal-note.txt"], Directory.EnumerateFileSystemEntries(temporary.FullName)
                .Select(Path.GetFileName).Order(StringComparer.Ordinal));
        }
        finally { temporary.Delete(true); }
    }

    [AvaloniaTheory]
    [InlineData("file")]
    [InlineData("nonempty-folder")]
    [InlineData("broken-set")]
    public async Task Named_mode_refuses_occupied_child_paths_without_changing_them(string collision)
    {
        var temporary = Directory.CreateTempSubdirectory("nfty-cook-output-");
        try
        {
            using var book = NamedBook("Chests Demo");
            var child = Path.Combine(temporary.FullName, "chests-demo");
            if (collision == "file") File.WriteAllText(child, "untouched");
            else
            {
                Directory.CreateDirectory(child);
                File.WriteAllText(Path.Combine(child, collision == "broken-set" ? SetLayout.ManifestFile : "note.txt"), "untouched");
            }
            var before = Directory.GetFiles(temporary.FullName, "*", SearchOption.AllDirectories)
                .ToDictionary(p => p, File.ReadAllText, StringComparer.Ordinal);
            var dialogs = new FakeDialogs();
            var vm = new CookDialogViewModel(book, new Picker(temporary.FullName), new NoopFolderRevealer(), dialogs)
                { Count = 1, NameFolderAfterCookBook = true };
            await vm.CookCommand.ExecuteAsync(null);
            Assert.False(vm.IsDone);
            var error = Assert.IsType<ErrorDialogViewModel>(dialogs.Active);
            Assert.Contains("chests-demo", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("did not expect", error.Message, StringComparison.OrdinalIgnoreCase);
            var after = Directory.GetFiles(temporary.FullName, "*", SearchOption.AllDirectories);
            Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Order(StringComparer.Ordinal));
            Assert.All(before, pair => Assert.Equal(pair.Value, File.ReadAllText(pair.Key)));
        }
        finally { temporary.Delete(true); }
    }

    [AvaloniaFact]
    public async Task Named_mode_refuses_a_linked_child_instead_of_writing_outside_the_selected_parent()
    {
        var temporary = Directory.CreateTempSubdirectory("nfty-cook-output-");
        try
        {
            var parent = Directory.CreateDirectory(Path.Combine(temporary.FullName, "parent"));
            var outside = Directory.CreateDirectory(Path.Combine(temporary.FullName, "outside"));
            var child = Path.Combine(parent.FullName, "book");
            try { Directory.CreateSymbolicLink(child, outside.FullName); }
            catch (Exception ex) when (CannotCreateLink(ex)) { Assert.Skip("This host does not permit creating a test directory symbolic link."); }
            using var book = CoreTestBook.Tiny();
            var dialogs = new FakeDialogs();
            var vm = new CookDialogViewModel(book, new Picker(parent.FullName), new NoopFolderRevealer(), dialogs)
                { Count = 1, NameFolderAfterCookBook = true };
            await vm.CookCommand.ExecuteAsync(null);
            Assert.False(vm.IsDone);
            Assert.Contains("link", Assert.IsType<ErrorDialogViewModel>(dialogs.Active).Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(outside.EnumerateFileSystemInfos());
        }
        finally { temporary.Delete(true); }
    }

    [AvaloniaFact]
    public async Task Extending_refuses_linked_Set_metadata_instead_of_regrading_an_external_file()
    {
        var temporary = Directory.CreateTempSubdirectory("nfty-cook-output-");
        try
        {
            using var book = CoreTestBook.Tiny();
            var output = Path.Combine(temporary.FullName, "set");
            var dialogs = new FakeDialogs();
            CookDialogViewModel Vm() => new(book, new Picker(output), new NoopFolderRevealer(), dialogs) { Count = 1 };
            var first = Vm();
            await first.CookCommand.ExecuteAsync(null);
            Assert.True(first.IsDone);
            var rich = Path.Combine(output, SetLayout.NftyDir, "0001.json");
            var external = Path.Combine(temporary.FullName, "external.json");
            File.Move(rich, external);
            var original = File.ReadAllText(external);
            try { File.CreateSymbolicLink(rich, external); }
            catch (Exception ex) when (CannotCreateLink(ex)) { Assert.Skip("This host does not permit creating a test file symbolic link."); }
            var second = Vm();
            await second.CookCommand.ExecuteAsync(null);
            Assert.False(second.IsDone);
            Assert.Contains("link", Assert.IsType<ErrorDialogViewModel>(dialogs.Active).Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(original, File.ReadAllText(external));
            Assert.False(File.Exists(Path.Combine(output, SetLayout.ImagesDir, "0002.png")));
        }
        finally { temporary.Delete(true); }
    }

    private sealed class DelayedPicker : IFilePickerService
    {
        internal TaskCompletionSource Entered { get; } = new();
        internal TaskCompletionSource<string?> Answer { get; } = new();
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) { Entered.SetResult(); return Answer.Task; }
    }

    private static bool CannotCreateLink(Exception exception) => exception is UnauthorizedAccessException
        || exception is IOException && (exception.HResult & 0xffff) == 1314; // ERROR_PRIVILEGE_NOT_HELD

    [AvaloniaFact]
    public async Task Naming_mode_is_captured_before_the_native_picker_await()
    {
        var temporary = Directory.CreateTempSubdirectory("nfty-cook-output-");
        try
        {
            using var book = CoreTestBook.Tiny();
            var picker = new DelayedPicker();
            var vm = new CookDialogViewModel(book, picker, new NoopFolderRevealer(), new FakeDialogs())
                { Count = 1, NameFolderAfterCookBook = true };
            var run = vm.CookCommand.ExecuteAsync(null);
            await picker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            vm.NameFolderAfterCookBook = false;
            picker.Answer.SetResult(temporary.FullName);
            await run;
            Assert.True(vm.IsDone);
            Assert.Equal(Path.Combine(temporary.FullName, "book"), vm.OutputPath);
            Assert.False(File.Exists(Path.Combine(temporary.FullName, SetLayout.ManifestFile)));
        }
        finally { temporary.Delete(true); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Canceled_picker_or_dismissal_creates_no_named_folder(bool dismiss)
    {
        var temporary = Directory.CreateTempSubdirectory("nfty-cook-output-");
        try
        {
            using var book = CoreTestBook.Tiny();
            var picker = new DelayedPicker();
            var vm = new CookDialogViewModel(book, picker, new NoopFolderRevealer(), new FakeDialogs())
                { Count = 1, NameFolderAfterCookBook = true };
            var run = vm.CookCommand.ExecuteAsync(null);
            await picker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var cancellation = dismiss ? vm.CancelAndWaitAsync() : Task.CompletedTask;
            picker.Answer.SetResult(dismiss ? temporary.FullName : null);
            await Task.WhenAll(run, cancellation);
            Assert.False(vm.IsDone);
            Assert.False(vm.IsBusy);
            Assert.Empty(temporary.EnumerateFileSystemInfos());
        }
        finally { temporary.Delete(true); }
    }

    [AvaloniaFact]
    public async Task Failed_generation_does_not_leave_an_empty_named_folder()
    {
        var temporary = Directory.CreateTempSubdirectory("nfty-cook-output-");
        try
        {
            using var book = CoreTestBook.Tiny();
            var dialogs = new FakeDialogs();
            var vm = new CookDialogViewModel(book, new Picker(temporary.FullName), new NoopFolderRevealer(), dialogs)
                { Count = 9, NameFolderAfterCookBook = true };
            await vm.CookCommand.ExecuteAsync(null);
            Assert.False(vm.IsDone);
            Assert.IsType<ErrorDialogViewModel>(dialogs.Active);
            Assert.Empty(temporary.EnumerateFileSystemInfos());
        }
        finally { temporary.Delete(true); }
    }
}
