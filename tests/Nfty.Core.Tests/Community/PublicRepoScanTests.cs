using System.Net;
using System.Text;
using Nfty.Core.Publish.Community;

namespace Nfty.Core.Tests.Community;

public class PublicRepoScanTests
{
    private static CommunityEntry Entry(string slug, int n = 1) => new(
        1, slug, new CommunityOwner(7, "me"), "nfty-public", $"{slug}-v{n}", slug, slug, "", [],
        new CommunityCanvas(8, 8), 4, false, false, null,
        new CommunityFile("https://x/a.set", 1, "aa"), new CommunityFile("https://x/p.png", 1, "bb"),
        "fp", "1.0", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static GitHubRelease Rel(long id, string slug, int n = 1, params string[] extraAssets) => new(id, $"{slug}-v{n}",
        [new GitHubAsset(id * 10, slug + ".set", 100), new GitHubAsset(id * 10 + 1, "preview.png", 5),
            .. extraAssets.Select((a, i) => new GitHubAsset(id * 10 + 2 + i, a, 9))], "me", "nfty-public");

    private static GitHubTreeItem Blob(string path, long? size = 3) => new(path, "blob", "sha-" + path, size);

    private static RepoSnapshot Clean(Action<Parts>? edit = null)
    {
        var p = new Parts();
        edit?.Invoke(p);
        return new RepoSnapshot(p.Repo, p.Tree, p.Branches, p.Releases, p.Tags, p.Manifest, p.Pending);
    }

    private sealed class Parts
    {
        public GitHubRepo Repo = new("me/nfty-public", false, "main", 40);
        public List<GitHubTreeItem> Tree = [Blob("README.md"), Blob("nfty-public.json")];
        public List<string> Branches = ["main"];
        public List<GitHubRelease> Releases = [Rel(1, "cat")];
        public List<string> Tags = ["cat-v1"];
        public PublicRepoManifest? Manifest = new(1, [Entry("cat")]);
        public string[] Pending = [];
    }

    [Fact]
    public void A_clean_repo_has_no_extras()
    {
        var scan = PublicRepoScan.Scan(Clean());
        Assert.Empty(scan.Extras);
        Assert.Null(scan.Blocker);
    }

    [Fact]
    public void A_stray_file_is_an_extra()
    {
        var scan = PublicRepoScan.Scan(Clean(p => p.Tree.AddRange([Blob("notes.txt", 12),
            new GitHubTreeItem("dump", "tree", "t", null), Blob("dump/a.bin", 100), Blob("dump/deep/b.bin", 50)])));
        Assert.Equal([new RepoExtra(RepoExtraKind.File, "dump", 150, null), new RepoExtra(RepoExtraKind.File, "notes.txt", 12, null)], scan.Extras);
    }

    [Fact]
    public void A_nested_readme_is_not_the_root_readme()
    {
        var scan = PublicRepoScan.Scan(Clean(p => p.Tree.Add(Blob("sub/README.md", 1))));
        Assert.Equal([new RepoExtra(RepoExtraKind.File, "sub", 1, null)], scan.Extras);
    }

    [Fact]
    public void A_second_branch_is_an_extra()
    {
        var scan = PublicRepoScan.Scan(Clean(p => p.Branches.Add("feature/x")));
        Assert.Equal([new RepoExtra(RepoExtraKind.Branch, "feature/x", null, null)], scan.Extras);
    }

    [Fact]
    public void An_unlisted_release_is_an_extra()
    {
        var scan = PublicRepoScan.Scan(Clean(p => { p.Releases.Add(Rel(2, "old")); p.Tags.Add("old-v1"); }));
        Assert.Equal([new RepoExtra(RepoExtraKind.Release, "old-v1", 105, 2), new RepoExtra(RepoExtraKind.Tag, "old-v1", null, null)], scan.Extras);
    }

    [Fact]
    public void A_fourth_listed_release_is_an_extra()
    {
        var scan = PublicRepoScan.Scan(Clean(p =>
        {
            p.Manifest = new(1, [Entry("a"), Entry("b"), Entry("c"), Entry("d")]);
            p.Releases = [Rel(1, "a"), Rel(2, "b"), Rel(3, "c"), Rel(4, "d")];
            p.Tags = ["a-v1", "b-v1", "c-v1", "d-v1"];
        }));
        Assert.Equal([new RepoExtra(RepoExtraKind.Release, "d-v1", 105, 4), new RepoExtra(RepoExtraKind.Tag, "d-v1", null, null)], scan.Extras);
    }

    [Fact]
    public void A_stray_asset_is_an_extra()
    {
        var scan = PublicRepoScan.Scan(Clean(p => p.Releases = [Rel(1, "cat", 1, "extra.zip")]));
        Assert.Equal([new RepoExtra(RepoExtraKind.Asset, "extra.zip", 9, 12)], scan.Extras);
    }

    [Fact]
    public void An_asset_named_for_another_slug_is_an_extra()
    {
        var scan = PublicRepoScan.Scan(Clean(p => p.Releases = [Rel(1, "cat", 1, "dog.set")]));
        Assert.Equal(RepoExtraKind.Asset, Assert.Single(scan.Extras).Kind);
    }

    [Fact]
    public void An_orphan_tag_is_an_extra()
    {
        var scan = PublicRepoScan.Scan(Clean(p => p.Tags.Add("v0.1")));
        Assert.Equal([new RepoExtra(RepoExtraKind.Tag, "v0.1", null, null)], scan.Extras);
    }

    [Fact]
    public void History_over_5_mb_blocks()
    {
        var scan = PublicRepoScan.Scan(Clean(p => p.Repo = p.Repo with { SizeKb = 5121 }));
        Assert.Equal(ShareRules.HistoryTooLarge(5121), scan.Blocker);
        Assert.Null(PublicRepoScan.Scan(Clean(p => p.Repo = p.Repo with { SizeKb = 5120 })).Blocker);
    }

    [Fact]
    public void A_superseded_release_passes_only_while_pending()
    {
        void Old(Parts p) { p.Releases.Add(Rel(2, "cat", 0)); p.Tags.Add("cat-v0"); }
        Assert.Empty(PublicRepoScan.Scan(Clean(p => { Old(p); p.Pending = ["cat-v0"]; })).Extras);
        var without = PublicRepoScan.Scan(Clean(Old));
        Assert.Equal([RepoExtraKind.Release, RepoExtraKind.Tag], without.Extras.Select(e => e.Kind));
    }

    [Fact]
    public void No_manifest_means_not_ours()
    {
        var scan = PublicRepoScan.Scan(Clean(p => { p.Manifest = null; p.Repo = p.Repo with { Private = true, SizeKb = 9999 }; }));
        Assert.Equal(ShareRules.RepoNotOurs, scan.Blocker);
        Assert.NotEmpty(scan.Extras); // still listed for display
    }

    [Fact]
    public void Private_blocks()
    {
        var scan = PublicRepoScan.Scan(Clean(p => p.Repo = p.Repo with { Private = true, SizeKb = 9999 }));
        Assert.Equal(ShareRules.RepoPrivate, scan.Blocker);
    }

    // ---- fetch and remove against the fake ----------------------------------------------------------------------

    private const string Repo = "/repos/me/nfty-public";

    private static FakeGitHub Script(string tree, string branches, string releases, string tags, string manifestJson = "{\"schemaVersion\":1,\"shares\":[]}")
    {
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(manifestJson));
        return new FakeGitHub()
            .Ok(HttpMethod.Get, Repo, "{\"full_name\":\"me/nfty-public\",\"private\":false,\"default_branch\":\"main\",\"size\":10}")
            .Ok(HttpMethod.Get, Repo + "/git/trees/main", tree)
            .Ok(HttpMethod.Get, Repo + "/branches", branches)
            .Ok(HttpMethod.Get, Repo + "/releases", releases)
            .Ok(HttpMethod.Get, Repo + "/tags", tags)
            .Ok(HttpMethod.Get, Repo + "/contents/nfty-public.json", $"{{\"sha\":\"m\",\"content\":\"{b64}\"}}");
    }

