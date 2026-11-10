using Avalonia;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using Nfty.Core.Publish.Community;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>A checker that answers from a script and records what it was asked.</summary>
internal sealed class ScriptedChecker : IShareChecker
{
    public Func<ShareCheckResult> Result { get; set; } = () => ShareFixtures.Ok();
    public List<(ExportOptions Options, string? Book)> Calls { get; } = [];
    public TaskCompletionSource? Gate { get; set; }

    public async Task<ShareCheckResult> CheckAsync(ShareSource source, ExportOptions options, string? bookPath, CancellationToken ct)
    {
        Calls.Add((options, bookPath));
        if (Gate is { } gate) await gate.Task.WaitAsync(ct);
        return Result();
    }
}

/// <summary>A repository that answers from a script and records every deletion it is asked for.</summary>
internal sealed class FakeShareRepo : IShareRepo
{
    public RepoSnapshot? Snapshot { get; set; }
    public List<IReadOnlyList<RepoExtra>> Removed { get; } = [];
    public Func<RepoSnapshot?>? AfterRemoval { get; set; }
    public int Fetches { get; private set; }

    public Task<RepoSnapshot?> FetchAsync(string login, CancellationToken ct)
    {
        Fetches++;
        return Task.FromResult(Snapshot);
    }

    public Task RemoveAsync(string login, IReadOnlyList<RepoExtra> extras, CancellationToken ct)
    {
        Removed.Add(extras);
        if (AfterRemoval is { } after) Snapshot = after();
        return Task.CompletedTask;
    }
}

/// <summary>Dialogs that answer a confirmation from a script and keep what was asked.</summary>
internal sealed class ShareScriptedDialogs : IDialogService
{
    public bool Answer { get; set; }
    public List<ViewModelBase> Shown { get; } = [];
    public ViewModelBase? Active { get; private set; }
    public event Action? Changed;

    public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
    {
        Shown.Add(dialog);
        Active = dialog;
        Changed?.Invoke();
        return Task.FromResult(Answer is TResult t ? (TResult?)t : default);
    }

    public void Close(object? result) { Active = null; Changed?.Invoke(); }
}

/// <summary>An account whose login a test can change, announcing it the way the real one does.</summary>
internal sealed class RaisingAccount : IGitHubAccount
{
    public string? Login { get; set; }
    public bool IsConnected => Login is not null;
    public long? UserId { get; set; }
    public Task<long?> EnsureUserIdAsync(CancellationToken cancellationToken) => Task.FromResult(UserId);
    public string? UnavailableReason => null;
    public Task<DeviceCode> BeginAsync(CancellationToken cancellationToken) => throw new InvalidOperationException();
    public Task ConnectAsync(DeviceCode code, CancellationToken cancellationToken) => throw new InvalidOperationException();
    public void Disconnect() { }
    public GitHubShareClient? Client() => throw new InvalidOperationException();
    public event EventHandler? Changed;
    public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}

internal static class ShareFixtures
{
    public const string Collection = "VaporCats";

    public static ShareCheckResult Ok(int width = 16, int height = 16, long bytes = 4_194_304, string fingerprint = "fp-mine",
        string collection = Collection, params string[] problems) =>
        new(new ShareFacts(problems, 2, width, height, false, false, null, collection, fingerprint, "sha-mine"), width, height, bytes);

    public static ShareSource Source(out string dir, string? book = null, Func<SetManifest, SetManifest>? edit = null)
    {
        dir = Directory.CreateTempSubdirectory("nfty-share-").FullName;
        using var cooked = CoreTestBook.Tiny();
        LoadedCookBook source = cooked;
        string? candidate = null;
        if (book is not null)
        {
            CookBookArchive.Write(book, cooked.Manifest, cooked.Recipes);
            source = CookBookArchive.Read(book);
            candidate = book;
        }
        using var set = Generator.Generate(source, new GenerateOptions(2, "seed1"));
        SetWriter.Write(set, dir, pack: false);
        if (!ReferenceEquals(source, cooked)) source.Dispose();
        using var loaded = SetReader.Read(dir);
        var manifest = edit?.Invoke(loaded.Manifest) ?? loaded.Manifest;
        return new ShareSource(dir, manifest, loaded.Items[0].ImagePath, candidate is null ? [] : [candidate]);
    }

