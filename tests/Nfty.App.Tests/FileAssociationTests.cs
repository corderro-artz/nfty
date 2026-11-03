using Avalonia;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.App.Viewer;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Registering nfty as the program that opens its file types (Windows, per user). The logic runs
/// over a memory hive; one test round-trips the real registry under a throwaway key that is removed
/// in finally. No test writes nfty's real keys.
/// </summary>
public class FileAssociationTests
{
    private const string Exe = @"C:\Program Files\nfty app\nfty.exe";
    private static readonly AssociationLayout Layout = AssociationLayout.Windows;

    /// <summary>A machine with other programs already present: .png belongs to Photos, .json to an editor
    /// that also lists itself under OpenWithProgids.</summary>
    private static InMemoryRegistryHive Machine()
    {
        var hive = new InMemoryRegistryHive();
        hive.SetString(@"Software\Classes\.png", "", "pngfile");
        hive.SetString(@"Software\Classes\.json", "", "JSONFile");
        hive.SetString(@"Software\Classes\.json\OpenWithProgids", "Editor.json", "");
        hive.SetString(@"Software\RegisteredApplications", "Editor", @"Software\Editor\Capabilities");
        return hive;
    }

    private static FileAssociationRegistrar Registrar(IRegistryHive hive, string exe = Exe, List<string>? launched = null,
        Action? notify = null) =>
        new(hive, Layout, exe, notify, uri => launched?.Add(uri));

    private static readonly AssociationChoice Everything =
        new(AssociationTarget.App, AssociationTarget.Viewer, AssociationTarget.Viewer);