    private static GitHubShareClient Client(FakeGitHub fake) =>
        new(fake.Client(), "tok", CommunityEndpoints.Default);

    private const string DirtyTree =
        "{\"tree\":[{\"path\":\"README.md\",\"type\":\"blob\",\"sha\":\"r\",\"size\":1},{\"path\":\"nfty-public.json\",\"type\":\"blob\",\"sha\":\"m\",\"size\":1}," +
        "{\"path\":\"junk.txt\",\"type\":\"blob\",\"sha\":\"j\",\"size\":2},{\"path\":\"dir\",\"type\":\"tree\",\"sha\":\"d\"},{\"path\":\"dir/a.bin\",\"type\":\"blob\",\"sha\":\"da\",\"size\":3}]}";
    private const string DirtyReleases =
        "[{\"id\":11,\"tag_name\":\"old-v1\",\"assets\":[{\"id\":110,\"name\":\"old.set\",\"size\":1}]}]";

    [Fact]
    public async Task Fetch_answers_null_when_the_repo_does_not_exist()
    {
        var fake = new FakeGitHub().On(HttpMethod.Get, Repo, HttpStatusCode.NotFound, "{}");
        Assert.Null(await PublicRepoScan.FetchAsync(Client(fake), "me", [], default));
    }

    [Fact]
    public async Task Fetch_reads_a_damaged_manifest_as_none()
    {
        var fake = Script("{\"tree\":[]}", "[{\"name\":\"main\"}]", "[]", "[]", "not json");
        var snap = await PublicRepoScan.FetchAsync(Client(fake), "me", ["t"], default);
        Assert.Null(snap!.Manifest);
        Assert.Equal(["t"], snap.ProtectedTags);
    }

