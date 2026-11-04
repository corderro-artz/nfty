using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Models;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Publish.Ipfs;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The Settings card's GENERAL, PUBLISHING and DATA pages, the services behind them, and the one
/// layout promise the card makes: four pages, one height, no scroll at the smallest window.
/// </summary>
public class SettingsPagesTests
{
    private static string TempDir() => Directory.CreateTempSubdirectory("nfty-settings-").FullName;

    private sealed class RecordingTheme : IThemeService
    {
        public List<ThemeChoice> Applied { get; } = new();
        public bool IsDark { get; private set; }
        public void Toggle() => IsDark = !IsDark;
        public void Apply(ThemeChoice choice) { Applied.Add(choice); IsDark = choice == ThemeChoice.Dark; }
    }

    private sealed class FakePinning(bool reachable, string summary = "connected · kubo 0.29.0") : IPinningService
    {
        public string Provider => "Fake";
        public int Checks { get; private set; }
        public Task<PinningStatus> StatusAsync(CancellationToken cancellationToken = default)
        {
            Checks++;
            return Task.FromResult(new PinningStatus(reachable, Provider, reachable ? summary : "not reachable",
                reachable ? "Connected." : "Local IPFS is not reachable at http://127.0.0.1:5001. Start IPFS Desktop or Kubo (ipfs daemon)."));
        }
        public Task<string> AddFolderAsync(string folder, string label, IProgress<PinProgress>? progress = null,
            CancellationToken cancellationToken = default) => Task.FromResult("bafyfake0000000000000000");

