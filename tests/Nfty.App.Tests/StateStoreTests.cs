using System;
using System.Collections.Generic;
using System.IO;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The <c>.nfty</c> store's discovery order, and the exit from the state where there is nowhere to
/// write.
///
/// <para>Nothing here touches the real <see cref="AppContext.BaseDirectory"/> or the real working
/// directory: every candidate root is a temp folder, which is exactly why they are constructor
/// parameters. A test that let the defaults through would leave a <c>.nfty</c> in the developer's
/// own build output.</para>
///
/// <para>Two ways of making a root unusable appear below. <c>Blocked</c> is real: a FILE occupying
/// the <c>.nfty</c> name, which no OS will let you create a directory over — that exercises the
/// production write-probe end to end. The <c>canCreate</c> seam on the internal constructor is the
/// other, used only where the ORDER is what is under test and a genuinely unwritable folder would
/// mean ACL surgery. <see cref="IStateStore.Choose"/> ignores the seam entirely, so every refusal
/// asserted here is a real one.</para>
/// </summary>
public class StateStoreTests
{
    private sealed class Temps : IDisposable
    {
        private readonly List<string> _dirs = new();

        public string Dir()
        {
            var d = Directory.CreateTempSubdirectory().FullName;
            _dirs.Add(d);
            return d;
        }

        /// <summary>A root where a new <c>.nfty</c> genuinely cannot be created, because the name is
        /// already taken by a file. No permissions involved — this fails the same way everywhere.</summary>
        public string Blocked()
        {
            var d = Dir();
            File.WriteAllText(Path.Combine(d, StateStore.FolderName), "in the way");
            return d;
        }

        /// <summary>A root that already holds a <c>.nfty</c> folder — "where did I write last time?"</summary>
        public string WithExistingStore()
        {
            var d = Dir();
            Directory.CreateDirectory(Path.Combine(d, StateStore.FolderName));
            return d;
        }

        public void Dispose()
        {
            foreach (var d in _dirs)
                try { Directory.Delete(d, recursive: true); } catch { /* best effort */ }
        }
    }

    private static string Nfty(string root) => Path.Combine(root, StateStore.FolderName);

    private static (KitchenSession Session, string Dir) Kitchen(Temps t, bool blocked = false)
    {
        var dir = blocked ? t.Blocked() : t.Dir();
        var path = Path.Combine(dir, "Studio.ktn");
        Core.Formats.Kitchen.Create(path, new KitchenManifest("studio", "Studio"));
        var session = new KitchenSession();
        session.Open(path);
        return (session, dir);
    }

    // ---- the discovery order --------------------------------------------------------------------

    [Fact]
    public void Rule_one_is_beside_the_executable()
    {
        using var t = new Temps();
        var beside = t.Dir();
        var working = t.Dir();

        var store = new StateStore(kitchen: null, beside: beside, working: working);

        Assert.Equal(StoreLocation.BesideExecutable, store.Resolution.Location);
        Assert.Equal(Nfty(beside), store.Resolution.Directory);
        Assert.True(Directory.Exists(Nfty(beside)));
        Assert.False(store.Resolution.IsInMemory);

        // Dot-prefixed, so it sorts to the top and reads as "not for you".
        Assert.Equal(".nfty", StateStore.FolderName);
    }

    [Fact]
    public void Rule_two_is_the_working_directory_when_nothing_can_be_made_beside_the_executable()
    {
        using var t = new Temps();
        var beside = t.Blocked();
        var working = t.Dir();

        var store = new StateStore(kitchen: null, beside: beside, working: working);

        Assert.Equal(StoreLocation.WorkingDirectory, store.Resolution.Location);
        Assert.Equal(Nfty(working), store.Resolution.Directory);
    }

    [Fact]
    public void Rule_three_is_the_open_kitchens_folder_when_the_first_two_are_blocked()
    {
        using var t = new Temps();
        var (session, kitchenDir) = Kitchen(t);

        var store = new StateStore(session, beside: t.Blocked(), working: t.Blocked());

        Assert.Equal(StoreLocation.Kitchen, store.Resolution.Location);
        Assert.Equal(Nfty(kitchenDir), store.Resolution.Directory);
    }