    [Fact]
    public async Task RemoveAsync_deletes_exactly_the_listed_items()
    {
        var fake = Script(DirtyTree, "[{\"name\":\"main\"},{\"name\":\"side\"},{\"name\":\"other\"}]", DirtyReleases, "[{\"name\":\"old-v1\"},{\"name\":\"stray\"}]");
        fake.On(HttpMethod.Delete, Repo + "/releases/11", HttpStatusCode.NoContent, "")
            .On(HttpMethod.Delete, Repo + "/git/refs/tags/old-v1", HttpStatusCode.NoContent, "")
            .Ok(HttpMethod.Delete, Repo + "/contents/dir/a.bin", "{}")
            .On(HttpMethod.Delete, Repo + "/git/refs/heads/side", HttpStatusCode.NoContent, "");
        // junk.txt, the "other" branch and the "stray" tag are extras too, but are NOT in the list: they must survive.
        var list = new[]
        {
            new RepoExtra(RepoExtraKind.Branch, "side", null, null),
            new RepoExtra(RepoExtraKind.File, "dir", 3, null),
            new RepoExtra(RepoExtraKind.Tag, "old-v1", null, null),
            new RepoExtra(RepoExtraKind.Release, "old-v1", 1, 11),
        };
        await PublicRepoScan.RemoveAsync(Client(fake), "me", list, [], default);

        var deletes = fake.Requests.Where(r => r.Method == HttpMethod.Delete).Select(r => r.Path).ToArray();
        Assert.Equal([Repo + "/releases/11", Repo + "/git/refs/tags/old-v1", Repo + "/contents/dir/a.bin", Repo + "/git/refs/heads/side"], deletes);
        var file = fake.Requests.Single(r => r.Path == Repo + "/contents/dir/a.bin" && r.Method == HttpMethod.Delete);
        using var body = System.Text.Json.JsonDocument.Parse(file.Body);
        Assert.Equal("da", body.RootElement.GetProperty("sha").GetString());
        Assert.Equal("main", body.RootElement.GetProperty("branch").GetString());
        fake.AssertAllConsumed();
    }

    [Fact]
    public async Task RemoveAsync_deletes_assets_before_releases_and_branches_last()
    {
        var tree = "{\"tree\":[{\"path\":\"README.md\",\"type\":\"blob\",\"sha\":\"r\",\"size\":1},{\"path\":\"nfty-public.json\",\"type\":\"blob\",\"sha\":\"m\",\"size\":1}]}";
        var manifest = System.Text.Encoding.UTF8.GetString(PublicRepoManifest.Write(new(1, [Entry("cat")])));
        var fake = Script(tree, "[{\"name\":\"main\"},{\"name\":\"side\"}]",
            "[{\"id\":1,\"tag_name\":\"cat-v1\",\"assets\":[{\"id\":10,\"name\":\"cat.set\",\"size\":1},{\"id\":12,\"name\":\"junk.zip\",\"size\":1}]}]",
            "[{\"name\":\"cat-v1\"}]", manifest);
        fake.On(HttpMethod.Delete, Repo + "/releases/assets/12", HttpStatusCode.NoContent, "")
            .On(HttpMethod.Delete, Repo + "/git/refs/heads/side", HttpStatusCode.NoContent, "");
        await PublicRepoScan.RemoveAsync(Client(fake), "me",
            [new RepoExtra(RepoExtraKind.Branch, "side", null, null), new RepoExtra(RepoExtraKind.Asset, "junk.zip", 1, 12)], [], default);
        Assert.Equal([Repo + "/releases/assets/12", Repo + "/git/refs/heads/side"],
            fake.Requests.Where(r => r.Method == HttpMethod.Delete).Select(r => r.Path));
        fake.AssertAllConsumed();
    }

