using System.Net;
using System.Text;
using System.Text.Json;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using Nfty.Core.Publish.Community;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using static System.Net.Http.HttpMethod;

namespace Nfty.Core.Tests.Community;

public class CommunityShareTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("nfty-communityshare-");
    private static readonly GitHubUser Me = new(7, "me", DateTimeOffset.Parse("2020-01-01T00:00:00Z"));
    private static readonly CommunityEndpoints Ends = CommunityEndpoints.Default;
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-09T12:00:00Z");
    private static readonly DateTimeOffset OldCreated = DateTimeOffset.Parse("2026-10-01T08:00:00Z");

    private const string Mine = "/repos/me/nfty-public";
    private const string Gallery = "/repos/vaporsoft-dev/nfty-gallery";
    private const string Fork = "/repos/me/nfty-gallery";

    public void Dispose() => _dir.Delete(recursive: true);

    private string Fresh() => _dir.CreateSubdirectory(Guid.NewGuid().ToString("N")).FullName;

    // ---- fixtures ----------------------------------------------------------------------------------------------

    private static LoadedCookBook Book(int side = 4)
    {
        var ids = Enumerable.Range(0, 8).Select(v => $"v{v}").ToList();
        var ingredient = new LoadedIngredient
        {
            Manifest = new IngredientManifest("l0", "Layer 0", LayerKind.Custom, null, ids.Select(v => new Variant(v, v.ToUpperInvariant(), 1)).ToList()),
            VariantImages = ids.Select((v, i) => (v, i)).ToDictionary(t => t.v, t => new Image<Rgba32>(side, side, new Rgba32((byte)(t.i * 20), 0, 3, 255))),
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Chest Demo", new Dimensions(side, side), new Collection("Chest Demo", "d", "CHST"),
                new Dictionary<string, double> { ["chest"] = 100 }),
            Recipes = [new LoadedRecipe { Manifest = new RecipeManifest("chest", "Chest", ["l0"], Array.Empty<IncompatibilityRule>()), Ingredients = [ingredient] }],
        };
    }

    private string CookedSet(int side = 4, string seed = "launch")
    {
        using var book = Book(side);
        string dir = Path.Combine(Fresh(), "ChestDemo");
        using var set = Generator.Generate(book, new GenerateOptions(2, seed));
        SetWriter.Write(set, dir, pack: false);
        return dir;
    }

    private async Task<string> FingerprintOf(string setDir)
    {
        string packed = SetExporter.Export(setDir, Fresh(), new ExportOptions()).Path;
        return (await ShareCheck.CheckAsync(packed)).Fingerprint;
    }

    private static CommunityEntry Entry(string slug, string collection, string fingerprint = "old", long ownerId = 7, string login = "me",
        DateTimeOffset? created = null, int version = 1) => new(
        1, slug, new CommunityOwner(ownerId, login), $"{login}/nfty-public", $"{slug}-v{version}", slug, collection, "", [],
        new CommunityCanvas(4, 4), 2, false, false, null,
        new CommunityFile($"https://x/{slug}.set", 10, "aa"), new CommunityFile("https://x/preview.png", 5, "bb"),
        fingerprint, "1.0.0", created ?? OldCreated, created ?? OldCreated);

    private static ShareRequest Request(string setPath, string? replaceSlug = null, bool replaceShared = false, string name = "Chest Demo") =>
        new(setPath, false, null, false, name, "A few chests.", ["props"], replaceSlug, replaceShared);

    private static GitHubShareClient Client(FakeGitHub fake) => new(fake.Client(), "tok", Ends);

    private static Task<ShareResult> Run(FakeGitHub fake, string tempRoot, ShareRequest request, CancellationToken ct = default,
        IReadOnlyCollection<string>? protectedTags = null) =>
        CommunityShare.RunAsync(Client(fake), Me, request, Ends, tempRoot, null, ct, protectedTags ?? [], () => Now, (_, _) => Task.CompletedTask);

    private static string IndexJson(params CommunityEntry[] entries) =>
        Encoding.UTF8.GetString(CommunityIndex.Write(new CommunityIndex(1, Now, entries)));

    private static string RepoJson(string fullName, string branch = "main") =>
        $"{{\"full_name\":\"{fullName}\",\"private\":false,\"default_branch\":\"{branch}\",\"size\":10}}";

    private static string FileJson(string content) =>
        $"{{\"sha\":\"sha-{content.Length}\",\"content\":\"{Convert.ToBase64String(Encoding.UTF8.GetBytes(content))}\"}}";

    private static string ReleaseJson(long id, string tag) => $"{{\"id\":{id},\"tag_name\":\"{tag}\",\"assets\":[]}}";

    private static string ManifestJson(params CommunityEntry[] shares) =>
        Encoding.UTF8.GetString(PublicRepoManifest.Write(new PublicRepoManifest(1, shares)));

    /// <summary>Scripts the gallery side every run reads before it uploads: the published list and the open pull requests.</summary>
    private static FakeGitHub Gallery_(FakeGitHub fake, params CommunityEntry[] listed) =>
        fake.Ok(Get, "/nfty-gallery/index.json", IndexJson(listed)).Ok(Get, Gallery + "/pulls", "[]");

    /// <summary>A repo that does not exist yet.</summary>
    private static FakeGitHub NoRepo(FakeGitHub fake) => fake.On(Get, Mine, HttpStatusCode.NotFound, "{}");

    /// <summary>Scripts what the scan reads from an existing, clean repository holding <paramref name="shares"/>.</summary>
    private static FakeGitHub Existing(FakeGitHub fake, params CommunityEntry[] shares)
    {
        var releases = shares.Select((s, i) =>
            $"{{\"id\":{100 + i},\"tag_name\":\"{s.Release}\",\"assets\":[{{\"id\":{1000 + i * 2},\"name\":\"{s.Slug}.set\",\"size\":10}},{{\"id\":{1001 + i * 2},\"name\":\"preview.png\",\"size\":5}}]}}");
        var tags = shares.Select(s => $"{{\"name\":\"{s.Release}\"}}");
        return fake
            .Ok(Get, Mine, RepoJson("me/nfty-public"))
            .Ok(Get, Mine + "/git/trees/main", "{\"tree\":[{\"path\":\"README.md\",\"type\":\"blob\",\"sha\":\"r\",\"size\":3},{\"path\":\"nfty-public.json\",\"type\":\"blob\",\"sha\":\"m\",\"size\":3}]}")
            .Ok(Get, Mine + "/branches", "[{\"name\":\"main\"}]")
            .Ok(Get, Mine + "/releases", "[" + string.Join(",", releases) + "]")
            .Ok(Get, Mine + "/tags", "[" + string.Join(",", tags) + "]")
            .Ok(Get, Mine + "/contents/nfty-public.json", FileJson(ManifestJson(shares)));
    }

    /// <summary>The release, upload, manifest and README calls of a run against an existing repo (the manifest is read twice: scan, then write).</summary>
    private static FakeGitHub Publish(FakeGitHub fake, string manifestJson, long releaseId, string tag)
    {
        fake.On(Post, Mine + "/releases", HttpStatusCode.Created, ReleaseJson(releaseId, tag))
            .On(Post, Mine + $"/releases/{releaseId}/assets", HttpStatusCode.Created, "{}")
            .On(Post, Mine + $"/releases/{releaseId}/assets", HttpStatusCode.Created, "{}")
            .Ok(Get, Mine + "/contents/nfty-public.json", FileJson(manifestJson))
            .Ok(Put, Mine + "/contents/nfty-public.json", "{}")
            .Ok(Get, Mine + "/contents/README.md", FileJson("# old"))
            .Ok(Put, Mine + "/contents/README.md", "{}");
        return fake;
    }

    /// <summary>The fork, branch, entry file and pull request calls.</summary>
    private static FakeGitHub Submit(FakeGitHub fake, string entryPath, string? existingEntry = null, params string[] removed)
    {
        fake.On(Post, Gallery + "/forks", HttpStatusCode.Accepted, RepoJson("me/nfty-gallery"))
            .Ok(Get, Fork, RepoJson("me/nfty-gallery"))
            .Ok(Get, Gallery, RepoJson("vaporsoft-dev/nfty-gallery"))
            .Ok(Get, Gallery, RepoJson("vaporsoft-dev/nfty-gallery"))
            .Ok(Get, Gallery + "/git/ref/heads/main", "{\"object\":{\"sha\":\"base\"}}")
            .Ok(Get, Fork + "/branches", "[]")
            .On(Post, Fork + "/git/refs", HttpStatusCode.Created, "{}");
        if (existingEntry is null) fake.On(Get, Fork + "/contents/" + entryPath, HttpStatusCode.NotFound, "{}");
        else fake.Ok(Get, Fork + "/contents/" + entryPath, FileJson(existingEntry));
        fake.Ok(Put, Fork + "/contents/" + entryPath, "{}");
        foreach (var path in removed)
            fake.Ok(Get, Fork + "/contents/" + path, FileJson("old entry")).Ok(Delete, Fork + "/contents/" + path, "{}");
        return fake.On(Post, Gallery + "/pulls", HttpStatusCode.Created, "{\"number\":5}");
    }

    private static string[] Changes(FakeGitHub fake) =>
        fake.Requests.Where(r => r.Method != Get).Select(r => $"{r.Method} {r.Path}").ToArray();

    private static byte[] PutContent(FakeGitHub.Recorded put) =>
        Convert.FromBase64String(JsonDocument.Parse(put.Body).RootElement.GetProperty("content").GetString()!);

    // ---- tests -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task First_share_creates_repo_release_manifest_and_pr()
    {
        string set = CookedSet();
        var fake = NoRepo(new FakeGitHub());
        Gallery_(fake);
        fake.On(Post, "/user/repos", HttpStatusCode.Created, RepoJson("me/nfty-public"));
        fake.On(Post, Mine + "/releases", HttpStatusCode.Created, ReleaseJson(11, "chest-demo-v1"))
            .On(Post, Mine + "/releases/11/assets", HttpStatusCode.Created, "{}")
            .On(Post, Mine + "/releases/11/assets", HttpStatusCode.Created, "{}")
            .On(Get, Mine + "/contents/nfty-public.json", HttpStatusCode.NotFound, "{}")
            .Ok(Put, Mine + "/contents/nfty-public.json", "{}")
            .Ok(Get, Mine + "/contents/README.md", FileJson("# auto"))
            .Ok(Put, Mine + "/contents/README.md", "{}");
        Submit(fake, "entries/7/chest-demo.json");

        var result = await Run(fake, Path.Combine(Fresh(), "t"), Request(set));

        Assert.Equal(5, result.PullNumber);
        Assert.Equal("chest-demo", result.Slug);
        Assert.Equal("chest-demo-v1", result.Tag);
        Assert.Equal("https://github.com/vaporsoft-dev/nfty-gallery/pull/5", result.PullUrl.ToString());
        Assert.Equal(
        [
            $"POST /user/repos", $"POST {Mine}/releases", $"POST {Mine}/releases/11/assets", $"POST {Mine}/releases/11/assets",
            $"PUT {Mine}/contents/nfty-public.json", $"PUT {Mine}/contents/README.md",
            $"POST {Gallery}/forks", $"POST {Fork}/git/refs", $"PUT {Fork}/contents/entries/7/chest-demo.json", $"POST {Gallery}/pulls",
        ], Changes(fake));

        var manifest = PublicRepoManifest.Read(new MemoryStream(PutContent(fake.To(Mine + "/contents/nfty-public.json").Single(r => r.Method == Put))));
        var entry = Assert.Single(manifest.Shares);
        Assert.Equal(Now, entry.Created);
        Assert.Equal(Now, entry.Updated);
        Assert.Equal("me/nfty-public", entry.Repo);
        Assert.Equal("chest-demo-v1", entry.Release);
        Assert.EndsWith("/chest-demo-v1/chest-demo.set", entry.Set.Url);
        var readme = Encoding.UTF8.GetString(PutContent(fake.To(Mine + "/contents/README.md").Single(r => r.Method == Put)));
        Assert.Contains("Chest Demo", readme);
        Assert.Contains("[Preview](", readme);
        Assert.DoesNotContain("<", readme);
        Assert.Equal("share/chest-demo-v1", JsonDocument.Parse(fake.To(Fork + "/git/refs").Single().Body).RootElement.GetProperty("ref").GetString()!["refs/heads/".Length..]);
        fake.AssertAllConsumed();
    }

    [Fact]
    public async Task A_set_that_fails_its_check_uploads_nothing()
    {
        string set = CookedSet(side: 600);
        var fake = NoRepo(new FakeGitHub());
        var ex = await Assert.ThrowsAsync<CommunityException>(() => Run(fake, Path.Combine(Fresh(), "t"), Request(set)));
        Assert.Equal(ShareRules.CanvasTooLarge(600, 600), ex.Message);
        Assert.Empty(Changes(fake));
    }

    [Fact]
    public async Task Words_are_checked_before_anything_else()
    {
        var fake = new FakeGitHub();
        var ex = await Assert.ThrowsAsync<CommunityException>(() => Run(fake, Fresh(), Request("x", name: new string('n', 65))));
        Assert.Equal(ShareRules.NameLength, ex.Message);
        var link = await Assert.ThrowsAsync<CommunityException>(() => Run(fake, Fresh(), Request("x") with { Description = "see https://x.example" }));
        Assert.Equal(ShareRules.NoLinks, link.Message);
        var tags = await Assert.ThrowsAsync<CommunityException>(() => Run(fake, Fresh(), Request("x") with { Tags = ["a", "b", "c", "d", "e", "f"] }));
        Assert.Equal(ShareRules.TooManyTags, tags.Message);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task At_three_shares_without_a_choice_is_refused()
    {
        string set = CookedSet();
        var fake = Existing(new FakeGitHub(), Entry("a", "A"), Entry("b", "B"), Entry("c", "C"));
        var ex = await Assert.ThrowsAsync<CommunityException>(() => Run(fake, Path.Combine(Fresh(), "t"), Request(set)));
        Assert.Equal(ShareRules.AtShareLimit, ex.Message);
        Assert.Empty(Changes(fake));
    }

    [Fact]
    public async Task Replacing_deletes_the_old_entry_in_the_same_pr()
    {
        string set = CookedSet();
        var fake = Existing(new FakeGitHub(), Entry("a", "A"), Entry("b", "B"), Entry("c", "C"));
        Gallery_(fake);
        Publish(fake, ManifestJson(Entry("a", "A"), Entry("b", "B"), Entry("c", "C")), 11, "chest-demo-v1");
        Submit(fake, "entries/7/chest-demo.json", null, "entries/7/a.json");

        var result = await Run(fake, Path.Combine(Fresh(), "t"), Request(set, replaceSlug: "a"));

        var onFork = fake.Requests.Where(r => r.Method != Get && r.Path.StartsWith(Fork + "/contents/entries/7/", StringComparison.Ordinal))
            .Select(r => $"{r.Method} {r.Path[(Fork.Length + "/contents/".Length)..]}").ToArray();
        Assert.Equal(["PUT entries/7/chest-demo.json", "DELETE entries/7/a.json"], onFork);
        Assert.Equal("a-v1", result.Pending.ReplacedTag);
        Assert.Null(result.Pending.SupersededTag);
        var manifest = PublicRepoManifest.Read(new MemoryStream(PutContent(fake.To(Mine + "/contents/nfty-public.json").Single(r => r.Method == Put))));
        Assert.Equal(["b", "c", "chest-demo"], manifest.Shares.Select(s => s.Slug));
        // Nothing of the replaced share is deleted from the author's repository by the run itself.
        Assert.DoesNotContain(Changes(fake), c => c.StartsWith("DELETE " + Mine, StringComparison.Ordinal));
        fake.AssertAllConsumed();
    }

    [Fact]
    public async Task An_update_is_v2_and_needs_no_slot()
    {
        string set = CookedSet();
        var old = Entry("chest-demo", "Chest Demo");
        var shares = new[] { old, Entry("a", "A"), Entry("b", "B") };
        var fake = Existing(new FakeGitHub(), shares);
        Gallery_(fake, shares);
        Publish(fake, ManifestJson(shares), 12, "chest-demo-v2");
        Submit(fake, "entries/7/chest-demo.json", "old");

        var result = await Run(fake, Path.Combine(Fresh(), "t"), Request(set, replaceShared: true));

        Assert.Equal("chest-demo-v2", result.Tag);
        Assert.Equal("chest-demo-v1", result.Pending.SupersededTag);
        Assert.Null(result.Pending.ReplacedTag);
        Assert.Equal("share/chest-demo-v2", JsonDocument.Parse(fake.To(Fork + "/git/refs").Single().Body).RootElement.GetProperty("ref").GetString()!["refs/heads/".Length..]);
        var manifest = PublicRepoManifest.Read(new MemoryStream(PutContent(fake.To(Mine + "/contents/nfty-public.json").Single(r => r.Method == Put))));
        Assert.Equal(["chest-demo", "a", "b"], manifest.Shares.Select(s => s.Slug));
        fake.AssertAllConsumed();
    }

    [Fact]
    public async Task An_update_without_the_replace_choice_is_refused()
    {
        string set = CookedSet();
        var fake = Existing(new FakeGitHub(), Entry("chest-demo", "Chest Demo"));
        var ex = await Assert.ThrowsAsync<CommunityException>(() => Run(fake, Path.Combine(Fresh(), "t"), Request(set)));
        Assert.Equal(ShareRules.ReplaceNotChosen, ex.Message);
        Assert.Empty(Changes(fake));
    }

    [Fact]
    public async Task An_update_keeps_created_and_moves_updated()
    {
        string set = CookedSet();
        var old = Entry("chest-demo", "Chest Demo");
        var fake = Existing(new FakeGitHub(), old);
        Gallery_(fake, old);
        Publish(fake, ManifestJson(old), 12, "chest-demo-v2");
        Submit(fake, "entries/7/chest-demo.json", "old");

        await Run(fake, Path.Combine(Fresh(), "t"), Request(set, replaceShared: true));

        var entry = JsonSerializer.Deserialize<CommunityEntry>(PutContent(fake.To(Fork + "/contents/entries/7/chest-demo.json").Single(r => r.Method == Put)), Json.Options)!;
        Assert.Equal(OldCreated, entry.Created);
        Assert.Equal(Now, entry.Updated);
        Assert.Equal("chest-demo-v2", entry.Release);
    }

    [Fact]
    public async Task A_first_share_the_gallery_refused_starts_its_dates_over_when_shared_again()
    {
        string set = CookedSet();
        // nfty-public.json still lists the refused v1 (the gallery's list never did), so sharing again is an update of it.
        var refused = Entry("chest-demo", "Chest Demo");
        var fake = Existing(new FakeGitHub(), refused);
        Gallery_(fake);
        Publish(fake, ManifestJson(refused), 12, "chest-demo-v2");
        Submit(fake, "entries/7/chest-demo.json", "old");

        await Run(fake, Path.Combine(Fresh(), "t"), Request(set, replaceShared: true));

        var entry = JsonSerializer.Deserialize<CommunityEntry>(PutContent(fake.To(Fork + "/contents/entries/7/chest-demo.json").Single(r => r.Method == Put)), Json.Options)!;
        Assert.Equal(Now, entry.Created);
        Assert.Equal(Now, entry.Updated);
    }

    [Fact]
    public async Task A_list_from_a_newer_nfty_is_the_house_sentence_not_the_exception_text()
    {
        string set = CookedSet();
        var fake = NoRepo(new FakeGitHub())
            .Ok(Get, "/nfty-gallery/index.json", "{\"schemaVersion\":99,\"generated\":\"2026-11-09T12:00:00+00:00\",\"entries\":[]}");
        var ex = await Assert.ThrowsAsync<CommunityException>(() => Run(fake, Path.Combine(Fresh(), "t"), Request(set)));
        Assert.Equal(ShareRules.IndexTooNew, ex.Message);
    }

    [Fact]
    public async Task A_set_listed_by_someone_else_uploads_nothing()
    {
        string set = CookedSet();
        string fingerprint = await FingerprintOf(set);
        var fake = NoRepo(new FakeGitHub()); // an account with no repo yet
        Gallery_(fake, Entry("theirs", "Chest Demo", fingerprint, ownerId: 99, login: "other"));
        var ex = await Assert.ThrowsAsync<CommunityException>(() => Run(fake, Path.Combine(Fresh(), "t"), Request(set)));
        Assert.Equal(ShareRules.AlreadyListed("other"), ex.Message);
        Assert.Empty(Changes(fake));
    }

    [Fact]
    public async Task A_set_in_an_open_pr_counts_as_listed()
    {
        string set = CookedSet();
        string fingerprint = await FingerprintOf(set);
        var theirs = Entry("theirs", "Chest Demo", fingerprint, ownerId: 99, login: "other");
        var fake = NoRepo(new FakeGitHub())
            .Ok(Get, "/nfty-gallery/index.json", IndexJson())
            .Ok(Get, Gallery + "/pulls", "[{\"number\":3,\"head\":{\"sha\":\"h3\",\"repo\":{\"full_name\":\"other/nfty-gallery\"}}}]")
            .Ok(Get, Gallery + "/pulls/3/files", "[{\"filename\":\"entries/99/theirs.json\",\"status\":\"added\",\"changes\":20}]")
            .Ok(Get, "/repos/other/nfty-gallery/contents/entries/99/theirs.json", JsonSerializer.Serialize(theirs, Json.Options));
        var ex = await Assert.ThrowsAsync<CommunityException>(() => Run(fake, Path.Combine(Fresh(), "t"), Request(set)));
        Assert.Equal(ShareRules.AlreadyListed("other"), ex.Message);
        Assert.Empty(Changes(fake));
    }

    [Fact]
    public async Task A_different_collection_on_a_taken_slug_is_refused()
    {
        string set = CookedSet();
        var fake = Existing(new FakeGitHub(), Entry("chest-demo", "Another Collection"));
        var ex = await Assert.ThrowsAsync<CommunityException>(() => Run(fake, Path.Combine(Fresh(), "t"), Request(set, replaceShared: true)));
        Assert.Equal(ShareRules.SlugTaken("chest-demo"), ex.Message);
        Assert.Empty(Changes(fake));
    }

    [Fact]
    public async Task A_failure_after_the_release_deletes_only_that_release()
    {
        string set = CookedSet();
        var a = Entry("a", "A");
        var fake = Existing(new FakeGitHub(), a);
        Gallery_(fake);
        fake.On(Post, Mine + "/releases", HttpStatusCode.Created, ReleaseJson(11, "chest-demo-v1"))
            .On(Post, Mine + "/releases/11/assets", HttpStatusCode.Created, "{}")
            .On(Post, Mine + "/releases/11/assets", HttpStatusCode.Created, "{}")
            .Ok(Get, Mine + "/contents/nfty-public.json", FileJson(ManifestJson(a)))
            .On(Put, Mine + "/contents/nfty-public.json", HttpStatusCode.InternalServerError, "{}")
            // the rollback
            .Ok(Get, Mine + "/contents/nfty-public.json", FileJson(ManifestJson(a)))
            .Ok(Get, Mine + "/releases", "[{\"id\":100,\"tag_name\":\"a-v1\",\"assets\":[]},{\"id\":11,\"tag_name\":\"chest-demo-v1\",\"assets\":[]}]")
            .On(Delete, Mine + "/releases/11", HttpStatusCode.NoContent, "")
            .Ok(Get, Mine + "/tags", "[{\"name\":\"a-v1\"},{\"name\":\"chest-demo-v1\"}]")
            .On(Delete, Mine + "/git/refs/tags/chest-demo-v1", HttpStatusCode.NoContent, "");

        var ex = await Assert.ThrowsAsync<CommunityException>(() => Run(fake, Path.Combine(Fresh(), "t"), Request(set)));

        Assert.Equal(ShareRules.GitHubRefused, ex.Message);
        Assert.Equal(
        [
            $"POST {Mine}/releases", $"POST {Mine}/releases/11/assets", $"POST {Mine}/releases/11/assets",
            $"PUT {Mine}/contents/nfty-public.json",
            $"DELETE {Mine}/releases/11", $"DELETE {Mine}/git/refs/tags/chest-demo-v1",
        ], Changes(fake));
    }

    [Fact]
    public async Task Cancel_during_upload_rolls_back()
    {
        string set = CookedSet();
        using var cts = new CancellationTokenSource();
        var fake = NoRepo(new FakeGitHub());
        Gallery_(fake);
        fake.On(Post, "/user/repos", HttpStatusCode.Created, RepoJson("me/nfty-public"))
            .On(Post, Mine + "/releases", HttpStatusCode.Created, ReleaseJson(11, "chest-demo-v1"))
            .On(Post, Mine + "/releases/11/assets", _ => { cts.Cancel(); return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{}") }; })
            // the rollback
            .Ok(Get, Mine + "/releases", "[{\"id\":11,\"tag_name\":\"chest-demo-v1\",\"assets\":[]}]")
            .On(Delete, Mine + "/releases/11", HttpStatusCode.NoContent, "")
            .Ok(Get, Mine + "/tags", "[{\"name\":\"chest-demo-v1\"}]")
            .On(Delete, Mine + "/git/refs/tags/chest-demo-v1", HttpStatusCode.NoContent, "")
            .On(Delete, Mine, HttpStatusCode.Forbidden, "{}");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(fake, Path.Combine(Fresh(), "t"), Request(set), cts.Token));

        Assert.Equal(
        [
            "POST /user/repos", $"POST {Mine}/releases", $"POST {Mine}/releases/11/assets",
            $"DELETE {Mine}/releases/11", $"DELETE {Mine}/git/refs/tags/chest-demo-v1", $"DELETE {Mine}",
        ], Changes(fake));
        fake.AssertAllConsumed();
    }

    [Fact]
    public async Task A_failure_while_listing_puts_the_manifest_back()
    {
        string set = CookedSet();
        var a = Entry("a", "A");
        string before = ManifestJson(a);
        var fake = Existing(new FakeGitHub(), a);
        Gallery_(fake);
        fake.On(Post, Mine + "/releases", HttpStatusCode.Created, ReleaseJson(11, "chest-demo-v1"))
            .On(Post, Mine + "/releases/11/assets", HttpStatusCode.Created, "{}")
            .On(Post, Mine + "/releases/11/assets", HttpStatusCode.Created, "{}")
            .Ok(Get, Mine + "/contents/nfty-public.json", FileJson(before))
            .Ok(Put, Mine + "/contents/nfty-public.json", "{}")
            .Ok(Get, Mine + "/contents/README.md", FileJson("# old"))
            .On(Put, Mine + "/contents/README.md", HttpStatusCode.InternalServerError, "{}")
            // the rollback runs newest first: README, then the manifest (now holding the new list), then the release
            .Ok(Get, Mine + "/contents/README.md", FileJson("# old"))
            .Ok(Get, Mine + "/contents/nfty-public.json", FileJson(ManifestJson(a, Entry("chest-demo", "Chest Demo"))))
            .Ok(Put, Mine + "/contents/nfty-public.json", "{}")
            .Ok(Get, Mine + "/releases", "[{\"id\":11,\"tag_name\":\"chest-demo-v1\",\"assets\":[]}]")
            .On(Delete, Mine + "/releases/11", HttpStatusCode.NoContent, "")
            .Ok(Get, Mine + "/tags", "[{\"name\":\"chest-demo-v1\"}]")
            .On(Delete, Mine + "/git/refs/tags/chest-demo-v1", HttpStatusCode.NoContent, "");

        await Assert.ThrowsAsync<CommunityException>(() => Run(fake, Path.Combine(Fresh(), "t"), Request(set)));

        var puts = fake.To(Mine + "/contents/nfty-public.json").Where(r => r.Method == Put).ToArray();
        Assert.Equal(2, puts.Length);
        Assert.Equal(before, Encoding.UTF8.GetString(PutContent(puts[1])));
        fake.AssertAllConsumed();
    }

    [Fact]
    public async Task Finish_deletes_superseded_release_and_tag()
    {
        var live = Entry("chest-demo", "Chest Demo", version: 2);
        var fake = new FakeGitHub()
            .Ok(Get, Gallery + "/pulls/5", "{\"state\":\"closed\",\"merged\":true}")
            .Ok(Get, Mine, RepoJson("me/nfty-public"))
            .Ok(Get, Mine + "/contents/nfty-public.json", FileJson(ManifestJson(live)))
            .Ok(Get, Mine + "/releases", "[{\"id\":1,\"tag_name\":\"chest-demo-v1\",\"assets\":[]},{\"id\":2,\"tag_name\":\"chest-demo-v2\",\"assets\":[]}]")
            .On(Delete, Mine + "/releases/1", HttpStatusCode.NoContent, "")
            .Ok(Get, Mine + "/tags", "[{\"name\":\"chest-demo-v1\"},{\"name\":\"chest-demo-v2\"}]")
            .On(Delete, Mine + "/git/refs/tags/chest-demo-v1", HttpStatusCode.NoContent, "");

        await CommunityShare.FinishAsync(Client(fake), Me, new PendingShare(5, "chest-demo", "chest-demo-v1", null), default);

        Assert.Equal([$"DELETE {Mine}/releases/1", $"DELETE {Mine}/git/refs/tags/chest-demo-v1"], Changes(fake));
        fake.AssertAllConsumed();
    }

    [Fact]
    public async Task Finish_waits_for_the_merge_and_spares_a_live_tag()
    {
        var open = new FakeGitHub().Ok(Get, Gallery + "/pulls/5", "{\"state\":\"open\",\"merged\":false}");
        await CommunityShare.FinishAsync(Client(open), Me, new PendingShare(5, "chest-demo", "chest-demo-v1", null), default);
        Assert.Empty(Changes(open));

        var live = Entry("chest-demo", "Chest Demo", version: 1);
        var stale = new FakeGitHub()
            .Ok(Get, Gallery + "/pulls/5", "{\"state\":\"closed\",\"merged\":true}")
            .Ok(Get, Mine, RepoJson("me/nfty-public"))
            .Ok(Get, Mine + "/contents/nfty-public.json", FileJson(ManifestJson(live)))
            .Ok(Get, Mine + "/releases", "[{\"id\":1,\"tag_name\":\"chest-demo-v1\",\"assets\":[]}]");
        await CommunityShare.FinishAsync(Client(stale), Me, new PendingShare(5, "chest-demo", "chest-demo-v1", null), default);
        Assert.Empty(Changes(stale));
    }

    [Fact]
    public async Task Unshare_opens_a_delete_pr_and_never_deletes_the_repo()
    {
        var mine = Entry("chest-demo", "Chest Demo");
        var other = Entry("a", "A");
        var fake = new FakeGitHub()
            .Ok(Get, Mine, RepoJson("me/nfty-public"))
            .Ok(Get, Mine + "/contents/nfty-public.json", FileJson(ManifestJson(mine, other)))
            .On(Post, Gallery + "/forks", HttpStatusCode.Accepted, RepoJson("me/nfty-gallery"))
            .Ok(Get, Fork, RepoJson("me/nfty-gallery"))
            .Ok(Get, Gallery, RepoJson("vaporsoft-dev/nfty-gallery"))
            .Ok(Get, Gallery, RepoJson("vaporsoft-dev/nfty-gallery"))
            .Ok(Get, Gallery + "/git/ref/heads/main", "{\"object\":{\"sha\":\"base\"}}")
            .Ok(Get, Fork + "/branches", "[]")
            .On(Post, Fork + "/git/refs", HttpStatusCode.Created, "{}")
            .Ok(Get, Fork + "/contents/entries/7/chest-demo.json", FileJson("entry"))
            .Ok(Delete, Fork + "/contents/entries/7/chest-demo.json", "{}")
            .Ok(Get, Mine + "/contents/nfty-public.json", FileJson(ManifestJson(mine, other)))
            .Ok(Put, Mine + "/contents/nfty-public.json", "{}")
            .Ok(Get, Mine + "/contents/README.md", FileJson("# old"))
            .Ok(Put, Mine + "/contents/README.md", "{}")
            .On(Post, Gallery + "/pulls", HttpStatusCode.Created, "{\"number\":8}");

        int number = await CommunityShare.UnshareAsync(Client(fake), Me, "chest-demo", Ends, default, (_, _) => Task.CompletedTask);

        Assert.Equal(8, number);
        var changes = Changes(fake);
        Assert.Contains($"DELETE {Fork}/contents/entries/7/chest-demo.json", changes);
        Assert.DoesNotContain(changes, c => c == $"DELETE {Mine}" || c.StartsWith($"DELETE {Mine}/", StringComparison.Ordinal));
        var manifest = PublicRepoManifest.Read(new MemoryStream(PutContent(fake.To(Mine + "/contents/nfty-public.json").Single(r => r.Method == Put))));
        Assert.Equal(["a"], manifest.Shares.Select(s => s.Slug));
        fake.AssertAllConsumed();
    }

    [Fact]
    public void The_export_is_the_asset_pack_as_one_unsealed_archive()
    {
        var options = CommunityShare.Options(Request("x") with { IncludeCookBook = true, CookBookPath = "b.cbk", Models = true });
        Assert.Equal(ExportShape.Archive, options.Shape);
        Assert.False(options.Sealed);
        Assert.True(options.IncludeCookBook);
        Assert.True(options.Models);
        Assert.True(options.NftyMetadata);
        Assert.Equal(ExportPreset.AssetPack, CommunityShare.Options(Request("x") with { Models = true }).MatchingPreset());
    }
}