        public Task<bool> RemoveAsync(string cid, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private static PublishingSettingsService Publishing(IStateStore store, ISecretStore secrets, bool reachable = true) =>
        new(store, secrets, (_, _, _) => new FakePinning(reachable));

    // ------------------------------------------------------------------ settings.json

    [Fact]
    public void Every_page_keeps_its_own_key_in_one_settings_file()
    {
        var dir = TempDir();
        var store = StateStore.At(dir);
        new ParallelismSettingService(store, () => null).Set(2);
        new AppSettingsService(store).SetDefaultCanvasSide(64);
        new AppSettingsService(store).SetTheme(ThemeChoice.Dark);
        var publishing = Publishing(store, new InMemorySecretStore());
        publishing.SetProvider(PinningProvider.Pinata);
        publishing.SetKuboApi("http://127.0.0.1:5002");

        var reopened = StateStore.At(dir);
        Assert.Equal(2, new ParallelismSettingService(reopened, () => null).Saved);
        var general = new AppSettingsService(reopened);
        Assert.Equal(64, general.DefaultCanvasSide);
        Assert.Equal(ThemeChoice.Dark, general.Theme);
        var pub = Publishing(reopened, new InMemorySecretStore());
        Assert.Equal(PinningProvider.Pinata, pub.Provider);
        Assert.Equal("http://127.0.0.1:5002", pub.KuboApi);
    }

    [Fact]
    public void A_settings_file_with_nonsense_reads_as_the_defaults()
    {
        var store = StateStore.InMemory();
        store.Write(SettingsJson.FileName, """{"defaultCanvas":"huge","theme":"purple","pinningProvider":7}""");
        var general = new AppSettingsService(store);
        Assert.Equal(CanvasAdvice.DefaultSide, general.DefaultCanvasSide);
        Assert.Equal(ThemeChoice.System, general.Theme);
        Assert.Equal(PinningProvider.LocalIpfs, Publishing(store, new InMemorySecretStore()).Provider);
    }

    // ------------------------------------------------------------------ GENERAL

    [Fact]
    public void The_canvas_side_is_where_new_books_and_loose_ingredients_start()
    {
        var store = StateStore.InMemory();
        var general = new AppSettingsService(store);
        var vm = new SettingsDialogViewModel(new FakeDialogs(), new ParallelismSettingService(store, () => null), general);

        vm.CanvasSide = 64;

        Assert.Equal(64, general.DefaultCanvasSide);
        Assert.Contains("64 × 64", vm.CanvasNote, StringComparison.Ordinal);
        Assert.Equal(0, vm.LargeCanvasOpacity);
        vm.CanvasSide = 1024;
        Assert.Equal(1, vm.LargeCanvasOpacity);

        var book = new NewCookBookViewModel(new FakeDialogs(), general.DefaultCanvasSide);
        Assert.Equal(1024, book.Width);
        Assert.Equal(1024, book.Height);
        var ing = new NewIngredientViewModel(new FakeDialogs(), general.DefaultCanvasSide);
        Assert.True(ing.TryGetCanvas(out var c));
        Assert.Equal(1024, c.Width);
    }

    [Fact]
    public void The_theme_choice_applies_at_once_and_on_the_next_launch()
    {
        var store = StateStore.InMemory();
        var theme = new RecordingTheme();
        var vm = new SettingsDialogViewModel(new FakeDialogs(), new ParallelismSettingService(store, () => null),
            new AppSettingsService(store), theme);

        vm.UseThemeCommand.Execute(ThemeChoice.Dark);

        Assert.Equal([ThemeChoice.Dark], theme.Applied);
        Assert.True(vm.IsDarkTheme);

        var next = new RecordingTheme();
        _ = new ShellViewModel(new FakeNav(), new FakeDialogs(), next, new StatusService(),
            general: new AppSettingsService(store));
        Assert.Equal([ThemeChoice.Dark], next.Applied);
    }

    [Fact]
    public void The_titlebar_theme_button_is_remembered_too()
    {
        var store = StateStore.InMemory();
        var general = new AppSettingsService(store);
        var shell = new ShellViewModel(new FakeNav(), new FakeDialogs(), new RecordingTheme(), new StatusService(),
            general: general);
        shell.ToggleThemeCommand.Execute(null);
        Assert.Equal(ThemeChoice.Dark, new AppSettingsService(store).Theme);
    }

    // ------------------------------------------------------------------ PUBLISHING

    private static SettingsDialogViewModel Card(IPublishingSettings publishing) =>
        new(new FakeDialogs(), new ParallelismSettingService(StateStore.InMemory(), () => null), publishing: publishing);

    [AvaloniaFact]
    public async Task Opening_the_card_checks_the_connection_without_blocking()
    {
        var vm = Card(Publishing(StateStore.InMemory(), new InMemorySecretStore(), reachable: true));
        await vm.PendingCheck;
        Assert.Equal(ConnectionState.Connected, vm.Connection);
        Assert.True(vm.IsConnected);
        Assert.Equal("connected · kubo 0.29.0", vm.StatusSummary);
    }

    [AvaloniaFact]
    public async Task An_unreachable_node_says_what_to_start()
    {
        var vm = Card(Publishing(StateStore.InMemory(), new InMemorySecretStore(), reachable: false));
        await vm.PendingCheck;
        Assert.Equal(ConnectionState.NotReachable, vm.Connection);
        Assert.Contains("Start IPFS Desktop or Kubo", vm.StatusDetail, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Pinata_with_no_key_is_not_set_up_and_says_so()
    {
        var store = StateStore.InMemory();
        // The real factory, with Pinata chosen before the card opens: no key means no request at all.
        var publishing = new PublishingSettingsService(store, new InMemorySecretStore());
        publishing.SetProvider(PinningProvider.Pinata);
        var vm = Card(publishing);
        await vm.PendingCheck;
        Assert.Equal(ConnectionState.NotReachable, vm.Connection);
        Assert.Equal("not set up", vm.StatusSummary);
        Assert.Contains("No Pinata key is saved", vm.StatusDetail, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task A_saved_key_goes_to_the_credential_store_never_to_a_file_and_shows_masked()
    {
        var dir = TempDir();
        var store = StateStore.At(dir);
        var secrets = new InMemorySecretStore();
        var publishing = Publishing(store, secrets);
        var vm = Card(publishing);
        vm.UseProviderCommand.Execute(PinningProvider.Pinata);
        const string jwt = "eyJhbGciOiJIUzI1NiJ9.payload-that-is-secret.sig-wxyz";

        vm.PinataKeyInput = jwt;
        await vm.SaveKeyCommand.ExecuteAsync(null);

        Assert.Equal(jwt, secrets.Read(PublishingSettingsService.PinataTarget));
        Assert.Equal("", vm.PinataKeyInput);   // the box does not keep it
        Assert.Contains("••••wxyz", vm.KeyLine, StringComparison.Ordinal);
        Assert.DoesNotContain("payload", vm.KeyLine, StringComparison.Ordinal);
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            Assert.DoesNotContain("payload-that-is-secret", File.ReadAllText(file), StringComparison.Ordinal);

        Assert.True(vm.ForgetKeyCommand.CanExecute(null));
        await vm.ForgetKeyCommand.ExecuteAsync(null);
        Assert.Null(secrets.Read(PublishingSettingsService.PinataTarget));
        Assert.Contains("no key saved", vm.KeyLine, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Without_secure_storage_the_key_cannot_be_saved_and_the_card_says_why()
    {
        var dir = TempDir();
        var publishing = Publishing(StateStore.At(dir), new UnavailableSecretStore("Secure storage is unavailable: secret-tool was not found."));
        var vm = Card(publishing);
        vm.UseProviderCommand.Execute(PinningProvider.Pinata);
        vm.PinataKeyInput = "eyJ.some.key";

        Assert.False(vm.CanStoreKey);
        Assert.False(vm.SaveKeyCommand.CanExecute(null));
        Assert.Contains("secret-tool was not found", vm.KeyLine, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => publishing.SavePinataKey("eyJ.some.key"));
        Assert.DoesNotContain(Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories),
            f => File.ReadAllText(f).Contains("eyJ.some.key", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Changing_provider_checks_again()
    {
        int built = 0;
        var publishing = new PublishingSettingsService(StateStore.InMemory(), new InMemorySecretStore(),
            (_, _, _) => { built++; return new FakePinning(true); });
        publishing.SavePinataKey("eyJ.key.abcd");
        var vm = Card(publishing);
        await vm.PendingCheck;
        vm.UseProviderCommand.Execute(PinningProvider.Pinata);
        await vm.PendingCheck;
        Assert.Equal(2, built);
        Assert.True(vm.IsPinata);
    }

    [AvaloniaFact]
    public void A_bad_api_address_is_named_and_not_saved()
    {
        var publishing = Publishing(StateStore.InMemory(), new InMemorySecretStore());
        var vm = Card(publishing);
        vm.KuboApi = "127.0.0.1:5001";
        Assert.NotEqual("", vm.ApiProblem);
        Assert.Equal(KuboPinning.DefaultApi, publishing.KuboApi);
        vm.KuboApi = "http://127.0.0.1:5005";
        Assert.Equal("", vm.ApiProblem);
        Assert.Equal("http://127.0.0.1:5005", publishing.KuboApi);
        Assert.Equal(ConnectionState.Unchecked, vm.Connection);
    }

    [Fact]
    public void The_key_hint_never_shows_more_than_four_characters()
    {
        Assert.Null(PublishingSettingsService.Hint(null));
        Assert.Equal("••••", PublishingSettingsService.Hint("short"));
        Assert.Equal("••••6789", PublishingSettingsService.Hint("abcdefghij6789"));
    }

    // ------------------------------------------------------------------ DATA

    [AvaloniaFact]
    public void Clear_recents_empties_the_list_and_the_start_screen_hears_it()
    {
        var store = StateStore.At(TempDir());
        var recents = new RecentsService(store);
        recents.Add(new RecentItem("Book", "meta", Path.Combine(TempDir(), "a.cbk"), false));
        int changes = 0;
        recents.Changed += () => changes++;
        var vm = new SettingsDialogViewModel(new FakeDialogs(), new ParallelismSettingService(store, () => null),
            recents: recents, store: store);

        Assert.Equal("1 remembered", vm.RecentsText);
        vm.ClearRecentsCommand.Execute(null);

        Assert.Empty(recents.Items);
        Assert.Empty(new RecentsService(store).Items);   // persisted
        Assert.Equal(1, changes);
        Assert.False(vm.ClearRecentsCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Reset_views_puts_the_viewer_sizes_and_editor_view_back()
    {
        var store = StateStore.InMemory();
        var viewer = new ViewerWindowStateService(store);
        viewer.Save(ViewerWindowState.Default with
        {
            TwoD = new ViewerWindowEntry(1500, 900, 10, 10, true),
            Backdrop = "Color",
        });
        var editor = new EditorViewStateService(store);
        editor.Save(new EditorViewState(PixelGrid: false, GridStep: 8));
        var vm = new SettingsDialogViewModel(new FakeDialogs(), new ParallelismSettingService(store, () => null),
            viewerState: viewer, editorState: editor);

        vm.ResetViewsCommand.Execute(null);

        Assert.Equal(ViewerWindowState.DefaultTwoD, viewer.Current.TwoD);
        Assert.Equal("Color", viewer.Current.Backdrop);   // a look, not a size: kept
        Assert.Equal(new EditorViewState(), editor.Current);
        Assert.NotEqual("", vm.DataNote);
    }

    private sealed class RecordingRevealer : IFolderRevealer
    {
        public List<string> Revealed { get; } = new();
        public void Reveal(string path) => Revealed.Add(path);
    }

    [AvaloniaFact]
    public void Open_folder_reveals_the_store_and_is_dim_when_nothing_is_on_disk()
    {
        var dir = TempDir();
        var store = StateStore.At(dir);
        var revealer = new RecordingRevealer();
        var vm = new SettingsDialogViewModel(new FakeDialogs(), new ParallelismSettingService(store, () => null),
            store: store, revealer: revealer);
        vm.OpenDataFolderCommand.Execute(null);
        Assert.Equal([store.Resolution.Directory!], revealer.Revealed);

        var memory = new SettingsDialogViewModel(new FakeDialogs(),
            new ParallelismSettingService(StateStore.InMemory(), () => null), store: StateStore.InMemory(), revealer: revealer);
        Assert.False(memory.OpenDataFolderCommand.CanExecute(null));
        Assert.Contains("this session only", memory.StoreText, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ layout

    private static double ModalHeight =>
        (ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale;

    internal static FileAssociationRegistrar StaleAssociations()
    {
        var hive = new InMemoryRegistryHive();
        new FileAssociationRegistrar(hive, AssociationLayout.Windows, @"D:\Users\someone\Downloads\old copy of nfty\nfty.exe")
            .Apply(AssociationChoice.Suggested);
        return new FileAssociationRegistrar(hive, AssociationLayout.Windows, @"C:\Program Files\nfty\nfty.exe");
    }

    [AvaloniaFact]
    public void Every_page_is_the_same_height_and_fits_the_smallest_window_unscrolled()
    {
        var store = StateStore.InMemory();
        var vm = new SettingsDialogViewModel(new FakeDialogs(), new ParallelismSettingService(store, () => "2"),
            new AppSettingsService(store), null, Publishing(store, new UnavailableSecretStore(
                "Secure storage is unavailable: secret-tool was not found. Install libsecret-tools.")),
            new RecentsService(store), store, new RecordingRevealer(),
            // SYSTEM measured in its longest state: registered to a moved copy, so the detail line
            // names a path and wraps.
            associations: StaleAssociations());
        var view = new Views.SettingsDialogView { DataContext = vm };
        var window = new Window
        {
            Content = view,
            Width = ShellViewModel.MinWindowWidth / ShellViewModel.BaseScale,
            Height = ModalHeight,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var card = view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("modal"));
            var pages = view.GetVisualDescendants().OfType<Panel>().First(p => p.Classes.Contains("settingspages"));
            var heights = new Dictionary<SettingsTab, (double Card, double Want)>();
            foreach (var tab in Enum.GetValues<SettingsTab>())
            {
                vm.Tab = tab;
                Dispatcher.UIThread.RunJobs();
                var page = pages.Children.OfType<Control>().Single(c => c.IsVisible);
                page.Measure(new Size(pages.Bounds.Width, double.PositiveInfinity));
                heights[tab] = (card.Bounds.Height, page.DesiredSize.Height);
            }
            string report = string.Join(", ", heights.Select(h => $"{h.Key} card {h.Value.Card:0} page wants {h.Value.Want:0}"));
            Assert.True(heights.Values.Select(h => h.Card).Distinct().Count() == 1, report);
            Assert.True(heights.Values.All(h => h.Want <= pages.Bounds.Height + 0.5), report);
            Assert.True(card.Bounds.Height <= ModalHeight, $"{report}; modal area {ModalHeight:0}");
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<ScrollBar>(), s => s.IsVisible);
        }
        finally { window.Close(); }
    }

    // ------------------------------------------------------------------ the real store

    [Fact]
    public void Windows_credential_manager_round_trips_and_forgets()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new WindowsCredentialStore();
        string target = "nfty-test/" + Guid.NewGuid().ToString("N");
        try
        {
            Assert.Null(store.Read(target));
            store.Write(target, "eyJ.round-trip.ü");
            Assert.Equal("eyJ.round-trip.ü", store.Read(target));
            store.Write(target, "eyJ.replaced");
            Assert.Equal("eyJ.replaced", store.Read(target));
            store.Delete(target);
            Assert.Null(store.Read(target));
            store.Delete(target);   // forgetting what is not there is not an error
        }
        finally
        {
            try { store.Delete(target); } catch (InvalidOperationException) { }
        }
    }
}