    private const string CleanTree = "{\"tree\":[{\"path\":\"README.md\",\"type\":\"blob\",\"sha\":\"r\",\"size\":1},{\"path\":\"nfty-public.json\",\"type\":\"blob\",\"sha\":\"m\",\"size\":1}]}";
    private static string CatManifest => Encoding.UTF8.GetString(PublicRepoManifest.Write(new(1, [Entry("cat")])));
    private const string CatRelease = "{\"id\":1,\"tag_name\":\"cat-v1\",\"assets\":[{\"id\":10,\"name\":\"cat.set\",\"size\":1}]}";

    [Theory]
    [InlineData(RepoExtraKind.File, "README.md", null)]
    [InlineData(RepoExtraKind.File, "nfty-public.json", null)]
    [InlineData(RepoExtraKind.Branch, "main", null)]
    public async Task RemoveAsync_guards_the_files_and_branch_nfty_keeps(RepoExtraKind kind, string name, long? id)
    {
        var fake = Script(CleanTree, "[{\"name\":\"main\"}]", $"[{CatRelease}]", "[{\"name\":\"cat-v1\"}]", CatManifest);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            PublicRepoScan.RemoveAsync(Client(fake), "me", [new RepoExtra(kind, name, null, id)], [], default));
        Assert.DoesNotContain(fake.Requests, r => r.Method == HttpMethod.Delete);
    }

    [Theory]
    [InlineData(RepoExtraKind.Release, "cat-v1", 1L)]
    [InlineData(RepoExtraKind.Tag, "cat-v1", null)]
    [InlineData(RepoExtraKind.Asset, "cat.set", 10L)]
    public async Task RemoveAsync_refuses_something_now_allowed_with_the_changed_sentence(RepoExtraKind kind, string name, long? id)
    {
        var fake = Script(CleanTree, "[{\"name\":\"main\"}]", $"[{CatRelease}]", "[{\"name\":\"cat-v1\"}]", CatManifest);
        var ex = await Assert.ThrowsAsync<CommunityException>(() =>
            PublicRepoScan.RemoveAsync(Client(fake), "me", [new RepoExtra(kind, name, null, id)], [], default));
        Assert.Equal(ShareRules.RepoChanged, ex.Message);
        Assert.DoesNotContain(fake.Requests, r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task RemoveAsync_skips_an_item_that_is_already_gone()
    {
        var fake = Script(CleanTree, "[{\"name\":\"main\"}]", $"[{CatRelease}]", "[{\"name\":\"cat-v1\"}]", CatManifest);
        await PublicRepoScan.RemoveAsync(Client(fake), "me", [new RepoExtra(RepoExtraKind.File, "never-existed.txt", null, null),
            new RepoExtra(RepoExtraKind.Branch, "ghost", null, null), new RepoExtra(RepoExtraKind.Release, "x-v1", null, 99)], [], default);
        Assert.DoesNotContain(fake.Requests, r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task RemoveAsync_refuses_a_list_with_one_bad_item_before_deleting_any()
    {
        var fake = Script(DirtyTree, "[{\"name\":\"main\"}]", DirtyReleases, "[]");
        await Assert.ThrowsAsync<ArgumentException>(() => PublicRepoScan.RemoveAsync(Client(fake), "me",
            [new RepoExtra(RepoExtraKind.File, "junk.txt", 2, null), new RepoExtra(RepoExtraKind.File, "README.md", 1, null)], [], default));
        Assert.DoesNotContain(fake.Requests, r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task RemoveAsync_refuses_a_repository_nfty_did_not_make()
    {
        var fake = Script(DirtyTree, "[{\"name\":\"main\"}]", DirtyReleases, "[]", "not json");
        var ex = await Assert.ThrowsAsync<CommunityException>(() => PublicRepoScan.RemoveAsync(Client(fake), "me",
            [new RepoExtra(RepoExtraKind.File, "junk.txt", 2, null)], [], default));
        Assert.Equal(ShareRules.RepoNotOurs, ex.Message);
        Assert.DoesNotContain(fake.Requests, r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task RemoveAsync_keeps_a_pending_superseded_release()
    {
        var fake = Script(CleanTree, "[{\"name\":\"main\"}]", DirtyReleases, "[{\"name\":\"old-v1\"}]");
        var ex = await Assert.ThrowsAsync<CommunityException>(() => PublicRepoScan.RemoveAsync(Client(fake), "me",
            [new RepoExtra(RepoExtraKind.Release, "old-v1", 1, 11)], ["old-v1"], default));
        Assert.Equal(ShareRules.RepoChanged, ex.Message);
        Assert.DoesNotContain(fake.Requests, r => r.Method == HttpMethod.Delete);
    }

    // ---- protected tags (the live version while an update waits) -------------------------------------------------

    // nfty-public.json already names v2 while the gallery still lists v1: v1 is the LIVE share, never an extra.
    private static void OpenUpdate(Parts p, params string[] protectedTags)
    {
        p.Releases = [Rel(1, "cat", 1), Rel(2, "cat", 2)];
        p.Tags = ["cat-v1", "cat-v2"];
        p.Manifest = new PublicRepoManifest(1, [Entry("cat", 2)]);
        p.Pending = protectedTags;
    }

    [Fact]
    public void The_live_version_of_an_open_update_is_an_extra_only_when_nothing_protects_it()
    {
        Assert.Equal([RepoExtraKind.Release, RepoExtraKind.Tag], PublicRepoScan.Scan(Clean(p => OpenUpdate(p))).Extras.Select(e => e.Kind));
        Assert.Empty(PublicRepoScan.Scan(Clean(p => OpenUpdate(p, "cat-v1"))).Extras);
    }

    [Fact]
    public void Two_open_updates_offer_neither_the_live_version_nor_the_first_pending_one()
    {
        var scan = PublicRepoScan.Scan(Clean(p =>
        {
            p.Releases = [Rel(1, "cat", 1), Rel(2, "cat", 2), Rel(3, "cat", 3)];
            p.Tags = ["cat-v1", "cat-v2", "cat-v3"];
            p.Manifest = new PublicRepoManifest(1, [Entry("cat", 3)]);
            p.Pending = ["cat-v1", "cat-v2"];
        }));
        Assert.Empty(scan.Extras);
    }

    [Fact]
    public async Task Removing_the_live_version_of_an_open_update_is_refused()
    {
        var releases = "[{\"id\":1,\"tag_name\":\"cat-v1\",\"assets\":[{\"id\":10,\"name\":\"cat.set\",\"size\":1}]},"
            + "{\"id\":2,\"tag_name\":\"cat-v2\",\"assets\":[{\"id\":20,\"name\":\"cat.set\",\"size\":1}]}]";
        var fake = Script(CleanTree, "[{\"name\":\"main\"}]", releases, "[{\"name\":\"cat-v1\"},{\"name\":\"cat-v2\"}]",
            Encoding.UTF8.GetString(PublicRepoManifest.Write(new PublicRepoManifest(1, [Entry("cat", 2)]))));
        var ex = await Assert.ThrowsAsync<CommunityException>(() => PublicRepoScan.RemoveAsync(Client(fake), "me",
            [new RepoExtra(RepoExtraKind.Release, "cat-v1", 1, 1)], ["cat-v1"], default));
        Assert.Equal(ShareRules.RepoChanged, ex.Message);
        Assert.DoesNotContain(fake.Requests, r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public void A_listed_release_that_is_not_slug_dash_version_blocks_and_never_makes_asset_extras()
    {
        var scan = PublicRepoScan.Scan(Clean(p =>
        {
            p.Manifest = new PublicRepoManifest(1, [Entry("cat") with { Release = "other-tag" }]);
            p.Releases = [new GitHubRelease(1, "other-tag", [new GitHubAsset(10, "cat.set", 1), new GitHubAsset(11, "preview.png", 1), new GitHubAsset(12, "secret.bin", 1)], "me", "nfty-public")];
            p.Tags = ["other-tag"];
        }));
        Assert.Equal(ShareRules.RepoManifestDamaged, scan.Blocker);
        Assert.DoesNotContain(scan.Extras, e => e.Kind == RepoExtraKind.Asset);
    }

    // ---- submodules ----------------------------------------------------------------------------------------------

    [Fact]
    public void A_submodule_is_listed_but_not_removable()
    {
        var scan = PublicRepoScan.Scan(Clean(p => { p.Tree.Add(new GitHubTreeItem("vendor", "commit", "c", null)); p.Tree.Add(Blob("junk.txt", 2)); }));
        Assert.Equal([new RepoExtra(RepoExtraKind.File, "junk.txt", 2, null), new RepoExtra(RepoExtraKind.File, "vendor", null, null, Removable: false)], scan.Extras);
    }

    [Fact]
    public async Task RemoveAsync_refuses_a_submodule_with_its_sentence_and_deletes_nothing()
    {
        var tree = "{\"tree\":[{\"path\":\"README.md\",\"type\":\"blob\",\"sha\":\"r\",\"size\":1},{\"path\":\"nfty-public.json\",\"type\":\"blob\",\"sha\":\"m\",\"size\":1}," +
            "{\"path\":\"junk.txt\",\"type\":\"blob\",\"sha\":\"j\",\"size\":2},{\"path\":\"vendor\",\"type\":\"commit\",\"sha\":\"c\"}]}";
        var fake = Script(tree, "[{\"name\":\"main\"}]", "[]", "[]");
        var ex = await Assert.ThrowsAsync<CommunityException>(() => PublicRepoScan.RemoveAsync(Client(fake), "me",
            [new RepoExtra(RepoExtraKind.File, "junk.txt", 2, null), new RepoExtra(RepoExtraKind.File, "vendor", null, null)], [], default));
        Assert.Equal(ShareRules.RepoSubmodule("vendor"), ex.Message);
        Assert.DoesNotContain(fake.Requests, r => r.Method == HttpMethod.Delete);
    }

    // ---- two releases on one tag ---------------------------------------------------------------------------------

    [Fact]
    public void Two_releases_on_a_listed_tag_block_and_neither_is_an_extra()
    {
        var scan = PublicRepoScan.Scan(Clean(p => p.Releases = [Rel(2, "cat"), Rel(1, "cat")]));
        Assert.Equal(ShareRules.RepoDuplicateRelease("cat-v1"), scan.Blocker);
        Assert.Empty(scan.Extras);
    }

    [Fact]
    public async Task RemoveAsync_refuses_both_duplicate_releases()
    {
        var two = "[{\"id\":2,\"tag_name\":\"cat-v1\",\"assets\":[]},{\"id\":1,\"tag_name\":\"cat-v1\",\"assets\":[]}]";
        foreach (long id in new[] { 1L, 2L })
        {
            var fake = Script(CleanTree, "[{\"name\":\"main\"}]", two, "[{\"name\":\"cat-v1\"}]", CatManifest);
            var ex = await Assert.ThrowsAsync<CommunityException>(() => PublicRepoScan.RemoveAsync(Client(fake), "me",
                [new RepoExtra(RepoExtraKind.Release, "cat-v1", null, id)], [], default));
            Assert.Equal(ShareRules.RepoChanged, ex.Message);
            Assert.DoesNotContain(fake.Requests, r => r.Method == HttpMethod.Delete);
        }
    }

    // ---- the manifest's schema -----------------------------------------------------------------------------------

    [Theory]
    [InlineData(2, true)]
    [InlineData(0, false)]
    public async Task Fetch_turns_a_schema_it_cannot_read_into_a_house_sentence(int version, bool newer)
    {
        var fake = Script("{\"tree\":[]}", "[{\"name\":\"main\"}]", "[]", "[]", $"{{\"schemaVersion\":{version},\"shares\":[]}}");
        var ex = await Assert.ThrowsAsync<CommunityException>(() => PublicRepoScan.FetchAsync(Client(fake), "me", [], default));
        Assert.Equal(newer ? ShareRules.RepoNewerNfty : ShareRules.RepoNotOurs, ex.Message);
    }
}
