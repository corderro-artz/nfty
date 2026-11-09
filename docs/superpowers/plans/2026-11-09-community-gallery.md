# Community Gallery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A desktop page where anyone browses and downloads cooked Sets other people shared, and a
signed-in GitHub user shares up to three of their own through a `nfty-public` repo and the
`nfty-gallery` index.

**Architecture:** Core (`Nfty.Core/Publish/Community/`) owns every rule, sentence, schema, check
and GitHub call, UI-free and package-free; the CLI exposes `nfty share check` so the index repo's
Action runs the same code the client runs; the App adds a page, a share card and a GitHub
connection on the Settings PUBLISHING tab. The index repo's workflows and checker live in this repo
under `tools/gallery/` and are copied into `nfty-gallery` by the author.

**Tech Stack:** .NET 10, Avalonia 12.1.1, xunit.v3, `HttpClient` + `System.Text.Json`
(no new packages), Python 3 for `tools/gallery/` (stdlib only), GitHub REST API `2022-11-28`.

**Spec:** `docs/superpowers/specs/2026-11-09-community-gallery-design.md`

## Global Constraints

- **No new NuGet packages.** HTTP via `HttpClient`, JSON via `Json.Options`; Python stdlib only.
- **Naming:** code uses the `Community` prefix (`Nfty.Core.Publish.Community`, `CommunityIndex`,
  `CommunityViewModel`) because `Gallery*` is already the viewer's (`GalleryBuilder`,
  `GalleryItem`, `ArchiveGalleryViewModel`). User-facing words: page title **Community**, Landing
  button **Browse the community gallery**, repo **`nfty-gallery`**.
- Limits (spec 1, 3.2, 4): canvas **≤ 512 x 512**; **≤ 1,000** assets; packed `.set`
  **≤ 200 MB** (209,715,200 bytes); **≤ 3** shares per account; ZIP **≤ 6,000** entries,
  uncompressed **≤ 1 GiB**, per-entry ratio **≤ 100:1**; name **1–64** chars; description
  **≤ 500**; **≤ 5** tags; preview **≤ 2 MB** and **≤ 2048** px a side; repo history **≤ 5 MB**
  (GitHub `size` is KB: ≤ 5120); account age **≥ 30 days**; **≤ 1** new share per 24 h.
- Repo name exactly **`nfty-public`**, manifest **`nfty-public.json`**, Release tag
  **`<slug>-v<N>`**, assets exactly **`<slug>.set`** and **`preview.png`**.
- Never a sealed Set: refuse `ArchiveKind.Sealed` and any archive carrying a seal manifest,
  whatever the extension.
- Every refusal sentence lives in `ShareRules` and is shown verbatim by CLI, Action, card, page.
  No exception text on screen (`ErrorText` / house error card).
- GitHub token: `ISecretStore` target **`nfty/github`**; never in a file, never in a log; the card
  shows the login only. OAuth scope **`public_repo`**; client id is public and ships in the binary.
- Browsing/downloading never calls `api.github.com` (index from Pages, files from Release URLs).
- GUI house rules: page fits **1280 x 720** (page area ~1067 x 526 at `BaseScale` 1.2); modal card
  fits the real modal area unscrolled; reserve space, toggle ink (`Opacity`, not `IsVisible`, for
  anything occupying layout); token brushes only; `BitmapInterpolationMode="None"` on every
  `<Image` root; US English.
- All App waits on async Core use `UiThreadWait.For`, never `GetAwaiter().GetResult()`.
- Tests named `Snake_case_sentences`; fixtures built in memory or in
  `Directory.CreateTempSubdirectory()`; GitHub faked through an `HttpMessageHandler`.

## Review Focus

1. **A sealed Set renamed `.set`** (or a `.set` whose zip holds a seal manifest) must be refused
   on share, on download and in the Action, never opened. → Task 4 test
   `A_sealed_payload_renamed_set_is_refused`.
2. **A transfer that drops mid-way** (upload or download) must leave nothing half-made: no
   orphan Release, no partial file in the cache. → Task 10
   `A_failure_after_the_release_deletes_only_that_release`, Task 11
   `A_download_cut_short_leaves_nothing_in_the_cache`.
3. **A revoked or expired token** (401) must read `Reconnect GitHub to share.`, delete nothing,
   and leave browsing working. → Task 8 `A_401_becomes_the_reconnect_sentence`, Task 15
   `Browsing_works_with_a_revoked_token`.
4. **A collection name with no ASCII letters or digits** (`ドラゴン`, `!!!`) must still get a
   valid, stable slug. → Task 1 `A_name_without_ascii_letters_gets_a_hash_slug`.
5. **An index newer than this build** must not crash or blank the page: it keeps the cached list
   and says `The community gallery needs a newer nfty. Update to see the latest list.` → Task 2
   `A_newer_index_is_refused_by_name`, Task 14 `A_newer_index_keeps_the_cached_list`.

---

## Phase A — Core rules and checks

### Task 1: `ShareRules` and `ShareSlug`

**Files:**
- Create: `src/Nfty.Core/Publish/Community/ShareRules.cs`
- Create: `src/Nfty.Core/Publish/Community/ShareSlug.cs`
- Test: `tests/Nfty.Core.Tests/Community/ShareRulesTests.cs`, `tests/Nfty.Core.Tests/Community/ShareSlugTests.cs`

**Interfaces:**
- Produces: `public static class ShareRules` with `const int MaxCanvas = 512, MaxAssets = 1000,
  MaxShares = 3, MaxNameLength = 64, MaxDescriptionLength = 500, MaxTags = 5, MaxZipEntries = 6000,
  MaxEntryRatio = 100, MaxPreviewSide = 2048, MaxHistoryKb = 5120, MinAccountAgeDays = 30;
  const long MaxSetBytes = 209_715_200, MaxUncompressedBytes = 1_073_741_824, MaxPreviewBytes =
  2_097_152;` and one `public static string` method or const per refusal (list in Step 3).
- Produces: `public static class ShareSlug { static string Of(string collectionName); static bool
  IsValid(string slug); }`

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Slug_lowers_and_dashes() => Assert.Equal("vapor-chests", ShareSlug.Of("  Vapor  Chests!! "));
[Fact] public void Slug_is_at_most_forty() => Assert.Equal(40, ShareSlug.Of(new string('a', 90)).Length);
[Fact] public void Slug_never_ends_in_a_dash_after_trim() => Assert.Equal("ab", ShareSlug.Of("ab" + new string('-', 60) + "c")[..2]);
[Fact] public void A_name_without_ascii_letters_gets_a_hash_slug()
{
    var s = ShareSlug.Of("ドラゴン");
    Assert.Matches("^set-[0-9a-f]{8}$", s);
    Assert.Equal(s, ShareSlug.Of("ドラゴン"));          // stable
    Assert.NotEqual(s, ShareSlug.Of("!!!"));
}
[Fact] public void Valid_slug_rule() { Assert.True(ShareSlug.IsValid("a-1")); Assert.False(ShareSlug.IsValid("A")); Assert.False(ShareSlug.IsValid("-a")); }
[Fact] public void Canvas_sentence_names_both_sizes() =>
    Assert.Equal("The canvas is 640 x 640; the gallery takes up to 512 x 512.", ShareRules.CanvasTooLarge(640, 640));
