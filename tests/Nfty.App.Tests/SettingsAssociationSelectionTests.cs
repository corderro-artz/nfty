using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class SettingsAssociationSelectionTests
{
    private const string Exe = @"C:\nfty test\Nfty.Desktop.exe";

    private static FileAssociationRegistrar Registrar(InMemoryRegistryHive hive) =>
        new(hive, AssociationLayout.Windows, Exe, launch: _ => { });

    private static SettingsDialogViewModel Card(IFileAssociations associations) =>
        new(new DialogService(), new ParallelismSettingService(StateStore.InMemory(), () => null),
            associations: associations) { Tab = SettingsTab.System };

    [Fact]
    public void Native_composition_returns_only_the_unsupported_backend_off_Windows()
    {
        // Creating the Windows service is lazy: no inspection, launch, or registry write occurs.
        var service = FileAssociations.ForThisMachine(Exe);
        if (OperatingSystem.IsWindows()) Assert.IsType<FileAssociationRegistrar>(service);
        else Assert.IsType<UnsupportedFileAssociations>(service);
        Assert.Equal(OperatingSystem.IsWindows(), service.IsSupported);
    }

    [AvaloniaFact]
    public async Task Unsupported_platform_hides_the_system_tab_and_never_calls_native_operations()
    {
        var vm = new SettingsDialogViewModel(new DialogService(),
            new ParallelismSettingService(StateStore.InMemory(), () => null), associations: new NoNativeCalls());
        var view = new Views.SettingsDialogView { DataContext = vm };
        var window = new Window { Content = view, Width = 1067, Height = 526 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.False(vm.ShowsSystemTab);
            Assert.False(view.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "SYSTEM").IsVisible);
            vm.AllTypesSelected = true;
            Assert.False(vm.ApplyAssociationsCommand.CanExecute(null));
            Assert.False(vm.RemoveAssociationsCommand.CanExecute(null));
            Assert.False(vm.OpenDefaultAppsCommand.CanExecute(null));
            await vm.ApplyAssociationsCommand.ExecuteAsync(null);
            await vm.RemoveAssociationsCommand.ExecuteAsync(null);
            await vm.OpenDefaultAppsCommand.ExecuteAsync(null);
            vm.RefreshAssociationsCommand.Execute(null);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Every_supported_extension_can_be_selected_independently()
    {
        var vm = Card(Registrar(new InMemoryRegistryHive()));
        var view = new Views.SettingsDialogView { DataContext = vm };
        var window = new Window { Content = view, Width = 1067, Height = 526 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var choices = view.GetVisualDescendants().OfType<CheckBox>()
                .Select(c => c.Content?.ToString()).Where(c => c?.StartsWith('.') == true)
                .ToHashSet(StringComparer.Ordinal);
            Assert.Equal(FileTypeCatalog.All.Select(t => t.Extension).Order(StringComparer.Ordinal),
                choices.Order(StringComparer.Ordinal));
            Assert.Contains(view.GetVisualDescendants().OfType<CheckBox>(), c => c.Content?.ToString() == "All");
        }
        finally { window.Close(); }
    }

    [Fact]
    public void Registration_does_not_claim_to_be_the_effective_default()
    {
        var registrar = Registrar(new InMemoryRegistryHive());
        registrar.Apply(new AssociationChoice(AssociationTarget.App, AssociationTarget.Viewer, AssociationTarget.Viewer));

        var status = registrar.Inspect();

        Assert.Equal(AssociationState.Registered, status.State);
        Assert.Contains("registered", status.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("opens with", status.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void Registering_one_picture_does_not_register_its_shared_ProgId_siblings()
    {
        var hive = new InMemoryRegistryHive();
        var view = new Views.SettingsDialogView { DataContext = Card(Registrar(hive)) };
        var window = new Window { Content = view, Width = 1067, Height = 526 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var png = view.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content?.ToString() == ".png");
            png.IsChecked = true;
            var viewer = view.FindControl<Button>("AssociationViewer")!;
            viewer.Command!.Execute(viewer.CommandParameter);
            var apply = view.FindControl<Button>("ApplyAssociations")!;
            apply.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var pngType = FileTypeCatalog.All.Single(t => t.Extension == ".png");
            Assert.True(hive.HasValue(@"Software\Classes\.png\OpenWithProgids", pngType.RegistrationProgId));
            Assert.False(hive.HasValue(@"Software\Classes\.jpg\OpenWithProgids", pngType.RegistrationProgId));
            Assert.False(hive.HasValue(@"Software\Classes\.jpeg\OpenWithProgids", pngType.RegistrationProgId));
            Assert.Equal($"\"{Exe}\" --viewer \"%1\"", hive.GetString($@"Software\Classes\{pngType.RegistrationProgId}\shell\open\command", ""));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Unregistering_one_extension_keeps_other_registered_extensions_and_external_values()
    {
        var hive = new InMemoryRegistryHive();
        hive.SetString(@"Software\Classes\.jpg", "", "Other.Picture");
        var registrar = Registrar(hive);
        registrar.Apply(new AssociationChoice(AssociationTarget.Off, AssociationTarget.Viewer, AssociationTarget.Off));
        var view = new Views.SettingsDialogView { DataContext = Card(registrar) };
        var window = new Window { Content = view, Width = 1067, Height = 526 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var boxes = view.GetVisualDescendants().OfType<CheckBox>().ToArray();
            foreach (var box in boxes.Where(c => c.Content?.ToString()?.StartsWith('.') == true)) box.IsChecked = false;
            boxes.Single(c => c.Content?.ToString() == ".jpg").IsChecked = true;
            var remove = view.FindControl<Button>("RemoveAssociations")!;
            Assert.True(remove.Command!.CanExecute(null));
            remove.Command.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var jpgType = FileTypeCatalog.All.Single(t => t.Extension == ".jpg");
            var pngType = FileTypeCatalog.All.Single(t => t.Extension == ".png");
            var jpegType = FileTypeCatalog.All.Single(t => t.Extension == ".jpeg");
            Assert.False(hive.HasValue(@"Software\Classes\.jpg\OpenWithProgids", jpgType.ProgId));
            Assert.True(hive.HasValue(@"Software\Classes\.png\OpenWithProgids", pngType.ProgId));
            Assert.True(hive.HasValue(@"Software\Classes\.jpeg\OpenWithProgids", jpegType.ProgId));
            Assert.Equal("Other.Picture", hive.GetString(@"Software\Classes\.jpg", ""));
            Assert.Equal($"\"{Exe}\" --viewer \"%1\"", hive.GetString($@"Software\Classes\{pngType.ProgId}\shell\open\command", ""));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Select_all_has_a_keyboard_path_and_reflects_a_partial_selection()
    {
        var view = new Views.SettingsDialogView { DataContext = Card(Registrar(new InMemoryRegistryHive())) };
        var window = new Window { Content = view, Width = 1067, Height = 526 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var boxes = view.GetVisualDescendants().OfType<CheckBox>().ToArray();
            var all = boxes.Single(c => c.Content?.ToString() == "All");
            Assert.True(all.Focus());
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Dispatcher.UIThread.RunJobs();
            var types = boxes.Where(c => c.Content?.ToString()?.StartsWith('.') == true).ToArray();
            Assert.Equal(FileTypeCatalog.All.Count, types.Length);
            Assert.All(types, c => Assert.True(c.IsChecked));
            types[0].IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Null(all.IsChecked);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Registering_one_type_preserves_an_unselected_foreign_command_byte_for_byte()
    {
        var hive = new InMemoryRegistryHive();
        var registrar = Registrar(hive);
        registrar.Apply(AssociationChoice.Suggested);
        const string foreign = "\"D:\\another copy\\Nfty.Desktop.exe\" \"%1\"";
        hive.SetString(@"Software\Classes\nfty.CookBook\shell\open\command", "", foreign);
        var view = new Views.SettingsDialogView { DataContext = Card(registrar) };
        var window = new Window { Content = view, Width = 1067, Height = 526 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            foreach (var box in view.GetVisualDescendants().OfType<CheckBox>()
                         .Where(c => c.Content?.ToString()?.StartsWith('.') == true))
                box.IsChecked = box.Content?.ToString() == ".json";
            var viewer = view.FindControl<Button>("AssociationViewer")!;
            viewer.Command!.Execute(viewer.CommandParameter);
            view.FindControl<Button>("ApplyAssociations")!.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(foreign, hive.GetString(@"Software\Classes\nfty.CookBook\shell\open\command", ""));
            Assert.Equal("nfty.CookBook", hive.GetString(@"Software\Classes\.cbk", ""));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_new_picture_registration_does_not_repair_an_unselected_legacy_shared_handler()
    {
        var hive = new InMemoryRegistryHive();
        const string legacy = "nfty.Viewer.Image";
        const string foreign = "\"D:\\old copy\\Nfty.Desktop.exe\" --viewer \"%1\"";
        hive.SetString($@"Software\Classes\{legacy}\shell\open\command", "", foreign);
        hive.SetString(@"Software\Classes\.jpg\OpenWithProgids", legacy, "");
        hive.SetString(@"Software\nfty\Capabilities\FileAssociations", ".jpg", legacy);
        hive.SetString(@"Software\RegisteredApplications", "nfty", @"Software\nfty\Capabilities");
        var view = new Views.SettingsDialogView { DataContext = Card(Registrar(hive)) };
        var window = new Window { Content = view, Width = 1067, Height = 526 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            foreach (var box in view.GetVisualDescendants().OfType<CheckBox>()
                         .Where(c => c.Content?.ToString()?.StartsWith('.') == true))
                box.IsChecked = box.Content?.ToString() == ".png";
            var viewer = view.FindControl<Button>("AssociationViewer")!;
            viewer.Command!.Execute(viewer.CommandParameter);
            view.FindControl<Button>("ApplyAssociations")!.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(foreign, hive.GetString($@"Software\Classes\{legacy}\shell\open\command", ""));
            Assert.True(hive.HasValue(@"Software\Classes\.jpg\OpenWithProgids", legacy));
            Assert.Equal(legacy, hive.GetString(@"Software\nfty\Capabilities\FileAssociations", ".jpg"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_footer_keeps_the_open_settings_trigger_selected_above_child_dialogs()
    {
        var dialogs = new DialogService();
        var shell = new ShellViewModel(new FakeNav(), dialogs, new StubTheme(), new StatusService(),
            settingsCard: () => new SettingsDialogViewModel(dialogs,
                new ParallelismSettingService(StateStore.InMemory(), () => null)));
        var view = new Views.ShellChromeView { DataContext = shell };
        var window = new Window { Content = view, Width = 1280, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var button = view.GetVisualDescendants().OfType<Button>()
                .Single(b => ToolTip.GetTip(b)?.ToString() == "Settings");
            var before = button.Bounds;
            shell.ShowSettingsCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("on", button.Classes);
            Assert.Equal(before, button.Bounds);
            var settings = dialogs.Active!;
            _ = dialogs.ShowAsync<object>(new ErrorDialogViewModel(dialogs, "Could not register", "Try again."));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("on", button.Classes);
            dialogs.Close(settings, null);
            Dispatcher.UIThread.RunJobs();
            Assert.IsType<ErrorDialogViewModel>(dialogs.Active);
            Assert.DoesNotContain("on", button.Classes);
            dialogs.Close(null);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("on", button.Classes);
            Assert.Equal(before, button.Bounds);
        }
        finally { window.Close(); }
    }

    private sealed class StubTheme : IThemeService
    {
        public bool IsDark => false;
        public void Toggle() { }
    }

    private sealed class NoNativeCalls : IFileAssociations
    {
        public bool IsSupported => false;
        public AssociationStatus Inspect() => throw new InvalidOperationException("Unsupported inspection was called.");
        public void Apply(AssociationChoice choice) => throw new InvalidOperationException("Unsupported apply was called.");
        public void SetTargets(IReadOnlyCollection<string> extensions, AssociationTarget target) => throw new InvalidOperationException("Unsupported delta was called.");
        public void Remove() => throw new InvalidOperationException("Unsupported remove was called.");
        public void OpenDefaultApps() => throw new InvalidOperationException("Unsupported launch was called.");
    }
}