    public static CommunityEntry Mine(string slug, string name = "Mine", string collection = "Other", string fingerprint = "fp-old") =>
        CommunityFixtures.Entry(slug, name, "me", ownerId: 7, collection: collection) with { Release = slug + "-v1", Fingerprint = fingerprint };

    public static RepoSnapshot Repo(IEnumerable<CommunityEntry> shares, Action<List<GitHubTreeItem>, List<string>, List<GitHubRelease>, List<string>>? edit = null)
    {
        var list = shares.ToList();
        var tree = new List<GitHubTreeItem> { new("README.md", "blob", "s1", 3), new("nfty-public.json", "blob", "s2", 3) };
        var branches = new List<string> { "main" };
        var releases = list.Select((e, i) => new GitHubRelease(i + 1, e.Release,
            [new GitHubAsset(100 + i, e.Slug + ".set", 10), new GitHubAsset(200 + i, "preview.png", 1)])).ToList();
        var tags = list.Select(e => e.Release).ToList();
        edit?.Invoke(tree, branches, releases, tags);
        return new RepoSnapshot(new GitHubRepo("me/nfty-public", false, "main", 40), tree, branches, releases, tags,
            new PublicRepoManifest(1, list), []);
    }

    public static string Slug => ShareSlug.Of(Collection);

    public static ShareResult Result(int pull = 12, string slug = "vaporcats") =>
        new(pull, new Uri($"https://github.com/nfty-gallery/nfty-gallery/pull/{pull}"), slug, slug + "-v1", new PendingShare(pull, slug, null, null));
}

/// <summary>The share card's Editing state: what it says, when Share waits, and what it will and won't do.</summary>
public class ShareDialogTests
{
    internal sealed class Rig : IDisposable
    {
        public ScriptedChecker Checker { get; } = new();
        public FakeShareRepo Repo { get; } = new();
        public ShareScriptedDialogs Dialogs { get; } = new();
        public RaisingAccount Account { get; } = new() { Login = "me" };
        public FakeCommunityCache Cache { get; } = new();
        public List<ShareRequest> Sent { get; } = [];
        public ShareSource Source { get; set; } = null!;
        public string Dir { get; private set; } = "";
        public IReadOnlyList<string> Tags { get; set; } = ["props", "pixel-art", "food", "icons", "items", "scenery"];

        public Rig(Func<SetManifest, SetManifest>? edit = null, string? book = null)
        {
            Source = ShareFixtures.Source(out var dir, book, edit);
            Dir = dir;
            Repo.Snapshot = ShareFixtures.Repo([]);
        }

        public async Task<ShareDialogViewModel> OpenAsync()
        {
            var vm = new ShareDialogViewModel(Source, Dialogs, Account, Cache, Repo, Checker,
                run: (r, _, _, _) => { Sent.Add(r); return Task.FromResult(ShareFixtures.Result()); }, tags: () => Tags);
            await vm.PendingBook;
            await vm.PendingFacts;
            await vm.PendingRepo;
            return vm;
        }

