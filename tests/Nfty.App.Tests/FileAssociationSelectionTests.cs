using Nfty.App.Services;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>Selected-only changes, legacy compatibility, and independent Windows-default evidence.</summary>
public class FileAssociationSelectionTests
{
    private const string Exe = @"C:\nfty app\nfty.exe";
    private static readonly AssociationLayout Layout = AssociationLayout.Windows;
    private static FileAssociationRegistrar Registrar(IRegistryHive hive,
        Func<string, AssociationDefault>? defaults = null) =>
        new(hive, Layout, Exe, launch: _ => { }, defaultFor: defaults);
    private static AssociatedType Type(string extension) => FileTypeCatalog.All.Single(t => t.Extension == extension);
    private static string Command(AssociatedType type) => $@"{Layout.Classes}\{type.RegistrationProgId}\shell\open\command";

    [Fact]
    public void Individual_modes_and_all_selection_round_trip_then_take_back_only_owned_values()
    {
        var hive = new InMemoryRegistryHive();
        hive.SetString(Layout.Classes + @"\.png", "", "Photos.Picture");
        hive.SetString(Layout.Classes + @"\.cbk", "", "Other.CookBook");
        // In-memory evidence of the protected choices: the registrar never addresses this tree.
        const string userChoice = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.png\UserChoice";
        hive.SetString(userChoice, "ProgId", "Photos.Picture");
        hive.SetString(userChoice, "Hash", "Opaque Windows hash CASE");
        var before = Snapshot(hive);
        var registrar = Registrar(hive);
        registrar.SetTargets([".cbk"], AssociationTarget.Viewer);
        registrar.SetTargets([".rcp", ".png", ".mnu"], AssociationTarget.App);
        var choices = registrar.Inspect().Current;
        Assert.Equal(AssociationTarget.Viewer, choices.For(Type(".cbk")));
        Assert.Equal(AssociationTarget.App, choices.For(Type(".rcp")));
        Assert.Equal(AssociationTarget.Viewer, choices.For(Type(".png")));
        Assert.Equal(AssociationTarget.Viewer, choices.For(Type(".mnu")));
        Assert.Equal(AssociationTarget.Off, choices.For(Type(".jpg")));
        registrar.SetTargets(FileTypeCatalog.All.Select(t => t.Extension).ToArray(), AssociationTarget.App);
        Assert.All(registrar.Inspect().Types, t => Assert.Equal(AssociationState.Registered, t.State));
        Assert.Equal(FileTypeCatalog.All.Count, FileTypeCatalog.All.Select(t => t.RegistrationProgId).Distinct().Count());
        registrar.SetTargets(FileTypeCatalog.All.Select(t => t.Extension).ToArray(), AssociationTarget.Off);
        registrar.SetTargets([".png"], AssociationTarget.Off); // Idempotent.
        Assert.Equal(before, Snapshot(hive));
    }

    [Fact]
    public void Repairing_one_type_preserves_all_unselected_values_including_foreign_edits_and_legacy_aliases()
    {
        var hive = new InMemoryRegistryHive();
        var registrar = Registrar(hive);
        registrar.Apply(new(AssociationTarget.App, AssociationTarget.Viewer, AssociationTarget.Viewer));
        hive.SetString(Layout.Classes + @"\nfty.Viewer.Image\shell\open\command", "", "Foreign Picture Command CASE");
        hive.SetString(Layout.Classes + @"\nfty.CookBook\shell\open\command", "", "Foreign CookBook Command CASE");
        hive.SetString(Layout.Capabilities, "ApplicationIcon", "Foreign ICON CASE");
        hive.SetString(Layout.Capabilities, "ApplicationName", "Foreign NAME CASE");
        hive.SetString(Layout.Capabilities + @"\FileAssociations", ".unknown", "Foreign.Unknown");
        hive.SetString(Layout.RegisteredApplications, "nfty", @"Software\OtherCopy\Capabilities");
        var before = Snapshot(hive, skipSelectedPng: true);
        registrar.SetTargets([".png"], AssociationTarget.Viewer);
        Assert.Equal(before, Snapshot(hive, skipSelectedPng: true));
        Assert.Equal($"\"{Exe}\" --viewer \"%1\"", hive.GetString(Command(Type(".png")), ""));
        Assert.Equal("nfty.Viewer.Image", hive.GetString(Layout.Capabilities + @"\FileAssociations", ".jpg"));
        registrar.SetTargets([".png"], AssociationTarget.Off);
        Assert.Equal(before, Snapshot(hive, skipSelectedPng: true));
    }