[Fact] public void Size_sentence_names_the_models_fix() =>
    Assert.Equal("214 MB — the gallery takes up to 200 MB. Leave out 3D models (−160 MB) or share fewer assets.",
        ShareRules.SetTooLarge(224_395_264, modelsBytes: 167_772_160));
```

- [ ] **Step 2: Run** `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~Community` — Expected: FAIL (types missing).

- [ ] **Step 3: Implement.** Slug: lower-invariant, every run of non `[a-z0-9]` → `-`, trim `-`, cut
  to 40, trim `-` again; empty → `"set-" + first 8 hex of SHA-256(UTF-8 name)`. `IsValid`:
  `^[a-z0-9](?:[a-z0-9-]{0,38}[a-z0-9])?$`. Sizes in sentences use `SizeText` (existing,
  `Publish/SizeText.cs`). Sentences (exact copy, one member each):
  - `CanvasTooLarge(w,h)`, `TooManyAssets(n)` → `"This Set has {n:N0} assets; the gallery takes up to 1,000."`
  - `SetTooLarge(bytes, modelsBytes)` (models clause only when `modelsBytes > 0`)
  - `Sealed` → `"Sealed Sets can't be shared or come from the gallery."`
  - `NotASet` → `"This file isn't a Set nfty can open."`
  - `UnsafeArchive(reason)` → `"This Set's archive is unsafe to open: {reason}."` with reasons
    `"too many files"`, `"it expands too far"`, `"a file name leaves the archive"`, `"it holds a link"`
  - `UnexpectedFile(path)` → `"'{path}' isn't part of a Set."`
  - `ForeignBook` → `"The CookBook inside isn't the one that made this Set."`
  - `RenamedExport` → `"This is a marketplace export with renamed files, not a Set."`
  - `ImageUnreadable(file)`, `ImageWrongSize(file)`, `CountMismatch`, `CanvasMismatch`
  - `NameLength`, `DescriptionLength`, `NoLinks` → `"Names and descriptions can't contain links."`, `TooManyTags`, `UnknownTag(tag)`
  - `PreviewBad` → `"The preview picture is missing, too big or unreadable."`
  - `HashMismatch` → `"The download doesn't match what the gallery lists, so it was not opened."`
  - `AlreadyListed(login)` → `"This Set is already in the gallery, shared by @{login}."`
  - `AlreadySharedAs(name)` → `"You already share this Set as \"{name}\"."`
  - `NothingChanged(name, date)` → `"Nothing changed since you shared \"{name}\" on {d MMM yyyy}."` (invariant culture)
  - `ReplaceNotChosen` → `"Tick Replace the shared version to update it."`
  - `AtShareLimit` → `"You share 3 collections, the most the gallery takes. Pick one to replace."`
  - `SlugTaken(name)` → `"You already share a different collection as \"{name}\". Rename this one to share it too."`
  - `RepoNotOurs` / `RepoPrivate` → spec 3.2 sentence / `"Your nfty-public repository is private; the gallery reads only public ones."`
  - `RepoExtras` → `"Your nfty-public holds things the gallery doesn't allow. Remove them to share."`
  - `HistoryTooLarge(kb)` → spec 3.3 sentence
  - `Reconnect` → `"Reconnect GitHub to share."`
  - `Offline(time)` → `"GitHub can't be reached. Showing the list from {HH:mm}."`
  - `IndexTooNew` → `"The community gallery needs a newer nfty. Update to see the latest list."`

- [ ] **Step 4: Run** same filter — Expected: PASS.
- [ ] **Step 5: Commit** `feat(community): share limits, sentences and slugs`

### Task 2: `CommunityIndex` schema

**Files:**
- Create: `src/Nfty.Core/Publish/Community/CommunityEntry.cs`, `CommunityIndex.cs`, `PublicRepoManifest.cs`
- Test: `tests/Nfty.Core.Tests/Community/CommunityIndexTests.cs`

**Interfaces:**
- Produces records (all through `Json.Options`, `ISchemaVersioned`):
  `CommunityOwner(long Id, string Login)`, `CommunityCanvas(int Width, int Height)`,
  `CommunityFile(string Url, long Size, string Sha256)`,
  `CommunityEntry(int SchemaVersion, string Slug, CommunityOwner Owner, string Repo, string Release,
  string Name, string Collection, string Description, IReadOnlyList<string> Tags, CommunityCanvas
  Canvas, int Assets, bool Models, bool IncludesCookBook, string? CookbookSha256, CommunityFile Set,
  CommunityFile Preview, string Fingerprint, string NftyVersion, DateTimeOffset Created, DateTimeOffset Updated)`
  with `string Id => $"{Owner.Id}/{Slug}"`,
  `CommunityIndex(int SchemaVersion, DateTimeOffset Generated, IReadOnlyList<CommunityEntry> Entries)`,
  `PublicRepoManifest(int SchemaVersion, IReadOnlyList<CommunityEntry> Shares)`.
- Produces: `static CommunityIndex CommunityIndex.Read(Stream)`, `static PublicRepoManifest
  PublicRepoManifest.Read(Stream)`, `static byte[] PublicRepoManifest.Write(PublicRepoManifest)`,
  `static byte[] CommunityIndex.Write(CommunityIndex)` (entries sorted ordinally by
  `Owner.Id` then `Slug`), `static bool CommunityEntry.SameShare(CommunityEntry a, CommunityEntry b)`
  (every shared field equal; `Tags` compared as sequence; ignores `Updated`).
- Produces: `static class CommunityDuplicates { static string? Problem(CommunityEntry candidate,
  IEnumerable<CommunityEntry> listed); }` — spec 4.4a, returns the exact sentence or null:
  other owner + same `Fingerprint` or `Set.Sha256` ⇒ `ShareRules.AlreadyListed(login)`; same owner,
  other slug, same fingerprint ⇒ `ShareRules.AlreadySharedAs(name)`; same owner, same slug, same
  fingerprint ⇒ `ShareRules.NothingChanged(name, updated)`; else null.
- Produces: `static string? CommunityDates.Problem(CommunityEntry candidate, CommunityEntry? previous,
  DateTimeOffset prCreated)` — `Created` must equal `previous.Created` when previous exists;
  `Updated > previous.Updated`; `Updated <= prCreated + 1 h`; sentence
  `"The share's dates don't match its history."`.
- Produces: `CommunitySchema { const int Oldest = 1, Current = 1; }`; a newer version throws
  `UnsupportedSchemaVersionException` (existing type) whose message is `ShareRules.IndexTooNew`.

- [ ] **Step 1: Write failing tests:** `Round_trip_is_byte_identical`, `Unknown_fields_are_ignored`,
  `A_newer_index_is_refused_by_name` (schemaVersion 2 → throws, message ==
  `ShareRules.IndexTooNew`), `Write_sorts_ordinally_by_owner_then_slug`,
  `SameShare_ignores_updated_and_catches_a_changed_tag`,
  `Same_fingerprint_by_another_owner_is_already_listed`, `Same_file_hash_by_another_owner_is_already_listed`,
  `Own_set_under_another_slug_is_refused`, `Unchanged_update_is_refused_with_its_date`,
  `Changed_own_set_on_its_slug_is_not_a_duplicate`, `Dates_keep_created_and_move_updated_forward`,
  `Updated_ahead_of_the_pr_is_refused`.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~CommunityIndexTests` — FAIL.
