using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Nfty.Core.Demo;
using Nfty.Core.Formats;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The Cook card keeps its edges and action targets while its option/state ink changes.</summary>
public class CookDialogCleanupTests
{
    private static Rect In(Control control, Visual root) => new(
        control.TranslatePoint(default, root)!.Value, control.Bounds.Size);

    private static Image<Rgba32> Frame(Window window)
    {
        using var frame = window.CaptureRenderedFrame()!;
        using var bytes = new MemoryStream();
        frame.Save(bytes, PngBitmapEncoderOptions.Default);
        bytes.Position = 0;
        return SixLabors.ImageSharp.Image.Load<Rgba32>(bytes);
    }

    private static void Save(Image<Rgba32> frame, string name)
    {
        if (Environment.GetEnvironmentVariable("NFTY_CAPTURE") is null) return;
        var directory = Environment.GetEnvironmentVariable("NFTY_CAPTURE_DIR");
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        frame.SaveAsPng(Path.Combine(directory, name + ".png"));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Options_and_run_states_keep_the_same_card_footer_and_action_targets(bool dark)
    {
        using var book = CookDialogModelsTests.Book();
        var vm = new CookDialogViewModel(book, new FilePickerService(), new NoopFolderRevealer(), new FakeDialogs())
            { Count = 500, Seed = "vapor" }.Landed();
        var view = new Views.CookDialogView { DataContext = vm };
        var area = RisePages.MinArea;
        var window = new Window { Content = view, Width = area.Width, Height = area.Height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var controls = view.GetVisualDescendants().OfType<Control>().ToArray();
            var card = controls.OfType<Border>().First(b => b.Classes.Contains("modal"));
            var footer = controls.OfType<Border>().Single(b => b.Classes.Contains("modal-foot"));
            Button Command(object command) => controls.OfType<Button>().Single(b => ReferenceEquals(b.Command, command));
            var cardBounds = In(card, window);
            Assert.Equal(421, cardBounds.Height);
            var footerBounds = In(footer, window);
            var primaryBounds = In(Command(vm.CookCommand), window);
            var secondaryBounds = In(Command(vm.CloseCommand), window);
            using var baseline = Frame(window);
            Save(baseline, $"cook-clean-form-{(dark ? "dark" : "light")}");
            if (Environment.GetEnvironmentVariable("NFTY_CAPTURE") is not null
                && Environment.GetEnvironmentVariable("NFTY_CAPTURE_DIR") is { } captureDirectory)
            {
                object Coordinates(Rect rectangle) => new { rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height };
                File.WriteAllText(Path.Combine(captureDirectory, $"cook-clean-geometry-{(dark ? "dark" : "light")}.json"),
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        Card = Coordinates(cardBounds), Footer = Coordinates(footerBounds),
                        Primary = Coordinates(primaryBounds), Secondary = Coordinates(secondaryBounds),
                    }));
            }
            var states = new (string Name, Action Change)[]
            {
                ("named-folder", () => vm.NameFolderAfterCookBook = true),
                ("direct-folder", () => vm.NameFolderAfterCookBook = false),
                ("repeats", () => vm.Unlimited = true),
                ("models", () => vm.IncludeModels = true),
                ("options", () => vm.ShowModelOptions = true),
                ("options-closed", () => vm.ShowModelOptions = false),
                ("models-off", () => vm.IncludeModels = false),
                ("extend", () => { vm.IsExtending = true; vm.ExistingCount = 4200; }),
                ("running", () => { vm.IsRunning = true; vm.PhaseText = "Writing assets…"; vm.Progress = .4; }),
                ("done", () => { vm.IsRunning = false; vm.IsDone = true; vm.ResultText = "Cooked 500 assets."; vm.OutputPath = @"C:\Output\VaporPets"; }),
                ("warned", () => vm.ProvenanceWarning = "This Set was cooked from a different CookBook."),
            };
            foreach (var (name, change) in states)
            {
                change();
                Dispatcher.UIThread.RunJobs();
                using var frame = Frame(window);
                Save(frame, $"cook-clean-{name}-{(dark ? "dark" : "light")}");
                Assert.True(cardBounds == In(card, window), $"Card moved in {name}: {cardBounds} -> {In(card, window)}");
                Assert.Equal(footerBounds, In(footer, window));
                Assert.True(cardBounds.Bottom <= area.Height && cardBounds.Top >= 0, "The card must fit the real minimum modal area.");
                Assert.Equal(primaryBounds, In(Command(vm.OpenSetCommand), window));
                Assert.Equal(secondaryBounds, In(Command(vm.CancelCommand), window));

                // Compare the actual painted footer rule, not only the layout rectangles. State
                // captions/buttons change below it, while this full-width strip must stay exact.
                int y = (int)Math.Ceiling(footerBounds.Top);
                for (int x = (int)Math.Ceiling(cardBounds.Left + 12); x < cardBounds.Right - 12; x++)
                    Assert.Equal(baseline[x, y], frame[x, y]);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_partial_alpha_answer_does_not_move_the_card_or_footer(bool dark)
    {
        using var gate = new ManualResetEventSlim();
        using var book = CookDialogModelsTests.Book(variants: 3, alpha: 128);
        var vm = new CookDialogViewModel(book, new FilePickerService(), new NoopFolderRevealer(), new FakeDialogs(),
            scan: (_, _) =>
            {
                // The scan delegate runs on a worker; keep its test-only gate bounded.
                if (!gate.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The test did not release the partial-alpha scan.");
                return 3;
            }) { Seed = "vapor", IncludeModels = true, ShowModelOptions = true };
        var view = new Views.CookDialogView { DataContext = vm };
        var area = RisePages.MinArea;
        var window = new Window { Content = view, Width = area.Width, Height = area.Height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var card = view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("modal"));
            var footer = view.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("modal-foot"));
            var before = (In(card, window), In(footer, window));
            gate.Set();
            await vm.ScanTask;
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.BookIsTwoDOnly);
            Assert.False(vm.ShowModelOptionsSection);
            Assert.True(vm.ModelsNoteShown);
            Assert.Equal(before, (In(card, window), In(footer, window)));
            using var frame = Frame(window);
            Save(frame, $"cook-clean-2d-only-{(dark ? "dark" : "light")}");
        }
        finally { gate.Set(); await vm.ScanTask; window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Keyboard_disclosure_reaches_model_fields_and_skips_them_when_closed(bool dark)
    {
        using var book = CookDialogModelsTests.Book();
        var vm = new CookDialogViewModel(book, new FilePickerService(), new NoopFolderRevealer(), new FakeDialogs())
            { IncludeModels = true, Seed = "vapor" }.Landed();
        var view = new Views.CookDialogView { DataContext = vm };
        var area = RisePages.MinArea;
        var window = new Window { Content = view, Width = area.Width, Height = area.Height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var buttons = view.GetVisualDescendants().OfType<Button>().ToArray();
            var disclosure = buttons.Single(b => b.Command == vm.ToggleModelOptionsCommand);
            var format = view.GetVisualDescendants().OfType<ComboBox>().Single(c => ReferenceEquals(c.ItemsSource, vm.FormatChoices));
            void Stroke(PhysicalKey key)
            {
                window.KeyPressQwerty(key, RawInputModifiers.None);
                window.KeyReleaseQwerty(key, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.True(disclosure.Focus());
            Stroke(PhysicalKey.Space);
            Assert.True(vm.ShowModelOptionsSection);
            Stroke(PhysicalKey.Tab);
            Assert.Same(format, window.FocusManager!.GetFocusedElement());

            Assert.True(disclosure.Focus());
            Stroke(PhysicalKey.Space);
            Assert.False(vm.ShowModelOptionsSection);
            Assert.False(format.IsEffectivelyEnabled);
            Stroke(PhysicalKey.Tab);
            Assert.Same(buttons.Single(b => b.Command == vm.CloseCommand), window.FocusManager!.GetFocusedElement());
            Stroke(PhysicalKey.Tab);
            Assert.Same(buttons.Single(b => b.Command == vm.CookCommand), window.FocusManager.GetFocusedElement());

            var naming = view.FindControl<CheckBox>("NamedFolderCheck")!;
            Assert.True(naming.Focus());
            Stroke(PhysicalKey.Space);
            Assert.True(vm.NameFolderAfterCookBook);
            Stroke(PhysicalKey.Space);
            Assert.False(vm.NameFolderAfterCookBook);
        }
        finally { window.Close(); }
    }

    private sealed class DocumentationPicker(string folder) : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(folder);
    }

    /// <summary>Captures the real shell, embedded demo and production Cook result for the manual.
    /// Inert unless NFTY_COOK_DOC_CAPTURE_DIR names an output directory.</summary>
    [AvaloniaFact]
    public async Task Capture_documentation_cook_card_from_the_embedded_demo()
    {
        var destination = Environment.GetEnvironmentVariable("NFTY_COOK_DOC_CAPTURE_DIR");
        if (destination is null) return;
        Directory.CreateDirectory(destination);
        foreach (var (theme, name) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
        {
            var temporary = Directory.CreateTempSubdirectory("nfty-cook-docs-");
            try
            {
                var bookPath = DemoCookBook.WriteTo(temporary.FullName);
                var output = Path.Combine(temporary.FullName, "Chest Demo");
                using var provider = new ServiceCollection().AddNftyApp()
                    .AddSingleton<IStateStore>(StateStore.InMemory())
                    .AddSingleton<IRecentsService>(new RecentsService(Path.Combine(temporary.FullName, "recents")))
                    .AddSingleton<IFilePickerService>(new DocumentationPicker(output))
                    .AddSingleton<IFolderRevealer>(new NoopFolderRevealer())
                    .BuildServiceProvider();
                var book = CookBookArchive.Read(bookPath);
                provider.GetRequiredService<ICookBookSession>().Open(book, bookPath);
                using var explorer = provider.GetRequiredService<Func<LoadedCookBook, ExplorerViewModel>>()(book);
                var shell = provider.GetRequiredService<ShellViewModel>();
                provider.GetRequiredService<INavigationService>().To(explorer);
                var dialogs = provider.GetRequiredService<IDialogService>();
                var cook = provider.GetRequiredService<Func<LoadedCookBook, CookDialogViewModel>>()(book);
                cook.Count = 50;
                cook.Seed = "cook-cleanup-2.4.9";
                cook.IncludeModels = true;
                await cook.ScanTask;
                var answer = dialogs.ShowAsync<object>(cook);
                var window = new Nfty.Desktop.MainWindow { DataContext = shell, Width = 1416, Height = 792,
                    RequestedThemeVariant = theme };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Rect SaveCard(string tag)
                {
                    Dispatcher.UIThread.RunJobs();
                    var card = window.GetVisualDescendants().OfType<Views.CookDialogView>().Single()
                        .GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("modal"));
                    var origin = card.TranslatePoint(default, window)!.Value;
                    var end = card.TranslatePoint(new Avalonia.Point(card.Bounds.Width, card.Bounds.Height), window)!.Value;
                    using var full = Frame(window);
                    // The documentation convention removes the shell's 12px transparent gutter.
                    // Cropping retains every source pixel; no scaling or invented frame is used.
                    using var raw = full.Clone(context => context.Crop(new SixLabors.ImageSharp.Rectangle(12, 12, 1392, 768)));
                    raw.SaveAsPng(Path.Combine(destination, $"{tag}-{name}.png"));
                    return new Rect(origin - new Vector(12, 12), end - new Vector(12, 12));
                }
                try
                {
                    var formBounds = SaveCard("cook-dialog");
                    await cook.CookCommand.ExecuteAsync(null);
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(cook.IsDone);
                    Assert.IsNotType<ErrorDialogViewModel>(dialogs.Active);
                    using var written = SetReader.Read(output);
                    Assert.Equal(50, written.Manifest.Count);
                    Assert.Equal(50, Directory.GetFiles(Path.Combine(output, SetLayout.ImagesDir)).Length);
                    int models = Directory.GetFiles(Path.Combine(output, SetLayout.ModelsDir), "*.glb").Length;
                    Assert.Equal(50, models);
                    var doneBounds = SaveCard("cook-done");
                    Assert.Equal(formBounds, doneBounds);
                    object Bounds(Rect rectangle) => new { rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height };
                    File.WriteAllText(Path.Combine(destination, $"cook-{name}-evidence.json"),
                        System.Text.Json.JsonSerializer.Serialize(new
                        {
                            Seed = cook.Seed, Count = written.Manifest.Count, Models = models,
                            Result = cook.ResultText, Source = book.Manifest.Name,
                            Form = Bounds(formBounds), Done = Bounds(doneBounds), RawWidth = 1392, RawHeight = 768,
                        }));
                }
                finally { dialogs.Close(null); await answer; window.Close(); }
            }
            finally { temporary.Delete(true); }
        }
    }
}