    [Fact]
    public void Unregistering_a_legacy_sibling_keeps_its_shared_handler_until_the_last_reference_is_removed()
    {
        var hive = new InMemoryRegistryHive();
        var registrar = Registrar(hive, _ => new(true, null));
        registrar.Apply(new(AssociationTarget.Off, AssociationTarget.Viewer, AssociationTarget.Off));
        string legacyCommand = Layout.Classes + @"\nfty.Viewer.Image\shell\open\command";
        var command = hive.GetString(legacyCommand, "");
        registrar.SetTargets([".jpg"], AssociationTarget.Off);
        Assert.Equal(command, hive.GetString(legacyCommand, ""));
        Assert.Equal(AssociationState.NotRegistered, registrar.Inspect().Types.Single(t => t.Type.Extension == ".jpg").State);
        Assert.Equal(AssociationState.Registered, registrar.Inspect().Types.Single(t => t.Type.Extension == ".png").State);
        registrar.SetTargets([".png", ".jpeg"], AssociationTarget.Off);
        Assert.Null(hive.GetString(legacyCommand, ""));
        registrar.Remove();
        Assert.Empty(hive.Keys);
    }

    [Fact]
    public void Invalid_requests_are_rejected_before_any_mutation()
    {
        var hive = new InMemoryRegistryHive();
        var registrar = Registrar(hive);
        registrar.SetTargets([".cbk"], AssociationTarget.App);
        var before = Snapshot(hive);
        Assert.Throws<ArgumentException>(() => registrar.SetTargets([".png", ".exe"], AssociationTarget.Viewer));
        Assert.Throws<ArgumentException>(() => registrar.SetTargets([".cbk"], (AssociationTarget)99));
        Assert.Equal(before, Snapshot(hive));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Repairing_one_picture_preserves_a_legacy_handler_used_only_by_a_siblings_effective_default_or_unknown_evidence(bool known)
    {
        var hive = new InMemoryRegistryHive();
        var registrar = Registrar(hive, _ => known ? new(true, "nfty.Viewer.Image") : AssociationDefault.Unknown);
        registrar.Apply(new(AssociationTarget.Off, AssociationTarget.Viewer, AssociationTarget.Off));
        foreach (string ext in new[] { ".jpg", ".jpeg" })
        {
            hive.DeleteValue(Layout.Classes + "\\" + ext + @"\OpenWithProgids", "nfty.Viewer.Image");
            hive.DeleteValue(Layout.Capabilities + @"\FileAssociations", ext);
        }
        string legacyKey = Layout.Classes + @"\nfty.Viewer.Image\shell\open\command";
        string? command = hive.GetString(legacyKey, "");
        var before = Snapshot(hive, skipSelectedPng: true);
        registrar.SetTargets([".png"], AssociationTarget.Viewer);
        Assert.Equal(command, hive.GetString(legacyKey, ""));
        Assert.Equal(before, Snapshot(hive, skipSelectedPng: true));
        registrar.SetTargets([".png"], AssociationTarget.Off);
        Assert.Equal(command, hive.GetString(legacyKey, ""));
        if (known)
            Assert.Equal(AssociationTarget.Viewer, registrar.Inspect().Types.Single(t => t.Type.Extension == ".jpg").DefaultTarget);
    }

    [Theory]
    [InlineData(19045, 0, false)]
    [InlineData(22000, 1817, false)]
    [InlineData(22621, 1555, false)]
    [InlineData(22631, 0, true)]
    [InlineData(26100, 0, true)]
    public void Default_apps_links_use_a_conservative_version_fallback_and_escape_the_registered_name(int build, int revision, bool deep)
    {
        var version = new Version(10, 0, build, revision);
        const string registeredName = "nfty & viewer/é";
        Assert.Equal(deep ? "ms-settings:defaultapps?registeredAppUser=nfty%20%26%20viewer%2F%C3%A9" : "ms-settings:defaultapps",
            FileAssociationRegistrar.DefaultAppsUriFor(version, registeredName));
        string? launched = null;
        var registrar = new FileAssociationRegistrar(new InMemoryRegistryHive(), Layout, Exe,
            launch: uri => launched = uri, windowsVersion: version);
        registrar.OpenDefaultApps();
        Assert.Equal(deep ? FileAssociationRegistrar.DefaultAppsUri : "ms-settings:defaultapps", launched);
    }

    [Fact]
    public void Effective_defaults_require_the_queried_type_ProgId_and_its_exact_command_mode()
    {
        var hive = new InMemoryRegistryHive();
        var answers = new Dictionary<string, AssociationDefault>();
        var registrar = Registrar(hive, ext => answers.GetValueOrDefault(ext, AssociationDefault.Unknown));
        registrar.SetTargets([".cbk", ".rcp", ".igt", ".png", ".json"], AssociationTarget.App);
        answers[".cbk"] = new(true, Type(".cbk").RegistrationProgId);
        answers[".rcp"] = new(true, Type(".rcp").RegistrationProgId);
        hive.SetString(Command(Type(".rcp")), "", $"\"{Exe}\" --viewer \"%1\"");
        answers[".igt"] = new(true, "Other.SameExe");
        hive.SetString(Layout.Classes + @"\Other.SameExe\shell\open\command", "", $"\"{Exe}\" \"%1\"");
        answers[".png"] = new(true, Type(".png").RegistrationProgId);
        answers[".json"] = new(true, null);
        var status = registrar.Inspect().Types;
        Assert.Equal(AssociationTarget.App, status.Single(t => t.Type.Extension == ".cbk").DefaultTarget);
        Assert.Equal(AssociationTarget.Viewer, status.Single(t => t.Type.Extension == ".rcp").DefaultTarget);
        Assert.Equal(AssociationTarget.Viewer, status.Single(t => t.Type.Extension == ".png").DefaultTarget);
        Assert.Null(status.Single(t => t.Type.Extension == ".igt").DefaultTarget);
        Assert.True(status.Single(t => t.Type.Extension == ".json").Default.IsKnown);
        Assert.Null(status.Single(t => t.Type.Extension == ".json").DefaultTarget);
        Assert.False(status.Single(t => t.Type.Extension == ".jpg").Default.IsKnown);
        var failing = Registrar(hive, _ => throw new UnauthorizedAccessException());
        Assert.All(failing.Inspect().Types, t => Assert.False(t.Default.IsKnown));
        Assert.Equal(AssociationState.Registered, failing.Inspect().State);
    }

    [Fact]
    public void Windows_delta_registration_round_trips_only_a_private_injected_root()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = @"Software\nfty-private-test-" + Guid.NewGuid().ToString("N");
        var layout = new AssociationLayout(root + @"\Classes", root + @"\App", root + @"\RegisteredApplications");
        var hive = new WindowsRegistryHive();
        try
        {
            var registrar = new FileAssociationRegistrar(hive, layout, Exe, notify: null, launch: _ => { });
            registrar.SetTargets([".png", ".jpg"], AssociationTarget.Viewer);
            string jpgCommand = layout.Classes + @"\" + Type(".jpg").RegistrationProgId + @"\shell\open\command";
            const string foreign = "Foreign CASE-preserved %nfty_test_variable% command";
            using (var commandKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(jpgCommand))
                commandKey.SetValue(null, foreign, Microsoft.Win32.RegistryValueKind.ExpandString);
            void AssertForeignValue()
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(jpgCommand)!;
                Assert.Equal(Microsoft.Win32.RegistryValueKind.ExpandString, key.GetValueKind(null));
                Assert.Equal(foreign, key.GetValue(null, null, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames));
            }
            registrar.SetTargets([".png"], AssociationTarget.Off);
            AssertForeignValue();
            registrar.SetTargets([".jpg"], AssociationTarget.Off);
            AssertForeignValue();
            Assert.False(hive.HasValue(layout.Classes + @"\.jpg\OpenWithProgids", Type(".jpg").RegistrationProgId));
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false); }
    }

    private static string[] Snapshot(InMemoryRegistryHive hive, bool skipSelectedPng = false) => hive.Keys
        .Where(k => !k.Equals(Layout.Ledger, StringComparison.OrdinalIgnoreCase))
        .SelectMany(k => hive.ValueNames(k).Select(n => (Key: k, Name: n, Value: hive.GetString(k, n))))
        .Where(v => !skipSelectedPng || (!v.Key.StartsWith(Layout.Classes + @"\.png", StringComparison.OrdinalIgnoreCase)
            && !v.Key.StartsWith(Layout.Classes + @"\" + Type(".png").RegistrationProgId, StringComparison.OrdinalIgnoreCase)
            && !(v.Key == Layout.Capabilities + @"\FileAssociations" && v.Name == ".png")))
        .Select(v => $"{v.Key} | {v.Name} = {v.Value}").Order(StringComparer.Ordinal).ToArray();
}