- [ ] **Step 3: Implement** using `Json.Options`; reading via `JsonSerializer.Deserialize` then the
  version gate (mirror `ArchiveIo.ReadManifest`'s check, don't call it — no ZIP here).
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(community): index, entry and nfty-public schemas`

### Task 3: `ShareCheck` — archive safety

**Files:**
- Create: `src/Nfty.Core/Publish/Community/ShareCheck.cs` (archive half)
- Test: `tests/Nfty.Core.Tests/Community/ShareCheckArchiveTests.cs`

**Interfaces:**
- Produces: `public static class ShareCheck { static IReadOnlyList<string> CheckArchive(string setPath); }`
  — report, never throw; opens the zip with `ZipFile.OpenRead`, never extracts.

- [ ] **Step 1: Write failing tests** (build zips in a temp dir with `ZipArchive`):
  `A_clean_set_layout_passes` (empty list), `Too_many_entries_is_refused` (6,001 empty entries),
  `A_zip_bomb_by_ratio_is_refused` (one entry 10 MB of zeros → ratio > 100), `Total_expansion_over_one_gib_is_refused`
  (assert on `Length` metadata: write entries whose declared sizes sum past the cap using stored
  compression of a sparse file is impractical — instead unit-test the internal
  `CheckSizes(IEnumerable<(long compressed,long length)>)` seam marked `internal`),
  `Dot_dot_path_is_refused`, `Absolute_and_drive_paths_are_refused`, `Backslash_path_is_refused`,
  `A_symlink_entry_is_refused` (`ExternalAttributes = 0xA1FF << 16`),
  `Unexpected_root_file_is_refused` (`notes.txt`), `One_root_cbk_is_allowed_two_are_not`,
  `Over_200_mb_is_refused_before_opening` (file length check on a sparse temp file via
  `FileStream.SetLength`). Each asserts the exact `ShareRules` sentence.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~ShareCheckArchiveTests` — FAIL.
- [ ] **Step 3: Implement.** Allowed: `set.json`, entries under `images/`, `metadata/`, `nfty/`,
  `models/` (the `SetLayout` include list — read the directory names from `SetLayout`, don't
  restate), plus at most one root `*.cbk`. Ratio = `Length / max(CompressedLength,1)`.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(community): archive safety checks`

### Task 4: `ShareCheck` — the Set itself

**Files:**
- Modify: `src/Nfty.Core/Publish/Community/ShareCheck.cs`
- Test: `tests/Nfty.Core.Tests/Community/ShareCheckSetTests.cs`

**Interfaces:**
- Consumes: `CheckArchive` (Task 3), `SetReader.ReadAsync(path, ct)`, `Archives.KindOf`,
  `ImageFormats.Identify(path)`, `SetManifest.Files`, `SetManifest.CookbookSha256`.
- Produces: `static Task<ShareFacts> CheckAsync(string setPath, CancellationToken ct = default)`
  with `public sealed record ShareFacts(IReadOnlyList<string> Problems, int Assets, int Width,
  int Height, bool Models, bool IncludesCookBook, string? CookbookSha256, string Collection,
  string Fingerprint, string FileSha256)`. Fingerprint = lowercase hex SHA-256 of UTF-8
  `string.Join('\n', items.OrderBy(i => i.Number).Select(i => i.Dna))`.
  Runs `CheckArchive` first and returns early on any archive problem.

- [ ] **Step 1: Write failing tests** (cook a tiny real Set with `Generator` + `SetWriter.Write` +
  `SetWriter.Pack`, as `SetExporterTests` does): `A_tiny_cooked_set_passes_with_its_facts`,
  `A_sealed_payload_renamed_set_is_refused` (export `SealedCritique` with a passphrase, rename
  `.tin`→`.set`), `A_513_canvas_is_refused`, `A_1001_asset_set_is_refused` (1×1 canvas, cooked
  once), `A_renamed_marketplace_export_is_refused` (`Files` block present), `A_foreign_cbk_is_refused`,
  `The_sets_own_cbk_is_allowed`, `A_damaged_image_is_named`, `Count_in_set_json_must_match_images`,
  `Fingerprint_survives_repacking_and_leaving_models_out` (same Set packed twice, once without
  `models/` and once with the `.cbk`: equal fingerprints, different `FileSha256`),
  `Fingerprint_changes_when_the_set_is_extended` (`extend` by one asset).
- [ ] **Step 2: Run** `--filter FullyQualifiedName~ShareCheckSetTests` — FAIL.
- [ ] **Step 3: Implement.** Sealed detection before `SetReader`: `Archives.TryKindOf` says
  Sealed, or the zip holds the seal manifest entry name `SealedSetReader` reads (reuse its constant;
  make it `internal` + `InternalsVisibleTo` already covers tests). Canvas from the first item's
  `ImageFormats.Identify`; every other image must match. Never `Image.Load` directly.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(community): set-level share checks`

### Task 5: `nfty share check`

**Files:**
- Create: `src/Nfty.Cli/CommandFactory.Share.cs`
- Modify: `src/Nfty.Cli/CommandFactory.cs` (register `ShareGroup()` beside `IpfsGroup()`, line ~126)
- Test: `tests/Nfty.Cli.Tests/ShareCommandTests.cs`

**Interfaces:**
- Consumes: `ShareCheck.CheckAsync`.
- Produces: `nfty share check <file.set>` — prints each problem on its own line to stdout, exit 1 if
  any; on pass prints `ok: {assets} assets · {w} x {h}{ · models}{ · book}` and exits 0.
  `--json` prints `ShareFacts` through `Json.Options` (the Action reads it).

- [ ] **Step 1: Write failing tests:** `Share_check_passes_a_clean_set`, `Share_check_fails_a_sealed_set_with_exit_1`,
  `Share_check_json_has_assets_and_canvas`.
- [ ] **Step 2: Run** `dotnet test tests/Nfty.Cli.Tests --filter FullyQualifiedName~ShareCommandTests` — FAIL.
- [ ] **Step 3: Implement** in the `CommandFactory.Ipfs.cs` style; no try/catch (Program's
  `ErrorReport` handles throws).
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(cli): nfty share check`

### Task 6: `CommunityPreview`

**Files:**
- Create: `src/Nfty.Core/Publish/Community/CommunityPreview.cs`
- Test: `tests/Nfty.Core.Tests/Community/CommunityPreviewTests.cs`

**Interfaces:**
- Consumes: `SpriteSheet.Fit`, `SpriteSheet.WriteAsync(items, layout, destination, …)`, `SheetGrid.Read`.
- Produces: `static int CommunityPreview.CellCount(int count, int cellW, int cellH)` and
  `static Task CommunityPreview.WriteAsync(IReadOnlyList<SetItem> items, string destination, CancellationToken ct)`;
  `static IReadOnlyList<string> CommunityPreview.Check(string pngPath, int canvasW, int canvasH)`.

- [ ] **Step 1: Write failing tests:** `Thirty_two_px_art_takes_sixty_four_cells`
  (`CellCount(600,32,32)==64`), `Five_twelve_px_art_takes_sixteen` (`==16`), `Never_more_than_the_set`
  (`CellCount(5,32,32)==5`), `Sheet_side_never_exceeds_2048` (sweep sizes 1..512),
  `The_written_preview_carries_its_grid_and_passes_check`, `Check_refuses_wrong_cell_size`.
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement.** `CellCount` = largest n ≤ min(64, count) whose `SpriteSheet.Fit(n, w, h)`
  stays ≤ 2048 a side. `Check` = size ≤ `MaxPreviewBytes`, `ImageFormats.Identify` ≤ 2048, `SheetGrid.Read` cell == canvas.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(community): preview sheet`