    [Fact]
    public void Rule_four_is_memory_and_the_store_says_so()
    {
        using var t = new Temps();
        var beside = t.Blocked();
        var working = t.Blocked();

        var store = new StateStore(kitchen: null, beside: beside, working: working);

        Assert.Equal(StoreLocation.InMemory, store.Resolution.Location);
        Assert.Null(store.Resolution.Directory);
        Assert.True(store.Resolution.IsInMemory);

        // Saying so is the requirement: an app that quietly fails to save is the outcome the whole
        // path exists to prevent.
        Assert.Contains("session only", store.Resolution.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Choose a folder", store.Resolution.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_unwritable_everywhere_store_still_holds_state_for_the_session()
    {
        using var t = new Temps();
        var beside = t.Blocked();
        var working = t.Blocked();
        var store = new StateStore(kitchen: null, beside: beside, working: working);

        store.Write("palette.json", "[\"hex:d6249f\"]");

        Assert.Equal("[\"hex:d6249f\"]", store.Read("palette.json"));
        // ...and nothing was smuggled onto disk anyway.
        Assert.False(Directory.Exists(Nfty(beside)));
        Assert.False(Directory.Exists(Nfty(working)));
    }

    [Fact]
    public void An_existing_store_is_honoured_even_where_a_new_one_could_not_be_created()
    {
        using var t = new Temps();
        var beside = t.Dir();                 // nothing here yet
        var working = t.WithExistingStore();  // but a .nfty is already here

        // Nothing is creatable anywhere. Only the folder that ALREADY exists can rescue this, and
        // that is the point: one order has to answer both "where should I write?" and "where did I
        // write last time?" without a pointer file.
        var store = new StateStore(null, beside, working, canCreate: _ => false);

        Assert.Equal(StoreLocation.WorkingDirectory, store.Resolution.Location);
        Assert.Equal(Nfty(working), store.Resolution.Directory);
    }

    [Fact]
    public void With_nothing_creatable_and_nothing_existing_the_store_falls_to_memory()
    {
        // The control for the test above: same seam, no pre-existing folder. If this also resolved
        // to a directory, the previous test would be proving nothing.
        using var t = new Temps();

        var store = new StateStore(null, t.Dir(), t.Dir(), canCreate: _ => false);

        Assert.Equal(StoreLocation.InMemory, store.Resolution.Location);
    }

    [Fact]
    public void The_order_beats_existence_an_existing_store_further_down_does_not_outrank_rule_one()
    {
        using var t = new Temps();
        var beside = t.Dir();                 // creatable, but empty
        var working = t.WithExistingStore();  // already has one

        var store = new StateStore(kitchen: null, beside: beside, working: working);

        Assert.Equal(StoreLocation.BesideExecutable, store.Resolution.Location);
        Assert.Equal(Nfty(beside), store.Resolution.Directory);
    }

    [Fact]
    public void Resolving_again_changes_nothing_and_raises_nothing()
    {
        using var t = new Temps();
        var store = new StateStore(kitchen: null, beside: t.Dir(), working: t.Dir());
        var before = store.Resolution;
        var raised = 0;
        store.Changed += () => raised++;

        store.Resolve();
        store.Resolve();

        Assert.Equal(before, store.Resolution);
        Assert.Equal(0, raised);
    }

    // ---- the exit from memory --------------------------------------------------------------------

    [Fact]
    public void Choosing_a_writable_folder_moves_the_sessions_state_into_it()
    {
        using var t = new Temps();
        var store = new StateStore(kitchen: null, beside: t.Blocked(), working: t.Blocked());
        store.Write("palette.json", "[\"hex:d6249f\"]");
        Assert.True(store.Resolution.IsInMemory);
        var raised = 0;
        store.Changed += () => raised++;

        var chosen = t.Dir();
        var result = store.Choose(chosen);

        Assert.True(result.Accepted);
        Assert.Null(result.Reason);
        Assert.Equal(StoreLocation.Chosen, store.Resolution.Location);
        Assert.Equal(Nfty(chosen), store.Resolution.Directory);
        Assert.Equal(1, raised);

        // The swatches moved in — and are now on disk, where the next launch finds them by rule 2/3.
        Assert.Equal("[\"hex:d6249f\"]", store.Read("palette.json"));
        Assert.Equal("[\"hex:d6249f\"]", File.ReadAllText(Path.Combine(Nfty(chosen), "palette.json")));
    }

    [Fact]
    public void Choosing_a_folder_that_cannot_be_written_to_is_refused_with_the_reason()
    {
        using var t = new Temps();
        var store = new StateStore(kitchen: null, beside: t.Blocked(), working: t.Blocked());
        var blocked = t.Blocked();

        var result = store.Choose(blocked);

        Assert.False(result.Accepted);
        Assert.NotNull(result.Reason);
        Assert.Contains(blocked, result.Reason!, StringComparison.Ordinal);   // names the folder
        Assert.NotEqual(blocked, result.Reason);                             // and says why, not just which

        // Refused at the point of choosing, rather than accepted and silently lost later.
        Assert.Equal(StoreLocation.InMemory, store.Resolution.Location);
    }

    [Fact]
    public void Choosing_nothing_is_refused_rather_than_throwing()
    {
        using var t = new Temps();
        var store = new StateStore(kitchen: null, beside: t.Blocked(), working: t.Blocked());

        var result = store.Choose("   ");

        Assert.False(result.Accepted);
        Assert.NotNull(result.Reason);
        Assert.Equal(StoreLocation.InMemory, store.Resolution.Location);
    }

    [Fact]
    public void A_chosen_folder_survives_a_kitchen_opening_afterwards()
    {
        using var t = new Temps();
        var session = new KitchenSession();
        var store = new StateStore(session, beside: t.Blocked(), working: t.Blocked());
        var chosen = t.Dir();
        Assert.True(store.Choose(chosen).Accepted);

        // Re-running the order here would find rules 1-3 still unusable and drop the user's choice —
        // and their swatches with it.
        var kitchenDir = t.Dir();
        var ktn = Path.Combine(kitchenDir, "Studio.ktn");
        Core.Formats.Kitchen.Create(ktn, new KitchenManifest("studio", "Studio"));
        session.Open(ktn);

        Assert.Equal(StoreLocation.Chosen, store.Resolution.Location);
        Assert.Equal(Nfty(chosen), store.Resolution.Directory);
    }

    [Fact]
    public void Opening_a_kitchen_moves_an_in_memory_store_into_it()
    {
        using var t = new Temps();
        var session = new KitchenSession();
        var store = new StateStore(session, beside: t.Blocked(), working: t.Blocked());
        Assert.True(store.Resolution.IsInMemory);
        store.Write("recents.json", "[]");

        var kitchenDir = t.Dir();
        var ktn = Path.Combine(kitchenDir, "Studio.ktn");
        Core.Formats.Kitchen.Create(ktn, new KitchenManifest("studio", "Studio"));
        session.Open(ktn);

        Assert.Equal(StoreLocation.Kitchen, store.Resolution.Location);
        Assert.Equal("[]", File.ReadAllText(Path.Combine(Nfty(kitchenDir), "recents.json")));
    }

    // ---- reading and writing ---------------------------------------------------------------------

    [Fact]
    public void A_file_that_is_not_there_reads_as_null_on_disk_and_in_memory()
    {
        using var t = new Temps();

        Assert.Null(new StateStore(kitchen: null, beside: t.Dir(), working: t.Dir()).Read("nope.json"));
        Assert.Null(new StateStore(kitchen: null, beside: t.Blocked(), working: t.Blocked()).Read("nope.json"));
    }

    [Fact]
    public void A_write_that_fails_is_swallowed()
    {
        using var t = new Temps();
        // Pinned at a path that is a FILE, so creating the folder and writing into it both fail.
        var file = Path.Combine(t.Dir(), "not-a-folder");
        File.WriteAllText(file, "x");
        var store = StateStore.At(file);

        store.Write("palette.json", "[]");   // must not throw — convenience state never surfaces

        Assert.Null(store.Read("palette.json"));
    }

    [Fact]
    public void A_pinned_store_uses_the_folder_exactly_as_given()
    {
        using var t = new Temps();
        var dir = t.Dir();

        var store = StateStore.At(dir);
        store.Write("recents.json", "[]");

        Assert.Equal(StoreLocation.Chosen, store.Resolution.Location);
        Assert.Equal(dir, store.Resolution.Directory);
        // No .nfty nesting: the caller decided the whole path.
        Assert.True(File.Exists(Path.Combine(dir, "recents.json")));
        Assert.False(Directory.Exists(Nfty(dir)));
    }

    [Fact]
    public void A_pinned_store_ignores_the_discovery_order()
    {
        using var t = new Temps();
        var dir = t.Dir();
        var store = StateStore.At(dir);

        store.Resolve();

        Assert.Equal(dir, store.Resolution.Directory);
    }

    [Fact]
    public void Every_resolved_location_names_its_folder_when_it_has_one()
    {
        using var t = new Temps();
        var beside = t.Dir();

        var store = new StateStore(kitchen: null, beside: beside, working: t.Dir());

        Assert.Contains(Nfty(beside), store.Resolution.Description, StringComparison.Ordinal);
    }
    /// <summary>
    /// A write-probe that fails must leave <b>no</b> <c>.nfty</c> behind. This is the one failure the
    /// existence rule in <c>Locate</c> is not allowed to cause: a half-made folder would be HONOURED on
    /// the next launch, pinning the store to somewhere it cannot write, and every save after that is
    /// swallowed with nothing said.
    ///
    /// <para>Reaching it needs creation to succeed and the file write to fail, which the <c>Blocked</c>
    /// helper cannot arrange — a file in the way makes <c>CreateDirectory</c> itself throw, so the
    /// cleanup branch runs with nothing to clean. Windows separates "add file" from "add
    /// subdirectory" in its ACLs, so denying only the former is exactly this state. Where that cannot
    /// be arranged the test skips rather than pretending to have run.</para>
    /// </summary>
    [Fact]
    public void A_failed_write_probe_leaves_no_half_made_folder_to_be_honoured_later()
    {
        using var temps = new Temps();
        var parent = temps.Dir();

        if (!DenyFileCreation(parent))
            Assert.Skip("Could not deny file creation on this platform; nothing to assert.");
        try
        {
            // The arrangement itself has to hold, or the test would pass for the wrong reason.
            Directory.CreateDirectory(Path.Combine(parent, "probe-dir"));
            Assert.Throws<UnauthorizedAccessException>(
                () => File.WriteAllText(Path.Combine(parent, "probe-file"), "x"));

            var store = new StateStore(kitchen: null, beside: parent, working: temps.Blocked());

            Assert.NotEqual(StoreLocation.BesideExecutable, store.Resolution.Location);
            Assert.False(Directory.Exists(Path.Combine(parent, StateStore.FolderName)),
                "the probe failed, so the .nfty it made on the way must not survive to be honoured");
        }
        finally { AllowFileCreation(parent); }
    }

    /// <summary>Denies "create files" while leaving "create folders", so a directory can still be made
    /// where a file cannot. Returns false when that cannot be arranged here.</summary>
    private static bool DenyFileCreation(string dir) => Icacls(dir, "/deny", "*S-1-1-0:(OI)(CI)(WD)");

    private static void AllowFileCreation(string dir) => Icacls(dir, "/remove:d", "*S-1-1-0");

    private static bool Icacls(string dir, string verb, string spec)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "icacls", $"\"{dir}\" {verb} {spec}")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
            if (p is null) return false;
            p.WaitForExit(10_000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