        public void Dispose() { try { Directory.Delete(Dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Limit_rows_turn_warning_and_name_the_fix()
    {
        using var rig = new Rig(m => m with { Count = 1200 });
        rig.Checker.Result = () => ShareFixtures.Ok(600, 600, 214L * 1024 * 1024, problems: ShareRules.CanvasTooLarge(600, 600));
        using var vm = await rig.OpenAsync();

        Assert.Equal(4, vm.LimitRows.Count);
        Assert.True(vm.LimitRows[0].IsOver);
        Assert.Contains("600 x 600 of 512 x 512", vm.LimitRows[0].Text);
        Assert.Contains("smaller canvas", vm.LimitRows[0].Text);
        Assert.True(vm.LimitRows[1].IsOver);
        Assert.Contains("1,200 of 1,000 assets", vm.LimitRows[1].Text);
        Assert.True(vm.LimitRows[2].IsOver);
        Assert.Contains("214 MB of 200 MB", vm.LimitRows[2].Text);
        Assert.Contains("share fewer assets", vm.LimitRows[2].Text);
        Assert.False(vm.LimitRows[3].IsOver);
        Assert.Equal("0 of 3 shared", vm.LimitRows[3].Text);
        Assert.Equal(ShareRules.TooManyAssets(1200), vm.ShareReason);
        Assert.False(vm.CanShare);
    }

    [Fact]
    public async Task Limit_rows_say_checking_until_the_check_lands()
    {
        using var rig = new Rig();
        rig.Checker.Gate = new TaskCompletionSource();
        var vm = new ShareDialogViewModel(rig.Source, rig.Dialogs, rig.Account, rig.Cache, rig.Repo, rig.Checker,
            run: (_, _, _, _) => Task.FromResult(ShareFixtures.Result()), tags: () => []);
        using (vm)
        {
            await vm.PendingRepo;
            Assert.Contains("checking", vm.LimitRows[0].Text);
            Assert.Contains("checking", vm.LimitRows[2].Text);
            Assert.Equal("Checking your Set…", vm.ShareReason);
            rig.Checker.Gate.SetResult();
            await vm.PendingFacts;
            Assert.Equal("", vm.ShareReason);
            Assert.True(vm.CanShare);
            Assert.Equal("Size 4 MB of 200 MB", vm.LimitRows[2].Text);
        }
    }

    [Fact]
    public async Task Plan_matches_CommunityShare_options()
    {
        using var rig = new Rig();
        using var vm = await rig.OpenAsync();
        var request = new ShareRequest(rig.Dir, false, null, false, "n", "", [], null, false);
        var expected = SetExporter.Plan(rig.Dir, CommunityShare.Options(request), null);

        Assert.Equal(expected.Parts(), vm.PlanParts);
        Assert.Equal(expected.SizeText(), vm.PlanSize);
        // And the checker was asked for exactly those options: an archive, never sealed.
        var asked = rig.Checker.Calls.Single().Options;
        Assert.Equal(ExportShape.Archive, asked.Shape);
        Assert.False(asked.Sealed);
    }

    [Fact]
    public async Task Book_checkbox_disabled_without_a_matching_book()
    {
        using var rig = new Rig();
        using var vm = await rig.OpenAsync();
        Assert.False(vm.IncludeCookBookEnabled);
        Assert.Null(vm.CookBookPath);

        var dir = Directory.CreateTempSubdirectory("nfty-share-book-").FullName;
        try
        {
            using var withBook = new Rig(book: Path.Combine(dir, "VaporCats.cbk"));
            using var found = await withBook.OpenAsync();
            Assert.True(found.IncludeCookBookEnabled);
            Assert.EndsWith("VaporCats.cbk", found.CookBookPath);

            found.IncludeCookBook = true;
            await found.PendingFacts;
            Assert.Equal(found.CookBookPath, withBook.Checker.Calls.Last().Book);
            Assert.True(found.BuildRequest().IncludeCookBook);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Extras_block_disables_share()
    {
        using var rig = new Rig();
        rig.Repo.Snapshot = ShareFixtures.Repo([], (tree, _, _, _) =>
        {
            tree.Add(new GitHubTreeItem("notes.txt", "blob", "s3", 12));
            tree.Add(new GitHubTreeItem("vendor", "commit", "s4", null));
        });
        using var vm = await rig.OpenAsync();

        Assert.Equal(ShareLowerBlock.Extras, vm.LowerBlock);
        Assert.Equal(ShareRules.RepoExtras, vm.BlockHeading);
        Assert.Equal(ShareRules.RepoExtras, vm.ShareReason);
        Assert.False(vm.CanShare);
        // Only what nfty can delete is offered for removal; the submodule says why not.
        Assert.True(vm.CanRemoveExtras);
        Assert.Contains(vm.Extras, r => r.Removable && r.Text.Contains("notes.txt"));
        Assert.Contains(vm.Extras, r => !r.Removable && r.Text == ShareRules.RepoSubmodule("vendor"));
    }

    [Fact]
    public async Task Remove_these_deletes_only_after_confirmation()
    {
        using var rig = new Rig();
        rig.Repo.Snapshot = ShareFixtures.Repo([], (tree, _, _, _) =>
        {
            tree.Add(new GitHubTreeItem("notes.txt", "blob", "s3", 12));
            tree.Add(new GitHubTreeItem("vendor", "commit", "s4", null));
        });
        using var vm = await rig.OpenAsync();

        // Declined: nothing is deleted, nothing is rescanned into a different answer.
        rig.Dialogs.Answer = false;
        await vm.RemoveExtrasCommand.ExecuteAsync(null);
        Assert.Empty(rig.Repo.Removed);
        var confirm = Assert.IsType<ConfirmDialogViewModel>(rig.Dialogs.Shown.Single());
        Assert.Contains("notes.txt", confirm.Message);
        Assert.DoesNotContain("vendor", confirm.Message);
        Assert.Contains("This can't be undone from nfty.", confirm.Message);
        Assert.Equal(ShareLowerBlock.Extras, vm.LowerBlock);

        // Confirmed: only the removable one goes, and the card rescans.
        rig.Dialogs.Answer = true;
        int fetches = rig.Repo.Fetches;
        rig.Repo.AfterRemoval = () => ShareFixtures.Repo([], (tree, _, _, _) => tree.Add(new GitHubTreeItem("vendor", "commit", "s4", null)));
        await vm.RemoveExtrasCommand.ExecuteAsync(null);
        var removed = Assert.Single(rig.Repo.Removed);
        Assert.Equal(["notes.txt"], removed.Select(e => e.Name));
        Assert.True(rig.Repo.Fetches > fetches);
        // The submodule is still there, so Share still waits and nothing offers to remove it.
        Assert.False(vm.CanRemoveExtras);
        Assert.False(vm.CanShare);
    }

    [Fact]
    public async Task At_three_shares_share_waits_for_a_choice()
    {
        using var rig = new Rig();
        rig.Repo.Snapshot = ShareFixtures.Repo([ShareFixtures.Mine("a"), ShareFixtures.Mine("b"), ShareFixtures.Mine("c")]);
        using var vm = await rig.OpenAsync();

        Assert.Equal(ShareLowerBlock.Replace, vm.LowerBlock);
        Assert.Equal(3, vm.ReplaceChoices.Count);
        Assert.True(vm.LimitRows[3].IsOver);
        Assert.Equal(ShareRules.AtShareLimit, vm.ShareReason);
        Assert.False(vm.CanShare);

        vm.ReplaceChoices[1].ChooseCommand.Execute(null);
        Assert.Equal("b", vm.ReplaceSlug);
        Assert.True(vm.ReplaceChoices[1].IsSelected);
        Assert.Equal("", vm.ShareReason);
        Assert.True(vm.CanShare);
        Assert.Equal("b", vm.BuildRequest().ReplaceSlug);
    }

    [Fact]
    public async Task An_already_shared_collection_needs_the_replace_tick()
    {
        using var rig = new Rig();
        rig.Repo.Snapshot = ShareFixtures.Repo([ShareFixtures.Mine(ShareFixtures.Slug, "Cats", ShareFixtures.Collection)]);
        using var vm = await rig.OpenAsync();

        Assert.Equal(ShareLowerBlock.AlreadyShared, vm.LowerBlock);
        Assert.NotNull(vm.AlreadyShared);
        Assert.Contains("Shared 1 Nov 2026 · updated 1 Nov 2026", vm.AlreadyShared!.DatesText);
        Assert.False(vm.ReplaceSharedVersion);
        Assert.Equal(ShareRules.ReplaceNotChosen, vm.ShareReason);
        Assert.False(vm.CanShare);

        vm.ReplaceSharedVersion = true;
        Assert.Equal("", vm.ShareReason);
        Assert.True(vm.CanShare);
        Assert.True(vm.BuildRequest().ReplaceSharedVersion);
    }

    [Fact]
    public async Task A_set_listed_by_someone_else_shows_who_and_disables_share()
    {
        using var rig = new Rig();
        rig.Cache.Cached = CommunityFixtures.Listing([
            CommunityFixtures.Entry("theirs", "Theirs", "bob", ownerId: 99) with { Fingerprint = "fp-mine" }]);
        using var vm = await rig.OpenAsync();

        Assert.Equal(ShareLowerBlock.Duplicate, vm.LowerBlock);
        Assert.Equal(ShareRules.AlreadyListed("bob"), vm.DuplicateReason);
        Assert.Equal(ShareRules.AlreadyListed("bob"), vm.BlockHeading);
        Assert.Equal(ShareRules.AlreadyListed("bob"), vm.ShareReason);
        Assert.False(vm.CanShare);
        Assert.False(vm.ReplaceSharedVersion);
    }

    [Fact]
    public async Task An_unchanged_set_shows_nothing_changed()
    {
        using var rig = new Rig();
        var mine = ShareFixtures.Mine(ShareFixtures.Slug, "Cats", ShareFixtures.Collection, "fp-mine");
        rig.Repo.Snapshot = ShareFixtures.Repo([mine]);
        rig.Cache.Cached = CommunityFixtures.Listing([mine]);
        using var vm = await rig.OpenAsync();

        Assert.Equal(ShareLowerBlock.Duplicate, vm.LowerBlock);
        Assert.Equal(ShareRules.NothingChanged("Cats", mine.Updated), vm.BlockHeading);
        Assert.False(vm.CanShare);
        // No tick offered: an update that changes nothing is not a choice.
        Assert.False(vm.IsAlreadySharedBlock);
    }

    [Fact]
    public async Task Signed_out_card_shows_connect_not_hidden()
    {
        using var rig = new Rig();
        rig.Account.Login = null;
        int connects = 0;
        var vm = new ShareDialogViewModel(rig.Source, rig.Dialogs, rig.Account, rig.Cache, rig.Repo, rig.Checker,
            connect: () => { connects++; return Task.CompletedTask; }, run: (_, _, _, _) => Task.FromResult(ShareFixtures.Result()), tags: () => []);
        using (vm)
        {
            await vm.PendingFacts;
            Assert.True(vm.IsSignedOut);
            Assert.False(vm.IsSignedIn);
            Assert.Equal(ShareRules.NotConnected, vm.ShareReason);
            Assert.False(vm.CanShare);
            Assert.Equal(0, rig.Repo.Fetches);
            await vm.ConnectGitHubCommand.ExecuteAsync(null);
            Assert.Equal(1, connects);
        }
    }

    [Fact]
    public async Task Tags_toggle_up_to_five_and_a_sixth_is_refused_with_the_reason()
    {
        using var rig = new Rig();
        rig.Tags = ["a", "b", "c", "d", "e", "f"];
        using var vm = await rig.OpenAsync();

        foreach (var chip in vm.TagChips.Take(5)) vm.ToggleTagCommand.Execute(chip);
        Assert.Equal(["a", "b", "c", "d", "e"], vm.Tags);
        Assert.Equal("5 of 5", vm.TagsCount);

        vm.ToggleTagCommand.Execute(vm.TagChips[5]);
        Assert.False(vm.TagChips[5].IsOn);
        Assert.Equal(ShareRules.TooManyTags, vm.TagNote);

        // Off again frees a slot and clears the note.
        vm.ToggleTagCommand.Execute(vm.TagChips[0]);
        vm.ToggleTagCommand.Execute(vm.TagChips[5]);
        Assert.True(vm.TagChips[5].IsOn);
        Assert.Equal("", vm.TagNote);
        Assert.Equal(["b", "c", "d", "e", "f"], vm.BuildRequest().Tags);
    }

    [Fact]
    public async Task A_card_opened_before_the_tags_were_fetched_fetches_them()
    {
        using var rig = new Rig();
        IReadOnlyList<string> kept = [];
        var gate = new TaskCompletionSource();
        using var vm = new ShareDialogViewModel(rig.Source, rig.Dialogs, rig.Account, rig.Cache, rig.Repo, rig.Checker,
            tags: () => kept, refreshTags: async _ => { await gate.Task; kept = ["props", "items"]; });
        Assert.False(vm.HasTags);
        Assert.True(vm.TagsLoading);
        Assert.Equal("Loading the gallery's tags…", vm.TagsEmptyText);
        gate.SetResult();
        await vm.PendingTags;
        Assert.False(vm.TagsLoading);
        Assert.Equal(["props", "items"], vm.TagChips.Select(c => c.Name));
    }

    [Fact]
    public async Task Signed_out_the_shares_row_says_why_it_has_no_count()
    {
        using var rig = new Rig();
        rig.Account.Login = null;
        using var vm = await rig.OpenAsync();
        var row = vm.LimitRows[3];
        Assert.Equal("YOUR SHARES", row.Label);
        Assert.Equal("connect GitHub to see", row.Value);
        Assert.True(row.IsPending);
        Assert.DoesNotContain("—", row.Text);
        Assert.Equal("Sharing needs a GitHub account", vm.ReadyHeading);
        Assert.True(vm.IsNoneBlock);
    }

    [Fact]
    public async Task An_empty_tag_list_offers_none_and_still_shares()
    {
        using var rig = new Rig();
        rig.Tags = [];
        using var vm = await rig.OpenAsync();
        Assert.False(vm.HasTags);
        Assert.Equal("No tags available yet.", ShareDialogViewModel.NoTagsText);
        Assert.True(vm.CanShare);
    }

    [Fact]
    public async Task Listing_words_the_run_would_refuse_keep_share_disabled()
    {
        using var rig = new Rig();
        using var vm = await rig.OpenAsync();
        vm.Name = "";
        Assert.Equal(ShareRules.NameLength, vm.ShareReason);
        vm.Name = "Cats";
        vm.Description = "see https://example.com";
        Assert.Equal(ShareRules.NoLinks, vm.ShareReason);
        vm.Description = "fine";
        Assert.True(vm.CanShare);
    }

    [AvaloniaFact]
    public async Task Share_button_press_runs_the_command()
    {
        using var rig = new Rig();
        using var vm = await rig.OpenAsync();
        var view = new Views.ShareDialogView { DataContext = vm };
        var window = new Window { Content = view, Width = 1067, Height = 526 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var button = view.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("accent"));
            Assert.True(button.IsEnabled);
            Assert.Same(vm.ShareCommand, button.Command);
            // A real press and release at the button's centre: raising Click would skip Button's own handler.
            var centre = button.TranslatePoint(new Avalonia.Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(centre, Avalonia.Input.MouseButton.Left);
            window.MouseUp(centre, Avalonia.Input.MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            UiThreadWait.For(vm.PendingShare);
            Assert.Single(rig.Sent);
            Assert.Equal(ShareState.Done, vm.State);
            Assert.Equal("VaporCats", rig.Sent[0].Name);
        }
        finally { window.Close(); }
    }
}