## Phase B — GitHub

### Task 7: `GitHubDeviceFlow`

**Files:**
- Create: `src/Nfty.Core/Publish/Community/GitHubDeviceFlow.cs`
- Test: `tests/Nfty.Core.Tests/Community/GitHubDeviceFlowTests.cs` (+ `tests/Nfty.Core.Tests/Community/FakeGitHub.cs`, a scripted `HttpMessageHandler` recording method + path + body)

**Interfaces:**
- Produces: `sealed class GitHubDeviceFlow(HttpClient http, string clientId, Func<TimeSpan, CancellationToken, Task>? delay = null)`;
  `Task<DeviceCode> StartAsync(CancellationToken)`; `Task<string> WaitForTokenAsync(DeviceCode, CancellationToken)`;
  `record DeviceCode(string Device, string UserCode, Uri VerificationUri, TimeSpan ExpiresIn, TimeSpan Interval)`;
  `GitHubAuthException(string message)` carrying a `ShareRules` sentence.
- Endpoints: `POST https://github.com/login/device/code` (`client_id`, `scope=public_repo`),
  `POST https://github.com/login/oauth/access_token` (`client_id`, `device_code`,
  `grant_type=urn:ietf:params:oauth:grant-type:device_code`), both `Accept: application/json`.

- [ ] **Step 1: Write failing tests:** `Start_asks_for_public_repo`, `Pending_then_token_returns_the_token`,
  `Slow_down_adds_five_seconds` (delay hook records `[5s, 10s]` for interval 5), `Denied_says_so`
  (`"GitHub sign-in was canceled."`), `Expired_says_so` (`"The sign-in code expired. Try again."`),
  `Cancellation_stops_polling`.
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement** (form-urlencoded bodies). **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(community): GitHub device flow`

### Task 8: `GitHubShareClient`

**Files:**
- Create: `src/Nfty.Core/Publish/Community/GitHubShareClient.cs`, `GitHubModels.cs`
- Test: `tests/Nfty.Core.Tests/Community/GitHubShareClientTests.cs`

**Interfaces:**
- Produces: `sealed class GitHubShareClient(HttpClient http, string token, CommunityEndpoints endpoints)`
  — every request sends `Authorization: Bearer`, `Accept: application/vnd.github+json`,
  `X-GitHub-Api-Version: 2022-11-28`, `User-Agent: nfty`. Methods (all `CancellationToken ct`):
  `GetViewerAsync() -> GitHubUser(long Id, string Login, DateTimeOffset CreatedAt)`;
  `GetRepoAsync(owner, name) -> GitHubRepo?(string FullName, bool Private, string DefaultBranch, long SizeKb)`;
  `CreateRepoAsync(name, description) -> GitHubRepo` (`auto_init:true`);
  `GetTreeAsync(owner, repo, branch) -> IReadOnlyList<GitHubTreeItem(string Path, string Type, string Sha, long? Size)>`;
  `ListBranchesAsync`, `DeleteBranchAsync(owner, repo, branch)`;
  `GetFileAsync(owner, repo, path, branch) -> (byte[] Content, string Sha)?`;
  `PutFileAsync(owner, repo, path, byte[] content, string message, string? sha, string branch)`;
  `DeleteFileAsync(owner, repo, path, string sha, string message, string branch)`;
  `ListReleasesAsync -> IReadOnlyList<GitHubRelease(long Id, string Tag, IReadOnlyList<GitHubAsset(long Id, string Name, long Size)>)>`;
  `CreateReleaseAsync(owner, repo, tag, name) -> GitHubRelease`;
  `UploadAssetAsync(GitHubRelease, string name, Stream content, long length, IProgress<long>?)` → `uploads.github.com`, `application/octet-stream`, streamed;
  `DeleteReleaseAsync(owner, repo, id)`; `ListTagsAsync`; `DeleteTagAsync(owner, repo, tag)`;
  `ForkAsync(galleryRepo) -> GitHubRepo` (returns existing fork when present);
  `CreateBranchAsync(owner, repo, branch, fromSha)`; `GetBranchShaAsync`;
  `OpenPullAsync(galleryRepo, head, title, body) -> int`;
  `GetPullAsync(galleryRepo, number) -> GitHubPull(string State, bool Merged)`;
  `GetPullCommentsAsync(galleryRepo, number) -> IReadOnlyList<string>`.
- Produces: `record CommunityEndpoints(string GalleryRepo, Uri IndexUrl, Uri ApiBase, Uri UploadsBase, string ClientId)`
  with `static CommunityEndpoints Default` (GalleryRepo and ClientId from Task 20).
- Errors: 401 → `GitHubAuthException(ShareRules.Reconnect)`; 403 with `x-ratelimit-remaining: 0`
  → `"GitHub is limiting requests. Try again in {minutes} minutes."`; other non-success →
  `CommunityException("GitHub refused the request.")`; network → `CommunityException(ShareRules.Offline…)`.

- [ ] **Step 1: Write failing tests:** one per method asserting method + path (+ body JSON keys) on
  `FakeGitHub`; plus `A_401_becomes_the_reconnect_sentence`, `Rate_limit_names_the_wait`,
  `Upload_streams_without_buffering` (fake reads the request stream in 64 KB chunks and asserts
  `Content-Length` set, no `Transfer-Encoding: chunked`), `Path_segments_are_escaped`.
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement.** **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(community): GitHub REST client for sharing`

### Task 9: `PublicRepoScan`

**Files:**
- Create: `src/Nfty.Core/Publish/Community/PublicRepoScan.cs`
- Test: `tests/Nfty.Core.Tests/Community/PublicRepoScanTests.cs`

**Interfaces:**
- Consumes: `GitHubShareClient` list/get/delete methods, `PublicRepoManifest`.
- Produces: `record RepoSnapshot(GitHubRepo Repo, IReadOnlyList<GitHubTreeItem> Tree,
  IReadOnlyList<string> Branches, IReadOnlyList<GitHubRelease> Releases, IReadOnlyList<string> Tags,
  PublicRepoManifest? Manifest, string? PendingSupersededTag)`;
  `enum RepoExtraKind { File, Branch, Release, Asset, Tag }`;
  `record RepoExtra(RepoExtraKind Kind, string Name, long? Size, long? Id)`;
  `record RepoScan(IReadOnlyList<RepoExtra> Extras, string? Blocker)` (Blocker = not-ours, private,
  history-too-large sentence);
  `static RepoScan PublicRepoScan.Scan(RepoSnapshot)` (pure);
  `static Task<RepoSnapshot> PublicRepoScan.FetchAsync(GitHubShareClient, string login, string? pendingTag, CancellationToken)`;
  `static Task PublicRepoScan.RemoveAsync(GitHubShareClient, string login, IReadOnlyList<RepoExtra>, CancellationToken)` — deletes exactly those items.