    [Fact]
    public void Every_extension_the_viewer_opens_is_registrable_and_nothing_else_is()
    {
        var catalog = FileTypeCatalog.All.Select(t => t.Extension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(ViewerOpener.Extensions.ToHashSet(StringComparer.OrdinalIgnoreCase), catalog);
        Assert.Equal(catalog.Count, FileTypeCatalog.All.Count);   // each extension once
    }

    [Fact]
    public void Apply_registers_every_type_with_quoted_commands_and_lists_nfty_in_default_apps()
    {
        var hive = Machine();
        int notified = 0;
        Registrar(hive, notify: () => notified++).Apply(Everything);

        Assert.Equal($"\"{Exe}\" \"%1\"", hive.GetString(@"Software\Classes\nfty.CookBook\shell\open\command", ""));
        Assert.Equal($"\"{Exe}\" --viewer \"%1\"", hive.GetString(@"Software\Classes\nfty.Viewer.Image\shell\open\command", ""));
        // The app does not edit a Menu, so it runs the viewer even when the group is App.
        Assert.Equal($"\"{Exe}\" --viewer \"%1\"", hive.GetString(@"Software\Classes\nfty.Menu\shell\open\command", ""));
        Assert.Equal($"\"{Exe}\",0", hive.GetString(@"Software\Classes\nfty.CookBook\DefaultIcon", ""));
        Assert.Equal("nfty CookBook", hive.GetString(@"Software\Classes\nfty.CookBook", ""));
        foreach (var t in FileTypeCatalog.All)
        {
            Assert.True(hive.HasValue($@"Software\Classes\{t.Extension}\OpenWithProgids", t.ProgId), t.Extension);
            Assert.Equal(t.ProgId, hive.GetString(@"Software\nfty\Capabilities\FileAssociations", t.Extension));
        }
        Assert.Equal(@"Software\nfty\Capabilities", hive.GetString(@"Software\RegisteredApplications", "nfty"));
        // nfty's own extension defaults to nfty; a shared one keeps its owner.
        Assert.Equal("nfty.CookBook", hive.GetString(@"Software\Classes\.cbk", ""));
        Assert.Equal("pngfile", hive.GetString(@"Software\Classes\.png", ""));
        Assert.Equal("JSONFile", hive.GetString(@"Software\Classes\.json", ""));
        Assert.Equal(1, notified);

        var status = Registrar(hive).Inspect();
        Assert.Equal(AssociationState.Registered, status.State);
        Assert.Equal(Everything, status.Current);
    }

    [Fact]
    public void Remove_takes_back_exactly_what_apply_wrote()
    {
        var hive = Machine();
        var before = hive.Dump();
        var registrar = Registrar(hive);
        registrar.Apply(Everything);
        Assert.NotEqual(before, hive.Dump());

        registrar.Remove();

        Assert.Equal(before, hive.Dump());
        Assert.Equal(AssociationState.NotRegistered, registrar.Inspect().State);
    }

    [Fact]
    public void Another_programs_claims_survive_remove()
    {
        var hive = Machine();
        hive.SetString(@"Software\Classes\.set", "", "Other.SetFile");    // somebody else owns .set
        var registrar = Registrar(hive);
        registrar.Apply(Everything);
        Assert.Equal("Other.SetFile", hive.GetString(@"Software\Classes\.set", ""));

        // After nfty registered, another program lists itself under a key nfty CREATED.
        hive.SetString(@"Software\Classes\.cbk\OpenWithProgids", "Other.Cbk", "");
        registrar.Remove();

        Assert.Equal("Other.SetFile", hive.GetString(@"Software\Classes\.set", ""));
        Assert.True(hive.HasValue(@"Software\Classes\.cbk\OpenWithProgids", "Other.Cbk"));
        Assert.False(hive.HasValue(@"Software\Classes\.cbk\OpenWithProgids", "nfty.CookBook"));
        Assert.True(hive.HasValue(@"Software\Classes\.json\OpenWithProgids", "Editor.json"));
        Assert.Equal(@"Software\Editor\Capabilities", hive.GetString(@"Software\RegisteredApplications", "Editor"));
        Assert.False(hive.KeyExists(@"Software\Classes\nfty.CookBook"));
        Assert.False(hive.KeyExists(@"Software\nfty"));
    }

    [Fact]
    public void A_group_left_off_is_not_registered_and_reads_back_off()
    {
        var hive = Machine();
        var choice = new AssociationChoice(AssociationTarget.Viewer, AssociationTarget.Off, AssociationTarget.Off);
        Registrar(hive).Apply(choice);

        Assert.False(hive.KeyExists(@"Software\Classes\nfty.Viewer.Image"));
        Assert.False(hive.HasValue(@"Software\Classes\.png\OpenWithProgids", "nfty.Viewer.Image"));
        Assert.Equal($"\"{Exe}\" --viewer \"%1\"", hive.GetString(@"Software\Classes\nfty.CookBook\shell\open\command", ""));
        var status = Registrar(hive).Inspect();
        Assert.Equal(AssociationState.Registered, status.State);
        Assert.Equal(choice, status.Current);
    }

    [Fact]
    public void A_moved_exe_needs_repair_and_reapplying_points_at_the_new_copy()
    {
        var hive = Machine();
        Registrar(hive, @"D:\old place\nfty.exe").Apply(Everything);

        var moved = Registrar(hive, Exe);
        var status = moved.Inspect();
        Assert.Equal(AssociationState.NeedsRepair, status.State);
        Assert.Contains(@"D:\old place\nfty.exe", status.Detail, StringComparison.Ordinal);
        Assert.Equal(Everything, status.Current);

        moved.Apply(status.Current);
        Assert.Equal(AssociationState.Registered, moved.Inspect().State);
        Assert.DoesNotContain(hive.Dump(), l => l.Contains(@"d:\old place", StringComparison.Ordinal));
    }

    [Fact]
    public void Part_of_a_registration_deleted_by_hand_needs_repair()
    {
        var hive = Machine();
        var registrar = Registrar(hive);
        registrar.Apply(Everything);
        hive.DeleteValue(@"Software\Classes\.glb\OpenWithProgids", "nfty.Viewer.Model");

        var status = registrar.Inspect();
        Assert.Equal(AssociationState.NeedsRepair, status.State);
        Assert.Contains(".glb", status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_apps_opens_nfty_s_own_page()
    {
        var launched = new List<string>();
        Registrar(Machine(), launched: launched).OpenDefaultApps();
        Assert.Equal(new[] { "ms-settings:defaultapps?registeredAppUser=nfty" }, launched);
    }

    [Fact]
    public void The_viewer_switch_is_read_from_the_command_line()
    {
        Assert.True(StartupPath.WantsViewer(new[] { "--viewer", @"C:\a b\x.png" }));
        Assert.Equal(@"C:\a b\x.png", StartupPath.FromArguments(new[] { "--viewer", @"C:\a b\x.png" }));
        Assert.False(StartupPath.WantsViewer(new[] { @"C:\book.cbk" }));
    }

    // ------------------------------------------------------------------ the Settings card

    private static SettingsDialogViewModel Card(IFileAssociations? associations) =>
        new(new FakeDialogs(), new ParallelismSettingService(StateStore.InMemory(), () => null),
            associations: associations);

    [Fact]
    public void The_system_tab_exists_only_where_associations_do()
    {
        Assert.False(Card(null).ShowsSystemTab);
        Assert.False(Card(new UnsupportedFileAssociations()).ShowsSystemTab);
        Assert.True(Card(Registrar(Machine())).ShowsSystemTab);
    }

    [Fact]
    public void Nothing_registered_proposes_a_start_and_apply_registers_it()
    {
        var hive = Machine();
        var vm = Card(Registrar(hive));

        Assert.Equal("not registered", vm.AssociationSummary);
        Assert.Equal(AssociationChoice.Suggested, vm.Choice);
        Assert.True(vm.ApplyAssociationsCommand.CanExecute(null));
        Assert.False(vm.RemoveAssociationsCommand.CanExecute(null));

        vm.ApplyAssociationsCommand.Execute(null);

        Assert.Equal("registered", vm.AssociationSummary);
        Assert.True(vm.IsAssociated);
        Assert.False(vm.ApplyAssociationsCommand.CanExecute(null));
        Assert.Equal(AssociationState.Registered, Registrar(hive).Inspect().State);

        vm.UseMetadataTargetCommand.Execute(AssociationTarget.Viewer);
        Assert.Equal("not applied", vm.AssociationSummary);
        Assert.True(vm.IsAssociationIdle);
        Assert.True(vm.ApplyAssociationsCommand.CanExecute(null));

        vm.RemoveAssociationsCommand.Execute(null);
        Assert.Equal("not registered", vm.AssociationSummary);
        Assert.Equal(AssociationState.NotRegistered, Registrar(hive).Inspect().State);
    }

    [Fact]
    public void A_card_opened_over_a_registration_shows_it_and_offers_repair_when_stale()
    {
        var hive = Machine();
        var choice = new AssociationChoice(AssociationTarget.Viewer, AssociationTarget.Off, AssociationTarget.Viewer);
        Registrar(hive, @"D:\old\nfty.exe").Apply(choice);

        var vm = Card(Registrar(hive));

        Assert.Equal(choice, vm.Choice);
        Assert.Equal("needs repair", vm.AssociationSummary);
        Assert.Equal("Repair", vm.ApplyLabel);
        vm.ApplyAssociationsCommand.Execute(null);
        Assert.Equal("registered", vm.AssociationSummary);
    }

    [Fact]
    public void Pictures_cannot_be_sent_to_the_app()
    {
        var vm = Card(Registrar(Machine()));
        vm.UsePicturesTargetCommand.Execute(AssociationTarget.App);
        Assert.Equal(AssociationTarget.Viewer, vm.PicturesTarget);
    }

    // ------------------------------------------------------------------ failures, in product words

    private const string RawText = "Access to the registry key 'HKEY_CURRENT_USER\\Software\\Classes' is denied.";

    /// <summary>A hive Windows refuses: reads, writes, or both.</summary>
    private sealed class RefusingHive(InMemoryRegistryHive inner, bool reads, bool writes, int writesBeforeRefusal = 0) : IRegistryHive
    {
        private int _writes;
        private void Read() { if (reads) throw new UnauthorizedAccessException(RawText); }
        private void Write() { if (writes && _writes++ >= writesBeforeRefusal) throw new UnauthorizedAccessException(RawText); }
        public bool KeyExists(string key) { Read(); return inner.KeyExists(key); }
        public bool HasValue(string key, string name) { Read(); return inner.HasValue(key, name); }
        public string? GetString(string key, string name) { Read(); return inner.GetString(key, name); }
        public void CreateKey(string key) { Write(); inner.CreateKey(key); }
        public void SetString(string key, string name, string value) { Write(); inner.SetString(key, name, value); }
        public IReadOnlyList<string> GetMultiString(string key, string name) { Read(); return inner.GetMultiString(key, name); }
        public void SetMultiString(string key, string name, IReadOnlyList<string> values) => inner.SetMultiString(key, name, values);
        public void DeleteValue(string key, string name) { Write(); inner.DeleteValue(key, name); }
        public void DeleteKeyIfEmpty(string key) { Write(); inner.DeleteKeyIfEmpty(key); }
    }

    private static (SettingsDialogViewModel Vm, FakeDialogs Dialogs) CardOver(IFileAssociations associations)
    {
        var dialogs = new FakeDialogs();
        return (new SettingsDialogViewModel(dialogs, new ParallelismSettingService(StateStore.InMemory(), () => null),
            associations: associations), dialogs);
    }

    private static void AssertProductError(FakeDialogs dialogs, string title, string message)
    {
        var card = Assert.IsType<ErrorDialogViewModel>(dialogs.Active);
        Assert.Equal(title, card.Title);
        Assert.Equal(message, card.Message);
        Assert.DoesNotContain("HKEY", card.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_registration_windows_will_not_let_nfty_read_says_so_in_the_reason_line()
    {
        var (vm, dialogs) = CardOver(Registrar(new RefusingHive(Machine(), reads: true, writes: false)));
        Assert.Equal("needs repair", vm.AssociationSummary);
        Assert.Equal(AssociationText.CannotRead, vm.AssociationDetail);
        Assert.Null(dialogs.Active);
    }

    [Fact]
    public void A_refused_apply_is_a_product_sentence_and_what_landed_can_still_be_removed()
    {
        var inner = Machine();
        var before = inner.Dump();
        var (vm, dialogs) = CardOver(Registrar(new RefusingHive(inner, reads: false, writes: true, writesBeforeRefusal: 9)));

        vm.ApplyAssociationsCommand.Execute(null);

        AssertProductError(dialogs, AssociationText.ApplyFailedTitle, AssociationText.ApplyFailed);
        Assert.Equal("needs repair", vm.AssociationSummary);
        // The part that landed was recorded: a registrar Windows no longer refuses takes it all back.
        Registrar(inner).Remove();
        Assert.Equal(before, inner.Dump());
    }

    [Fact]
    public void A_refused_remove_is_a_product_sentence()
    {
        var inner = Machine();
        Registrar(inner).Apply(Everything);
        var (vm, dialogs) = CardOver(Registrar(new RefusingHive(inner, reads: false, writes: true)));

        vm.RemoveAssociationsCommand.Execute(null);

        AssertProductError(dialogs, AssociationText.RemoveFailedTitle, AssociationText.RemoveFailed);
    }

    [Fact]
    public void Default_apps_that_will_not_open_is_a_product_sentence()
    {
        var registrar = new FileAssociationRegistrar(Machine(), Layout, Exe,
            launch: _ => throw new System.ComponentModel.Win32Exception(1155, "No application is associated with the specified file"));
        var (vm, dialogs) = CardOver(registrar);

        vm.OpenDefaultAppsCommand.Execute(null);

        AssertProductError(dialogs, AssociationText.DefaultAppsFailedTitle, AssociationText.DefaultAppsFailed);
    }

    [Fact]
    public void A_moved_copy_is_named_in_the_reason_line_not_an_error_card()
    {
        var hive = Machine();
        Registrar(hive, @"D:\old\nfty.exe").Apply(Everything);
        var (vm, dialogs) = CardOver(Registrar(hive));
        Assert.Equal(@"Registered to another copy of nfty (D:\old\nfty.exe). Repair points it at this one.", vm.AssociationDetail);
        Assert.Null(dialogs.Active);
    }

    [Fact]
    public void An_unsupported_platform_has_no_page_and_its_registrar_speaks_product_words()
    {
        var unsupported = new UnsupportedFileAssociations();
        Assert.False(Card(unsupported).ShowsSystemTab);
        var ex = Assert.Throws<PlatformNotSupportedException>(() => unsupported.Apply(Everything));
        Assert.Equal("File associations are a Windows setting.", ex.Message);
    }

    [Fact]
    public async Task A_damaged_archive_double_clicked_in_explorer_is_worded_not_dumped()
    {
        var dir = Directory.CreateTempSubdirectory("nfty-assoc-").FullName;
        var path = Path.Combine(dir, "broken.cbk");
        await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        var dialogs = new FakeDialogs();

        var subject = await ViewerOpener.OpenAsync(path, dialogs);

        Assert.Null(subject);
        var card = Assert.IsType<ErrorDialogViewModel>(dialogs.Active);
        Assert.Equal(
            "“broken.cbk” could not be read: it is damaged, only partly downloaded, or not the kind of file its name says. Download or export it again.",
            card.Message);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_registration_row_does_not_move_between_states()
    {
        var registered = Registrar(Machine());
        registered.Apply(AssociationChoice.Suggested);
        var states = new IFileAssociations[] { Registrar(Machine()), registered, SettingsPagesTests.StaleAssociations() };
        var lefts = new List<string>();
        foreach (var associations in states)
        {
            var vm = Card(associations);
            vm.Tab = SettingsTab.System;
            var view = new Views.SettingsDialogView { DataContext = vm };
            var window = new Avalonia.Controls.Window { Content = view, Width = 1067, Height = 526 };
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            try
            {
                var buttons = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view)
                    .OfType<Avalonia.Controls.Button>()
                    .Where(b => b.Content is "Remove" or "Default apps…" || b.Name == "ApplyAssociations")
                    .Select(b => $"{b.Content}@{b.TranslatePoint(default, view)!.Value.X:0.#}");
                lefts.Add(string.Join(" ", buttons.Select(s => s.Replace("Repair", "Apply", StringComparison.Ordinal))));
            }
            finally { window.Close(); }
        }
        Assert.Single(lefts.Distinct());
    }

    // ------------------------------------------------------------------ the real registry

    [Fact]
    public void The_windows_registry_round_trips_under_a_throwaway_root()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = @"Software\Classes\nfty-test-" + Guid.NewGuid().ToString("N");
        var layout = new AssociationLayout(root + @"\Classes", root + @"\App", root + @"\RegisteredApplications");
        var hive = new WindowsRegistryHive();
        try
        {
            var registrar = new FileAssociationRegistrar(hive, layout, Exe, notify: null, launch: _ => { });
            Assert.Equal(AssociationState.NotRegistered, registrar.Inspect().State);

            registrar.Apply(Everything);
            Assert.Equal($"\"{Exe}\" --viewer \"%1\"", hive.GetString(layout.Classes + @"\nfty.Viewer.Model\shell\open\command", ""));
            Assert.Equal("nfty.Set", hive.GetString(layout.Classes + @"\.set", ""));
            var status = registrar.Inspect();
            Assert.Equal(AssociationState.Registered, status.State);
            Assert.Equal(Everything, status.Current);

            registrar.Remove();
            Assert.False(hive.KeyExists(root));     // every key it created is gone, the root included
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false);
        }
    }
}