- [ ] **Step 1: Write failing tests** (pure `Scan` over hand-built snapshots):
  `A_clean_repo_has_no_extras`, `A_stray_file_is_an_extra`, `A_second_branch_is_an_extra`,
  `An_unlisted_release_is_an_extra`, `A_fourth_listed_release_is_an_extra`, `A_stray_asset_is_an_extra`,
  `An_orphan_tag_is_an_extra`, `History_over_5_mb_blocks` (SizeKb 5121 → Blocker ==
  `ShareRules.HistoryTooLarge(5121)`), `A_superseded_release_passes_only_while_pending`,
  `No_manifest_means_not_ours`, `Private_blocks`; and `RemoveAsync_deletes_exactly_the_listed_items`
  against `FakeGitHub` (asserts the DELETE paths, nothing else).
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement** per spec 3.3 table. **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(community): nfty-public scan`

### Task 10: `CommunityShare` — the share run

**Files:**
- Create: `src/Nfty.Core/Publish/Community/CommunityShare.cs`
- Test: `tests/Nfty.Core.Tests/Community/CommunityShareTests.cs`

**Interfaces:**
- Consumes: Tasks 1–9, `SetExporter.ExportAsync`, `ExportOptions.For(ExportPreset.AssetPack)`.
- Produces: `record ShareRequest(string SetPath, bool IncludeCookBook, string? CookBookPath, bool Models,
  string Name, string Description, IReadOnlyList<string> Tags, string? ReplaceSlug,
  bool ReplaceSharedVersion)`;
  `static ExportOptions CommunityShare.Options(ShareRequest)` (AssetPack, `Shape=Archive`,
  `Sealed=false`, `IncludeCookBook`, `Models`) — the card calls `SetExporter.Plan` with exactly this;
  `enum ShareStage { Scanning, Exporting, Checking, Preview, Uploading, Listing, Submitting }`;
  `record ShareProgress(ShareStage Stage, double Fraction)`;
  `record ShareResult(int PullNumber, Uri PullUrl, string Slug, string Tag)`;
  `static Task<ShareResult> CommunityShare.RunAsync(GitHubShareClient, GitHubUser, ShareRequest,
  CommunityEndpoints, string tempRoot, IProgress<ShareProgress>?, CancellationToken)`;
  `static Task CommunityShare.FinishAsync(GitHubShareClient, GitHubUser, PendingShare, CancellationToken)`
  — after merge, deletes superseded/replaced Release + tag;
  `static Task CommunityShare.UnshareAsync(GitHubShareClient, GitHubUser, string slug, CommunityEndpoints, CancellationToken) -> int` (PR number);
  `record PendingShare(int PullNumber, string Slug, string? SupersededTag, string? ReplacedTag)`.
- Order (spec 7.2): scan → export to temp → `ShareCheck.CheckAsync` (problems ⇒ `CommunityException(first problem)`, nothing uploaded)
  → preview → repo (create or open; `PublicRepoScan` blocker ⇒ stop) → Release `<slug>-v<N>`
  (N = 1 + highest existing for slug) → upload both → rewrite `nfty-public.json` + `README.md` →
  fork, branch `share/<slug>-v<N>`, put `entries/<id>/<slug>.json` (+ delete replaced entry) → open PR.
- Slug rule: slug already shared with a different `Collection` ⇒ `ShareRules.SlugTaken`; same
  collection ⇒ update (no limit check) only when `ReplaceSharedVersion`, else `ShareRules.ReplaceNotChosen`;
  else at 3 shares and `ReplaceSlug == null` ⇒ `ShareRules.AtShareLimit`.
- Duplicates: after `ShareCheck`, build the candidate entry and run
  `CommunityDuplicates.Problem(candidate, listed)` where `listed` = the freshly fetched
  `index.json` (`http GET endpoints.IndexUrl`, no API) plus the entries of the gallery's open PRs
  (one API call, `GET /repos/{gallery}/pulls?state=open` + their `entries/**` files); a problem
  stops the run before any upload. Dates: first share `Created = Updated = now (UTC)`; update keeps
  the listed `Created`, `Updated = now`.
- Rollback on failure/cancel: delete the Release this run created, its tag, the repo if this run
  created it, and restore the previous `nfty-public.json`/`README.md` contents; never touch the
  replaced share. Cleanup uses `CancellationToken.None` with a 30 s bound (the `MarketplaceUpload` rule).

- [ ] **Step 1: Write failing tests** on `FakeGitHub` with a tiny cooked Set:
  `First_share_creates_repo_release_manifest_and_pr` (asserts call order),
  `A_set_that_fails_its_check_uploads_nothing`, `At_three_shares_without_a_choice_is_refused`,
  `Replacing_deletes_the_old_entry_in_the_same_pr` (PR branch has one PUT + one DELETE under
  `entries/<id>/`), `An_update_is_v2_and_needs_no_slot`, `An_update_without_the_replace_choice_is_refused`,
  `An_update_keeps_created_and_moves_updated`, `A_set_listed_by_someone_else_uploads_nothing`,
  `A_set_in_an_open_pr_counts_as_listed`, `A_different_collection_on_a_taken_slug_is_refused`,
  `A_failure_after_the_release_deletes_only_that_release`, `Cancel_during_upload_rolls_back`,
  `Finish_deletes_superseded_release_and_tag`, `Unshare_opens_a_delete_pr_and_never_deletes_the_repo`.
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement.** **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(community): share, update, replace, unshare`

### Task 11: `CommunityDownload`

**Files:**
- Create: `src/Nfty.Core/Publish/Community/CommunityDownload.cs`
- Test: `tests/Nfty.Core.Tests/Community/CommunityDownloadTests.cs`

**Interfaces:**
- Produces: `static Task<string> CommunityDownload.FetchAsync(HttpClient http, CommunityEntry entry,
  string cacheDir, IProgress<double>? progress, CancellationToken ct)` → path
  `<cacheDir>/<sha256>.set`; returns at once when that file exists and its hash matches.
- Steps: stream to `<cacheDir>/<sha256>.part` (abort past `entry.Set.Size` bytes), compare size + SHA-256
  (`ShareRules.HashMismatch`), `ShareCheck.CheckAsync` (problems ⇒ `CommunityException(first)`),
  facts must equal entry assets/canvas (`CountMismatch`/`CanvasMismatch`), then atomic move.
  Any failure deletes the `.part`.

- [ ] **Step 1: Write failing tests:** `A_matching_download_lands_in_the_cache`,
  `A_hash_mismatch_is_refused_and_leaves_nothing`, `A_download_cut_short_leaves_nothing_in_the_cache`
  (handler throws `IOException` mid-stream), `A_server_sending_more_than_listed_is_cut_off`,
  `A_sealed_set_in_the_index_is_refused`, `A_cached_copy_is_reused_without_a_request`.
- [ ] **Step 2–4:** run FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat(community): verified download`

## Phase C — the index repo (`tools/gallery/`)

### Task 12: Gallery repo template and checker

**Files:**
- Create: `tools/gallery/repo/.github/workflows/check.yml`, `merge.yml`, `publish.yml`, `health.yml`
- Create: `tools/gallery/repo/.github/ISSUE_TEMPLATE/report.yml`, `tools/gallery/repo/blocked.json` (`{"users":[],"sets":[]}`),
  `tools/gallery/repo/tags.json`, `tools/gallery/repo/README.md`
- Create: `tools/gallery/check.py`, `tools/gallery/build_index.py`, `tools/gallery/health.py`, `tools/gallery/README.md`
- Test: `tools/tests/test_gallery.py` (unittest, stdlib; recorded API JSON under `tools/tests/gallery_fixtures/`)

**Interfaces:**
- `check.py --pr-files <json> --author <json> --repo <json> --tree <json> --releases <json>
  --entries-dir <path> --blocked <path> --tags <path> --set <file.set> --preview <file.png> --nfty <cli-path>`
  → exit 0 or 1, writes `comment.md` with one `ShareRules` sentence per line (sentences come from
  `nfty share check` stdout; the account/repo sentences are copied from `ShareRules` and
  `test_gallery.py` asserts each copy equals the C# constant by grepping `ShareRules.cs`).
- `check.yml`: `on: pull_request` (paths `entries/**`), `permissions: contents: read`; downloads the
  pinned nfty CLI release (`NFTY_VERSION` env), fetches API JSON with the read-only `GITHUB_TOKEN`,
  runs `check.py` (the preview is checked by `nfty sheet grid <preview.png>` for the grid and the cell size, plus file size and side in Python), uploads `comment.md` + `verdict.json` as an artifact. Never checks out PR code to
  execute: reads changed files with `gh api` (`repos/{repo}/pulls/{n}/files`).
- `merge.yml`: `on: workflow_run` of check, `permissions: contents: write, pull-requests: write`;
  downloads the artifact; green ⇒ `gh pr merge --squash`; red ⇒ comment `comment.md`, close.
- `publish.yml`: on push to main ⇒ `build_index.py entries/ > site/index.json` (ordinal sort, same
  bytes as `CommunityIndex.Write`), deploy Pages.
- `health.yml`: nightly cron `17 3 * * *` ⇒ `health.py` HEADs each set + preview, keeps a
  `health.json` miss counter, marks `"status": "gone"` after 3 consecutive misses, commits.

- [ ] **Step 1: Write failing tests:** `test_clean_pr_passes`, `test_entry_outside_own_id_dir_fails`,
  `test_fourth_share_fails`, `test_young_account_fails`, `test_second_new_share_in_a_day_fails`,
  `test_blocked_user_fails`, `test_repo_name_must_be_nfty_public`, `test_manifest_must_match_entry`,
  `test_repo_scan_extras_fail` (mirrors Task 9 table), `test_set_over_200mb_fails`,
  `test_links_in_description_fail`, `test_unknown_tag_fails`, `test_sentences_match_csharp`,
  `test_duplicate_fingerprint_by_another_owner_fails`, `test_duplicate_file_hash_fails`,
  `test_unchanged_update_fails`, `test_duplicate_in_another_open_pr_fails`,
  `test_created_must_be_kept_and_updated_must_advance`, `test_fingerprint_must_equal_cli_output`
  (the entry's `fingerprint` must equal `nfty share check --json`'s — the Action never trusts the
  entry's own claim),
  `test_build_index_matches_csharp_ordering` (fixture of 3 entries, expected bytes produced once by a
  Core test that writes `tools/tests/gallery_fixtures/index.expected.json` — the Core test
  `Index_fixture_for_python_is_current` asserts that file is up to date).
- [ ] **Step 2: Run** `python -m unittest tools.tests.test_gallery -v` — FAIL.
- [ ] **Step 3: Implement** the scripts (stdlib `json`, `hashlib`, `urllib`); workflows as above.
- [ ] **Step 4: Run** — PASS; `python -m unittest tools.tests.test_tooling` still PASS.
- [ ] **Step 5: Commit** `feat(gallery): index repo workflows and checker`

## Phase D — App

### Task 13: GitHub connection on Settings PUBLISHING

**Files:**
- Create: `src/Nfty.App/Services/GitHubAccount.cs` (`IGitHubAccount`)
- Modify: `src/Nfty.App/ViewModels/SettingsDialogViewModel.cs`, `src/Nfty.App/Views/SettingsDialogView.axaml`
- Test: `tests/Nfty.App.Tests/GitHubAccountTests.cs`, `tests/Nfty.App.Tests/SettingsGitHubTests.cs`

**Interfaces:**
- Produces: `interface IGitHubAccount { string? Login { get; } bool IsConnected { get; } string? UnavailableReason { get; }
  Task<DeviceCode> BeginAsync(CancellationToken); Task ConnectAsync(DeviceCode, CancellationToken);
  void Disconnect(); GitHubShareClient? Client(); event EventHandler? Changed; }` — token in
  `ISecretStore` target `nfty/github`, login cached in `settings.json` key `githubLogin` via `SettingsJson.Set`.
- Settings card: a GITHUB row under the pinning rows — `Connected as @login` + **Disconnect**, or
  **Connect GitHub…** which shows the user code in large mono, **Copy code**, **Open GitHub**
  (`verification_uri`), and waits; the row's height is reserved (`Panel.settingspages` stays 221 —
  if it no longer fits, re-measure and update the constant and CLAUDE.md line). Store unavailable ⇒
  button disabled with `UnavailableReason`.

- [ ] **Step 1: Write failing tests:** `Connect_stores_the_token_in_the_secret_store_only`
  (temp `.nfty` contains no token bytes), `Disconnect_deletes_the_secret`,
  `Settings_shows_the_login_not_the_token`, `An_unavailable_store_disables_connect_with_its_reason`,
  `Settings_page_still_fits_its_reserved_height`.
- [ ] **Step 2–4:** FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat(app): connect GitHub for sharing`

### Task 14: `ICommunityCache`

**Files:**
- Create: `src/Nfty.App/Services/CommunityCache.cs`
- Test: `tests/Nfty.App.Tests/CommunityCacheTests.cs`

**Interfaces:**
- Produces: `interface ICommunityCache { Task<CommunityListing> RefreshAsync(CancellationToken);
  CommunityListing? Cached { get; } Task<string?> PreviewAsync(CommunityFile, CancellationToken);
  string SetsDir { get; } }`; `record CommunityListing(CommunityIndex Index, DateTimeOffset FetchedAt,
  string? Notice)` (Notice = `Offline(time)` or `IndexTooNew`). Root `<state>/.nfty/community/`
  (`index.json`, `index.etag`, `previews/<sha>.png`, `sets/`), injected root for tests. Previews LRU
  by last-access, total ≤ 200 MB (209,715,200), trimmed after each write.
- [ ] **Step 1: Write failing tests:** `A_304_keeps_the_cached_index_and_sends_the_etag`,
  `Offline_returns_the_cache_with_the_offline_notice`, `A_newer_index_keeps_the_cached_list`
  (notice == `IndexTooNew`, cached entries returned), `Previews_are_verified_by_hash`,
  `Previews_trim_oldest_past_200_mb`, `Never_calls_api_github_com` (handler fails any request to that host).
- [ ] **Step 2–4:** FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat(app): community cache`

### Task 14a: Seen tracking (New / Updated)

**Files:**
- Create: `src/Nfty.App/Services/CommunitySeen.cs`
- Test: `tests/Nfty.App.Tests/CommunitySeenTests.cs`

**Interfaces:**
- Produces: `enum SeenMark { None, New, Updated }`; `interface ICommunitySeen { SeenMark MarkOf(CommunityEntry);
  (int New, int Updated) Count(CommunityIndex); void See(CommunityEntry); void SeeAll(CommunityIndex); }`
  over `<state>/.nfty/community/seen.json` = `{ "firstVisit": "...", "seen": { "<id>": "<updated>" } }`
  (injected root). First construction with no file records `firstVisit = now` and marks nothing.
- Rules (spec 8.1): New = id unseen and `Created > firstVisit`; Updated = id seen and
  `Updated > seen[id]`; otherwise None. An unseen entry created before `firstVisit` is None.
- [ ] **Step 1: Write failing tests:** `The_first_visit_marks_nothing`, `A_share_created_after_the_first_visit_is_new`,
  `A_later_updated_is_updated_not_new`, `Selecting_marks_seen`, `See_all_clears_every_mark`,
  `Counts_match_marks`, `A_corrupt_seen_file_starts_over_without_throwing`.
- [ ] **Step 2–4:** FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat(app): new and updated marks`

### Task 15: Community page

**Files:**
- Create: `src/Nfty.App/ViewModels/CommunityViewModel.cs`, `CommunityTile.cs`
- Create: `src/Nfty.App/Views/CommunityView.axaml(.cs)`
- Modify: `src/Nfty.App/Themes/Styles.axaml` (tile/badge classes, reusing `Border.tload`, `Border.tmissing`, `seg`)
- Test: `tests/Nfty.App.Tests/CommunityPageTests.cs`, `tests/Nfty.App.Tests/CommunityLayoutTests.cs`

**Interfaces:**
- Consumes: `ICommunityCache`, `ICommunitySeen`, `IGitHubAccount`, `INavigationService`, `IDialogService`, `ThumbnailGate`.
- Produces: `CommunityViewModel(ICommunityCache, ICommunitySeen, IGitHubAccount, INavigationService, IDialogService,
  IStatusService, Func<string, Task> openSet)` with `Query`, `Tag`, `Sort` (`RecentlyUpdated|Name|Assets`,
  default `RecentlyUpdated` by `Updated` desc then `Id` ordinal), `NewCount`, `UpdatedCount`,
  `MarkAllSeenCommand`, tile `Mark` (`SeenMark`, one reserved badge slot, accent ink), rail
  `DatesText` = `"Shared {d MMM yyyy} · updated {d MMM yyyy}"` (only `Shared …` when equal, invariant
  culture), selection calls `ICommunitySeen.See`,
  `Tiles` (current page), `PageSize` (set by view, measured), `Selected`, `StatusLine`,
  `DownloadCommand`, `ReportCommand`, `UnshareCommand`, `RefreshCommand`, `ClearSearchCommand`,
  `EmptyKind` (`None|NoMatches|EmptyGallery|NeverFetchedOffline`), `PendingRefresh` (Task, for tests).
  Tile: preview, name, `@owner`, `"{assets:N0} · {w} x {h} · {SizeText}"`, `HasModels`, `HasBook`, `IsPending`, `IsMine`.
- Layout per spec 8: header (title, search, tag `seg`, sort) + reserved status line; paged grid
  (page size measured from the rendered row, like `CookBookDetailView.OnLayoutUpdated`, reading only
  inputs it cannot change); 288 px rail (preview with cell stepping on wheel, wrapped description,
  facts, Download accent, Report, Unshare when mine; "Pick a collection to see it here." when none).
  Badges and status line reserved, ink toggled. Report opens
  `https://github.com/{GalleryRepo}/issues/new?template=report.yml&entry={owner.id}/{slug}` (no
  personal data in the URL).

- [ ] **Step 1: Write failing tests:** `Search_filters_by_name_owner_and_collection`,
  `Tag_filter_and_sort_compose`, `Empty_states_say_which_emptiness` (three cases),
  `Browsing_works_with_a_revoked_token`, `Offline_disables_download_with_the_offline_sentence`,
  `Badges_reserve_space_when_absent` (pixel-diff two tiles), `The_page_fits_the_smallest_window`
  (add the page to `MinimumWindowFitTests`), `Page_size_is_measured_and_stable_on_resize`,
  `Report_url_carries_only_the_entry_id`, `Recently_updated_is_the_default_sort`,
  `Status_line_counts_new_and_updated`, `Selecting_a_new_tile_clears_its_badge`,
  `Badge_slot_is_reserved_when_unmarked` (pixel-diff).
- [ ] **Step 2–4:** FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat(app): community page`

### Task 16: Download from the page

**Files:**
- Modify: `src/Nfty.App/ViewModels/CommunityViewModel.cs`, `src/Nfty.App/ViewModels/ShellViewModel.cs` (navigation to Set browser)
- Test: `tests/Nfty.App.Tests/CommunityDownloadFlowTests.cs`

**Interfaces:**
- Consumes: `CommunityDownload.FetchAsync`, `SetReader.ReadAsync` via the existing open-Set path (`LandingViewModel.OpenSet`'s `BusyViewModel` route — extract it into `Func<string, Task> openSet` passed to the page).
- Rail Download shows inline progress (reserved row) then opens the Set browser; **Save to…** in the Set browser already exists for `.set` files — reuse, no new button. Failures show the `ShareRules` sentence in the rail's reserved reason line, never a dialog.
- [ ] **Step 1: Write failing tests:** `Download_opens_the_set_browser`, `A_mismatch_shows_the_sentence_in_the_rail`,
  `Download_can_be_stopped_and_leaves_nothing`, `A_second_download_of_the_same_set_reuses_the_cache`.
- [ ] **Step 2–4:** FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat(app): download community sets`

### Task 17: Share card

**Files:**
- Create: `src/Nfty.App/ViewModels/ShareDialogViewModel.cs`, `src/Nfty.App/Views/ShareDialogView.axaml(.cs)`
- Modify: `src/Nfty.App/ViewModels/SetBrowserViewModel.cs` (`ShareCommand`, `CanShare` = `CanExport`; reason when sealed: `ShareRules.Sealed`), Set browser header markup (Share… beside Export)
- Test: `tests/Nfty.App.Tests/ShareDialogTests.cs`, `tests/Nfty.App.Tests/ShareDialogLayoutTests.cs`

**Interfaces:**
- Consumes: `CommunityShare.Options`, `SetExporter.Plan`, `ShareCheck.CheckAsync` (facts for limit rows, off the UI thread), `CookBookLocator`, `PublicRepoScan.FetchAsync/Scan/RemoveAsync`, `IGitHubAccount`.
- Produces: `ShareDialogViewModel` (`IUnsavedWork` while running) with `IncludeCookBook`
  (`IsEnabled` only when `CookBookLocator` finds the hash match), `AlreadyShared` (the user's
  matching share, shown in the lower block with its dates), `ReplaceSharedVersion` (checkbox, off
  by default; Share disabled with `ShareRules.ReplaceNotChosen` until ticked), `DuplicateReason`
  (from `CommunityDuplicates.Problem` against the cached index, computed when facts land),
  `Models` (dimmed with reason on a
  Set without models), `Plan` (rendered `ExportPlan.Parts()`/`SizeText()`), `LimitRows` (4 fixed
  rows: canvas, assets, size, shared — each `Text`, `IsOver`), `Name`, `Description`, `Tags`,
  `Slug` (derived line), `GitHubChip`, `LowerBlock` (`None|Extras|Duplicate|AlreadyShared|Replace` — one reserved block; precedence
  in that order after `None`),
  `Extras`, `RemoveExtrasCommand` (opens `ConfirmDialogViewModel` naming every item and
  "This can't be undone from nfty."), `ReplaceChoices`, `ReplaceSlug`, `ShareCommand`
  (`CanShare` with `ShareReason`), states `Editing|Running|Done|Failed`.
- Layout: `.modal.split` 880 px, `HorizontalAlignment/VerticalAlignment="Center"`, fits the real
  modal area (`ModalFitTests` adds it; NOT in `Only_the_named_modals_may_scroll`'s list — only the
  lower block's row list may scroll, rows only, with Remove these… outside the scroller).
- [ ] **Step 1: Write failing tests:** `Limit_rows_turn_warning_and_name_the_fix`,
  `Plan_matches_CommunityShare_options` (plan parts == `SetExporter.Plan(path, CommunityShare.Options(req))`),
  `Book_checkbox_disabled_without_a_matching_book`, `Extras_block_disables_share`,
  `Remove_these_deletes_only_after_confirmation` (declined ⇒ fake records no DELETE),
  `At_three_shares_share_waits_for_a_choice`, `An_already_shared_collection_needs_the_replace_tick`,
  `A_set_listed_by_someone_else_shows_who_and_disables_share`, `An_unchanged_set_shows_nothing_changed`,
  `Lower_block_height_is_the_same_in_every_state`
  (pixel-diff), `The_card_fits_the_modal_area_unscrolled`, `Signed_out_card_shows_connect_not_hidden`,
  `Share_button_press_runs_the_command` (gesture test: real button).
- [ ] **Step 2–4:** FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat(app): share card`

### Task 18: Share run, pending state, finish, unshare

**Files:**
- Modify: `ShareDialogViewModel.cs`, `CommunityViewModel.cs`, `src/Nfty.App/Services/CommunityCache.cs` (pending record `pending.json`: `PendingShare` list)
- Test: `tests/Nfty.App.Tests/ShareRunTests.cs`

**Interfaces:**
- Consumes: `CommunityShare.RunAsync/FinishAsync/UnshareAsync`, `GitHubShareClient.GetPullAsync/GetPullCommentsAsync`.
- Card Running state shows `ShareStage` phases with one progress bar; Done shows
  `Submitted. The gallery checks it now; it usually lists within a few minutes.` + PR link (copy +
  open). The page shows `IsPending` tiles from `pending.json`; on each refresh, for each pending:
  merged ⇒ `FinishAsync` then drop; closed unmerged ⇒ show the PR's comment lines in the rail's
  reason line, keep until the user dismisses. Close guard: running share = busy (existing modal busy hook).
- [ ] **Step 1: Write failing tests:** `Done_state_links_the_pull`, `Pending_tile_appears_before_merge`,
  `Merged_pending_runs_finish_once`, `Closed_pending_shows_the_actions_reasons`,
  `Window_close_is_refused_while_sharing`, `Unshare_asks_then_opens_the_pull`.
- [ ] **Step 2–4:** FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat(app): share run, pending and unshare`

### Task 19: Entry points and sweeps

**Files:**
- Modify: `src/Nfty.App/ViewModels/LandingViewModel.cs` + `Views/LandingView.axaml` (OPEN column: **Browse the community gallery**, with a reserved chip `"{n} new · {m} updated"` from `ICommunitySeen.Count(cache.Cached)` — cached index only, no network; ink off at zero; test `Landing_chip_uses_the_cached_index_only`)
- Modify: `src/Nfty.App/ViewModels/HelpCatalog.cs` (BASICS: Community entry; no new chords)
- Modify: `tests/Nfty.App.Tests/DarkModeContrastTests.cs` (`Screens()` adds the page offline and the card with a warning limit row + extras block), `VisualCapture` (page + card, both themes)
- Modify: `assets/icons/` + run `python tools/icons/build.py` only if a new glyph is needed (`community`); commit SVG + `Icons.axaml` together
- Test: existing sweeps — `MinimumWindowFitTests`, `Landing_needs_no_scrollbar_at_the_smallest_window`, `PixelPerfectRenderingTests`, `ThemeResourceTests`, `WiringCoverageTests`, `DeadCodeAuditTests`, `TextBaselineTests`
- [ ] **Step 1:** add the Landing button; run `dotnet test tests/Nfty.App.Tests --filter "FullyQualifiedName~Landing|FullyQualifiedName~MinimumWindowFit"` — if Landing now scrolls, rebalance the two action stacks (CREATE/OPEN) rather than shrink margins, and re-run until PASS.
- [ ] **Step 2:** add contrast screens + captures; run `--filter "FullyQualifiedName~DarkModeContrast|FullyQualifiedName~ThemeResource|FullyQualifiedName~PixelPerfect|FullyQualifiedName~WiringCoverage|FullyQualifiedName~DeadCodeAudit|FullyQualifiedName~TextBaseline"` — PASS.
- [ ] **Step 3:** `NFTY_CAPTURE=1 NFTY_CAPTURE_DIR=<dir> dotnet test tests/Nfty.App.Tests --filter FullyQualifiedName~VisualCapture` and LOOK at the page + card frames at 1280x720 in both themes; fix what the frames show.
- [ ] **Step 4: Commit** `feat(app): community entry points and sweeps`

### Task 20: Endpoints, docs, release notes

**Files:**
- Modify: `src/Nfty.Core/Publish/Community/CommunityEndpoints.cs` (`Default` with the author's `GalleryRepo` and OAuth `ClientId` — author supplies both; spec open question 1)
- Create: `docs/manual/using-the-app/community.md`; Modify: `mkdocs.yml` nav (Using the app), `docs/manual/reference/cli.md` (`share check`)
- Modify: `CLAUDE.md` (one bullet: COMMUNITY GALLERY — the rules in `ShareRules`, one check for client/CLI/Action, `nfty-public` scan, naming prefix), `BACKLOG.md` (image screening, GitHub App scope, from spec 11)
- Regenerate: `python tools/api-docs/build.py`
- Test: `tests/Nfty.Core.Tests/Community/CommunityEndpointsTests.cs`
- [ ] **Step 1: Write failing test** `Default_endpoints_are_filled` (non-empty `GalleryRepo` matching `^[\w.-]+/nfty-gallery$`, non-empty `ClientId`, `IndexUrl` https).
- [ ] **Step 2:** fill the values with the author; run — PASS.
- [ ] **Step 3:** write the manual page (intro sentence, prerequisites, numbered steps for browse, download, share, replace, unshare; result; the limits table; the fair-use reason) and run `python -m mkdocs build --strict` — PASS; `python tools/api-docs/build.py --check` — PASS after regenerating.
- [ ] **Step 4:** full suite `dotnet test nfty.sln` and `python -m unittest discover tools/tests` — PASS.
- [ ] **Step 5: Commit** `docs(community): manual, reference, CLAUDE.md`
