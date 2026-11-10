# Community Gallery: sharing cooked Sets — design

Date: 2026-11-09. Status: design approved in conversation, awaiting the author's review of this
written spec. No code yet.

## 1. What this is

A **Gallery** page in the desktop app where anyone can browse and download cooked Sets other
people shared, and where an author with a connected GitHub account can share their own. Desktop
only — there is no web app and none is planned. Only **publishable** Sets travel: a sealed `.tin`
is never shared, the same rule `SetExporter` already enforces for every export.

Decided with the author (2026-11-09):

| Decision | Ruling |
|---|---|
| Hosting | **GitHub as the registry.** Files live in the sharer's own repo; a separate index repo, **`nfty-gallery`**, holds the listing. No server of ours. |
| Who signs in | **Sharing** needs a connected GitHub account. **Browsing and downloading are anonymous.** |
| What is shared | One packed **`.set`**, built from the **AssetPack** preset; the sharer may tick **Include source CookBook** so others can remix. |
| Limits | Canvas **at most 512 x 512**, **at most 1,000 assets** and **at most 200 MB packed** per shared Set, **at most 3 shared collections per GitHub account** — so one account holds at most ~600 MB. The gallery is a **sample** of each author's work, not an archive. |
| Repo shape | **One repo per user, named exactly `nfty-public`**, holding 1–3 shared collections (one Release each) and a **`nfty-public.json`** listing them. At the limit, the signed-in user picks one to remove to make room. |
| Moderation | **Auto-merge, but only behind a strict safety Action.** Report + delist after the fact. |
| Duplicates | A Set already listed — same **fingerprint** (its DNAs) or same file hash, by anyone — is **refused**. A sharer's own changed Set may **replace** their listed version, by explicit choice. |
| New / updated | Every entry carries `created` and `updated`; the app remembers what the user has seen and marks **New** and **Updated** collections (section 8.1). |

## 2. Why GitHub, and why not plain IPFS

### 2.1 Pure IPFS (metadata only, files fetched from IPFS) does not work

The author's suspicion is right. An IPFS node garbage-collects every block it has not pinned; other
nodes and public gateways only hold transient cached copies, and provider records in the DHT
expire within about a day unless the node that holds the data keeps republishing them. A gallery
whose files live only on the sharers' own pins loses a Set the day that sharer's Kubo node goes
offline or their pinning plan lapses (Pinata free: 1 GB, 500 files). The free IPFS services that
promised permanence are the cautionary tale: NFT.Storage Classic stopped taking uploads on
2024-06-30. IPFS only works if **the registry itself pins everything** — at which point it is
ordinary paid storage reached through a slower protocol. The existing `Publish/Ipfs/` code stays
what it is: the marketplace drop, not the gallery.

### 2.2 Options compared

| | **GitHub registry** (chosen) | Cloudflare R2 + Worker + D1 | Supabase | Backblaze B2 (+ a catalog somewhere) | IPFS + pinning |
|---|---|---|---|---|---|
| Cost, small (≤10 GB) | $0 | $0 (10 GB R2, 100k Worker req/day, 5 GB D1) | $0 (1 GB) | ~$0.07/mo | $0 to 1 GB, then $20/mo (Pinata) |
| Cost, medium (~200 GB, heavy download) | $0 | ~$3/mo storage, egress free | Pro $25/mo + egress | ~$1.40/mo, egress free to 3x stored | $20/mo |
| Uploader identity | GitHub account (device flow) | Needs an OAuth provider anyway (GitHub) | Supabase Auth | Needs a server to mint upload URLs | Sharer's own key, or ours behind a server |
| Per-file limit | 2 GiB per Release asset; no documented total or bandwidth cap | ~5 GiB single PUT | **50 MB** on free | 5 GB single, multipart beyond | provider-specific |
| Abuse / takedown | Index-repo maintainer + GitHub's own abuse handling and DMCA process | Ours to build entirely | Ours to build | Ours to build | Ours, and unpinning does not erase other copies |
| Longevity / vendor risk | GitHub Releases have been stable for over a decade; fair-use clause is the risk (2.3) | Stable, but D1 free-tier enforcement changed in 2026 | **Free projects pause after 7 idle days** | Stable; raised prices 2026-05 | Free tiers have closed (NFT.Storage) |
| Server code we run | **None** (Actions only) | Worker: auth, rate limit, presign, takedown | Edge functions + RLS | A Worker or similar anyway | A server, to keep keys off clients |
| What the client embeds | Public OAuth **client id** only (not a secret) | Public URL; secrets stay in the Worker | Anon key (public by design) + RLS | Nothing; server mints URLs | Nothing, or a leaked key |

Supabase's 50 MB file cap and idle pausing rule it out. R2 is the best **fallback**: the index
stores plain URLs (section 5), so moving the files to R2 later changes the backend and no client
format.

### 2.3 The GitHub risk, stated plainly

The constraint is GitHub's Acceptable Use Policy, not size: GitHub reserves the right to throttle
or suspend an account whose bandwidth is "significantly excessive" next to similar users, or which
uses GitHub mainly as a media CDN. The design keeps clear of that three ways: hard content limits
(canvas, count, collections), files in each **sharer's own** repo (a suspension removes one
person's Sets, not the gallery), and a client that never calls `api.github.com` to browse (the
unauthenticated limit is 60 requests an hour per IP; `raw`/Pages and Release downloads are not
counted against it).

## 3. Repositories

### 3.1 `nfty-gallery` (the index; owned by the author)

```
nfty-gallery/
  entries/<github-user-id>/<slug>.json   one per shared collection; the ONLY files a PR may touch
  blocked.json                           user ids and Set sha256s refused at check time
  tags.json                              the fixed tag list the client offers
  .github/workflows/check.yml            on pull_request: validate, no secrets (section 4)
  .github/workflows/merge.yml            on workflow_run of check: merge if green
  .github/workflows/publish.yml          on push to main: build index.json, deploy to Pages
  .github/workflows/health.yml           nightly: HEAD every Set, delist the ones gone 3 nights
  .github/ISSUE_TEMPLATE/report.yml      the in-app Report button opens this, prefilled
```

The id directory is the GitHub **numeric user id**, not the login: logins can be renamed and
re-registered, ids cannot. `index.json` is generated and published to Pages; it is never committed
by hand, the same arrangement `docs/manual/reference/api/` has with its generator.

### 3.2 `<login>/nfty-public` (one per user; owned by the sharer)

```
nfty-public/
  nfty-public.json    { schemaVersion, shares: [ 1..3 objects, each the index entry's shared fields ] }
  README.md           written by nfty: every share's name, description, preview, "open in nfty"
  Releases (one live per share):
    <slug>-v<N>  ->  <slug>.set, preview.png
```

**Why one repo per user** (author's ruling 2026-11-09, replacing one repo per collection): the
name is fixed, so there is nothing to derive or collide on per share; the limit is a property of
one place the Action can count; a sharer has one repo to understand, not up to five; and the total
an account can put on GitHub is bounded by construction — 3 shares x 200 MB.

**A repo named `nfty-public` that nfty did not make** (no `nfty-public.json` at its root) is
refused, never adopted: `You already have a repository called nfty-public that nfty didn't make.
Rename or remove it on GitHub, then share again.` A private one is refused the same way.

**The bound holds only if old versions go.** An update makes Release `<slug>-v<N+1>` and opens the
PR; once the index lists the new one, the client deletes the superseded Release and its tag (the
app's next refresh notices, or the next share run does it). Until then the repo holds one Release
more than its shares — which is why the Action allows `shares + 1` (4.2 #7).

### 3.3 The scan before every share (author's ruling 2026-11-09)

Before **every** share, update or swap, the client scans the user's `nfty-public` and allows only:

| Where | Allowed |
|---|---|
| Default branch tree | `README.md` and `nfty-public.json` — nothing else. (`nfty-public.json` is the repo's manifest, the "matching nfty metadata file"; it is not a fourth collection.) |
| Branches | The default branch only. |
| Releases | One per share listed in `nfty-public.json`, at most 3; plus one superseded Release **only while** the PR that replaces it is still open. |
| Each Release's assets | Exactly `<slug>.set` and `preview.png`. |
| Tags | Only the tags of the allowed Releases. |
| Git history size | The repo's own size (GitHub's `size`, which excludes Release files) at most 5 MB — a large file committed and later deleted still lives in history. |

Anything else is listed on the share card, one row per item with its size, under `Your
nfty-public holds things the gallery doesn't allow. Remove them to share.` **Share stays
disabled** until the repo is clean. Beside the list, **Remove these…** opens a confirmation that
names every item again and says plainly that removing them from GitHub can't be undone from nfty;
only on that confirmation does the client delete them (files via the contents API, Releases and
their tags, extra branches). Nothing is ever deleted without that click. A Release the user keeps
by declining stays, and so does the refusal.

**History over 5 MB is the one thing nfty can't clean.** Rewriting history is not something the
client does to a user's repo; the card says `Your nfty-public's history holds about 48 MB of
deleted files. Delete the repository on GitHub and share again; nfty will make a fresh one.` with a
link to the repo's settings page. Declined to automate on purpose: deleting a repo also deletes
every Release in it.

The same rules are re-checked by the Action (4.2 #7a), so a hand-made PR cannot skip the scan.

**Removing one to make room.** At 3 shares, the share card lists the three (name, preview cell,
size) and asks which goes; nothing is removed until Share is pressed. That share run then deletes
the chosen entry AND adds the new one in **one PR**, and deletes the chosen Release after the
merge — so the gallery never shows the user with a gap they did not choose, and a run that fails
before the PR removes nothing.

`<slug>` is the collection name lowered, non-alphanumerics to `-`, collapsed, trimmed, at most 40
characters, the same rule as the identifiers the New CookBook wizard derives; unique within the
user's repo. An update is a new Release plus a PR that points the entry at it.

`preview.png` is a sprite sheet of the first assets in Set order — up to 64, fewer when the canvas
is large, so the sheet stays at most 2048 px a side (32 px art: 64 cells; 512 px art: 16) — built by
`Publish/SpriteSheet` and carrying its `nfty-grid` chunk, so the gallery tile and the detail rail
can show individual cells without downloading the Set.

## 4. The safety Action (`check.yml`)

Runs on `pull_request` with **no secrets and a read-only token**; it reads the PR's changed files
as **data** and never checks out or runs anything from the PR. A separate `merge.yml`, triggered
by `workflow_run` on success, holds the write token and merges. Every refusal is a PR comment in
product words (from `ShareRules`, section 6) and closes the PR.

### 4.1 Who is sharing

1. The PR touches only files under `entries/<author-id>/`, where `<author-id>` is the PR author's
   numeric id: at most **one added or changed** entry, plus at most **one deleted** entry (the
   swap in 3.2), or a lone deletion (unshare).
2. The account is at least **30 days** old.
3. After this PR the author has at most **3** entries.
4. At most **one new** entry per author per 24 hours (updates to an existing entry not counted).
5. Neither the author id nor the Set sha256 is in `blocked.json`.

### 4.2 The repo

6. `entry.repo` is exactly `<author-login>/nfty-public`, owned by the PR author, **public**, and the
   entry's `slug` matches its file name.
7. The repo's `nfty-public.json` on its default branch lists this entry with every shared field
   equal, lists every other entry the author keeps, and lists nothing the PR deletes; the repo
   holds at most **shares + 1** Releases (one superseded Release may still await deletion).
7a. The repo passes the scan in 3.3 (tree, branches, Releases, assets, tags, history size). The
   client ran it before sharing; the Action does not trust that.

### 4.3 The file

8. The Release asset exists, is **at most 200 MB**, and its sha256 equals `entry.set.sha256`. The
   canvas and asset limits do not bound size on their own (1,000 noisy 512 px PNGs plus models
   pass 1 GB), so this cap is what makes "~600 MB an account" true.
9. ZIP safety before anything else reads it: at most 6,000 entries (1,000 assets x four files,
   plus room); total uncompressed at most 1 GiB; no single entry's compression ratio above 100:1; no absolute path, drive letter, `..`
   segment or backslash; no symlink entries.
10. Only the `SetLayout` include list (`set.json`, `images/`, `metadata/`, `nfty/`, `models/`) plus
    **at most one** root `.cbk`, whose SHA-256 must equal `set.json`'s `cookbookSha256`. A book
    that is not the Set's own is refused, not warned: in a public gallery it is unexplained
    content.

### 4.4 The Set

11. `nfty share check <file.set>`, the CLI built from a **pinned nfty release tag**, runs
    `ShareCheck` (section 6) — the same code the client ran before uploading:
    - opens it through `SetReader` (schema gate included);
    - refuses anything `Archives.KindOf` calls `Sealed`, and anything carrying a seal manifest,
      whatever its extension;
    - canvas at most 512 x 512; at most 1,000 assets; `set.json`'s count and canvas equal the
      entry's;
    - every image decodes through `ImageFormats` and is canvas-sized;
    - `metadata/` and `nfty/` files parse; no `files` block (a renamed marketplace export is not a
      Set to open).

### 4.4a Duplicates (author's ruling 2026-11-09)

A Set's **fingerprint** is SHA-256 over its assets' DNA strings in set-number order, joined by
`\n` (UTF-8). It is computed by `ShareCheck` from the opened Set, so it ignores everything that
changes when the same collection is packed again (ZIP timestamps, file order, an added `.cbk`,
leaving models out) and changes the moment the collection itself changes (recooked, extended,
another seed). The file's sha256 is checked too, as the cheap exact case.

11b. Against every entry in `index.json` **and** every open gallery PR's entry:
   - same fingerprint or same `set.sha256` under **another account** ⇒ refused,
     `This Set is already in the gallery, shared by @{login}.`;
   - same fingerprint under **another slug of the same account** ⇒ refused,
     `You already share this Set as "{name}".`;
   - same slug, same fingerprint (an "update" that changes nothing) ⇒ refused,
     `Nothing changed since you shared "{name}" on {date}.`

A **changed** Set (new fingerprint) under the sharer's own existing slug is an update; it is only
ever submitted when the sharer chose **Replace the shared version** on the card (7.1). A collection
that overlaps another without being equal (an extended copy of someone else's Set) is not detected:
see open question 8.

### 4.5 Words and preview

12. Name 1–64 characters, description at most 500, no URLs in either (`://`, `www.`); at most 5
    tags, each from `tags.json`.
13. `preview.png` at most 2 MB, at most 2048 px a side, decodes, and its `nfty-grid` cell equals
    the Set canvas.

### 4.6 After merge

`publish.yml` rebuilds `index.json` (sorted ordinally by id, so a rebuild of the same entries is
byte-identical) and deploys Pages. `health.yml` checks each listed Set and preview nightly; one
missing three nights in a row is marked `"status": "gone"` and dropped from `index.json` (the entry
file stays, so the sharer can restore it with a fresh Release). **Takedown:** the maintainer
reverts the entry and adds the id or hash to `blocked.json`; the next publish removes it from
every client's next refresh.

Not automated, said out loud: **image content is not screened.** There is no free, private
image-moderation service worth wiring into a public Action; the 30-day account age, the per-day
rate, the report form and `blocked.json` are the defense. See open question 4.

## 5. Schemas

All JSON through `Json.Options` (camelCase). Both files carry `schemaVersion` and pass through the
same gate rule as manifests: `Oldest..Current`, refuse newer, every later field optional.

### 5.1 `entries/<id>/<slug>.json` (and one item of `nfty-public.json`'s `shares`)

```json
{
  "schemaVersion": 1,
  "slug": "vapor-chests",
  "owner": { "id": 1234567, "login": "someone" },
  "repo": "someone/nfty-public",
  "release": "vapor-chests-v1",
  "name": "Vapor Chests",
  "collection": "Vapor Chests",
  "description": "Chests and strongboxes, 29 drawings.",
  "tags": ["props", "pixel-art"],
  "canvas": { "width": 32, "height": 32 },
  "assets": 600,
  "models": true,
  "includesCookBook": true,
  "cookbookSha256": "…",
  "set":     { "url": "https://github.com/someone/nfty-public/releases/download/vapor-chests-v1/vapor-chests.set", "size": 4194304, "sha256": "…" },
  "preview": { "url": "https://github.com/someone/nfty-public/releases/download/vapor-chests-v1/preview.png", "size": 81920, "sha256": "…" },
  "fingerprint": "…",
  "nftyVersion": "2.5.0",
  "created": "2026-11-09T14:02:00Z",
  "updated": "2026-11-09T14:02:00Z"
}
```

`created` is set once and carried unchanged through every update; `updated` is the time of the
latest share (equal to `created` on the first). The Action refuses an entry whose `created`
differs from the entry it replaces, or whose `updated` is not later than it — so the dates the
gallery shows cannot be back- or forward-dated by a hand-made PR. Both are written by the client in
UTC; the Action also refuses an `updated` more than 1 hour ahead of the PR's own creation time.

`url` is a plain URL, never derived from `repo` + `release` by the client: that is the seam that
lets files move to another host without a client change.

### 5.2 `index.json` (generated)

```json
{ "schemaVersion": 1, "generated": "2026-11-09T14:10:00Z", "entries": [ /* 5.1 objects + "updated" */ ] }
```

## 6. Code boundaries

**Code names use `Community`, not `Gallery`**: `GalleryBuilder`, `GalleryItem` and `ArchiveGalleryViewModel` are already the viewer's. The page title reads **Community**.

**No new packages.** `HttpClient` and `System.Text.Json` cover everything; the existing
`Publish/Ipfs/` code already speaks HTTP from Core the same way.

### 6.1 `Nfty.Core/Publish/Community/` (UI-free)

| Type | Job |
|---|---|
| `CommunityIndex`, `CommunityEntry` | Records for 5.1/5.2; `Read(stream)` with the schema gate. |
| `ShareRules` | The limits (512, 1,000, 3, 200 MB, text lengths) and **every sentence** a refusal shows — CLI, Action comment, share card and download check all print these verbatim. One place, like `MarketplaceRules`. |
| `ShareCheck` | `Check(setPath) -> IReadOnlyList<string>` problems, report-never-throw like `Validator`. Sections 4.3 #9–10 and 4.4. Used by the client before upload, after download, and by the Action through the CLI. |
| `ShareSlug` | Name -> slug (3.2), unique within the user's `nfty-public`. |
| `PublicRepoManifest` | Reads/writes `nfty-public.json`; refuses a repo of that name nfty did not make (3.2). |
| `PublicRepoScan` | The 3.3 scan: `Scan(repo state) -> IReadOnlyList<RepoExtra>` (what, where, size), report-never-throw; `RemoveAsync(extras)` deletes exactly the listed items. Pure over a fetched snapshot, so the rules are tested without GitHub. |
| `GitHubDeviceFlow` | Device code request + poll; the client id is a constructor argument. |
| `GitHubShareClient` | Exactly the calls sharing needs: viewer, create/get repo, put file (contents API), create Release, upload asset, delete Release, fork, create branch + commit entry, open PR, read PR state. Takes an `HttpMessageHandler` so tests fake GitHub. |
| `CommunityShare` | The share run (section 7), progress + cancellation, rollback of what it created. Mirrors `MarketplaceUpload`. |
| `CommunityDownload` | Fetch, hash and size check, `ShareCheck`, hand back the path. |

### 6.2 `Nfty.Cli`

`nfty share check <file.set>` — prints problems one per line, exit 1 if any. This is what the
Action runs; giving the client and the Action the same code is the point of it.

### 6.3 `Nfty.App`

`CommunityViewModel` + `CommunityView` (the page), `ShareDialogViewModel` + view (the card),
`ICommunityCache` (6.4), the GitHub token in `ISecretStore` under target `nfty/github` (shown as
connected login, never the token), sign-in/out on the Settings **PUBLISHING** tab beside Pinata.
The OAuth client id ships in the binary; it is public by design and the device flow needs no
client secret.

### 6.4 Cache and offline

`.nfty/community/index.json` kept with its `ETag` (conditional GET; a 304 costs nothing).
`.nfty/community/previews/<sha256>.png`, least-recently-used, capped at 200 MB. Downloads go to a
temp file, then into `.nfty/community/sets/<sha256>.set` once checked; the Set browser opens from
there and **Save to…** copies it out. Offline: the cached list stays on screen, the header's
reserved status line says `GitHub can't be reached. Showing the list from 14:02.`, Download and
Share are disabled with that sentence as their reason. No cache yet and offline: the empty state
says so and offers **Try again**.

## 7. Share flow

Entry point: **Share…** in the Set browser header, beside Export (both are "send this Set
somewhere"). Disabled with its reason on a sealed Set (the Set browser already knows it is one),
on a Set with no `set.json`, and while signed out it opens the card on its sign-in state rather
than hiding.

### 7.1 The card — `.modal.split`, 880 px, fits ~1067 x 526 unscrolled

- **Left column — what leaves the machine.** Two checkboxes (**Include source CookBook**,
  **3D models**, the latter dimmed with its reason on a Set without models); below them the
  manifest rendered from **`SetExporter.Plan`** for `ExportOptions.For(AssetPack)` with
  `Shape = Archive`, `Sealed = false`, plus those two flags. The book checkbox resolves its path
  through `CookBookLocator` and, unlike Export, is disabled when no book hashes to the Set's
  `cookbookSha256` (the Action would refuse a mismatched one, 4.3 #10).
- **Limit rows**, reserved, one line each, always present: `Canvas 32 x 32 of 512 x 512`,
  `600 of 1,000 assets`, `Size 4.0 MB of 200 MB`, `2 of 3 shared`. A row past its limit turns
  `WarningBrush` and names the fix (`Leave out 3D models (−160 MB)`); ink toggles, geometry does
  not.
- **At 3 of 3 shared** (or when this Set's slug is already shared — then it is an update and no
  choice is needed), the right column's lower block shows the user's three shares as radio rows
  (preview cell, name, size): `Replace one to share this. Nothing is removed until you press
  Share.` Share stays disabled until one is picked. The block's height is reserved on every card
  so a user at 2 of 3 and one at 3 of 3 see the same card.
- **Already shared.** When this Set's collection is already one of the user's shares, the same
  reserved block shows it — preview cell, name, `Shared 3 Nov 2026 · updated 5 Nov 2026` — with one
  checkbox, **Replace the shared version**, off by default. Share stays disabled until it is ticked
  (an update is a choice, never a side effect of pressing Share). An unchanged Set shows the
  4.4a sentence instead and no checkbox. A Set someone else already listed shows that sentence
  and nothing to choose. These checks run locally against the cached index before anything is
  uploaded; the Action repeats them.
- **The scan's findings (3.3) use that same reserved block** — a repo must be clean before a share
  can be chosen to replace, so the two never show at once. More extras than the block holds
  scroll inside it, rows only, with the **Remove these…** button outside the scroller.
- **Right column — the listing.** Name (prefilled from `collection`), description, tag chips from
  the cached `tags.json`, the GitHub chip (`Connected as someone` / **Connect GitHub…**), the slug
  it will use (derived-identifier line under the name, the wizards' grammar).
- **Footer:** one accent button, **Share**, disabled with its reason while any check fails.

### 7.2 What Share does

0. The scan (3.3) already ran when the card opened and runs again here; anything new found
   stops the run before a byte is uploaded.
1. Export to temp through `SetExporter` with the plan the card showed.
2. `ShareCheck` on the result locally — refusal here costs no network.
3. Render `preview.png`.
4. Create `nfty-public` on first share (or open it, refusing one nfty did not make), create
   Release `<slug>-v<N>`, upload both files, then rewrite `nfty-public.json` + `README.md` with
   the new share in and any share being replaced out.
5. Fork `nfty-gallery` (or reuse the fork), commit the entry (and the replaced share's deletion)
   on a branch, open the PR.
6. After the merge is seen (card still open, or the page's next refresh): delete the superseded
   or replaced Release and its tag.
7. The card's done state: `Submitted. The gallery checks it now; it usually lists within a few
   minutes.` with a link to the PR. The page shows the entry as **pending** from a local record
   until the next index refresh lists it, or the PR is closed — then the card's reasons are read
   off the PR's comment and shown.

A stop or failure takes back what **this run created** — the new Release, and the repo if this
run made it, and `nfty-public.json` restored — the `MarketplaceUpload` rule. A replaced share is
untouched until the PR merges, so a failed swap loses nothing. A running share is busy to the
close guard.

**Unshare** (detail rail, own entries only): a PR deleting the entry, then the Release once
merged, and the share taken out of `nfty-public.json`. The repo itself is never deleted by nfty —
it is the user's, and an empty one costs nothing.

## 8. The Gallery page

Reached from Landing's OPEN column (**Browse the gallery**) — a sixth item there, so the two
action stacks must be re-measured against `Landing_needs_no_scrollbar_at_the_smallest_window`.

Layout at the 1280 x 720 floor (page area ~1067 x 526):

- **Header row:** title left; search box, tag filter (`seg` tray, stated widths), sort
  (Newest / Name / Assets) right; under it the reserved one-line status (`Updated 14:02` /
  offline sentence).
- **Search = three filters in one box (author's ruling 2026-11-09).** `#tag` keeps entries carrying
  that tag; `@login` keeps that sharer's entries; any other word matches the collection name or
  listing name (case-insensitive, invariant). Terms combine with AND (`@someone #props chest`).
  Parsing is Core's `CommunitySearch.Parse` so the rule is one function. The tag tray shows the
  gallery's fixed tag list (`tags.json`); picking a chip writes `#tag` into the box (one source of
  truth: the box). Every `#tag` and `@owner` shown on a tile or the rail is a link that writes the
  same token into the box.
- **Tags are a real feature.** `tags.json` (`{schemaVersion, tags: [...]}`) lives in
  `nfty-gallery`, `publish.yml` publishes it beside `index.json`, `ICommunityCache` fetches it
  token-free with the index (capped, cached, schema-gated), the share card offers its chips (≤ 5),
  and the Action refuses any other tag. Starting list: characters, creatures, props, items,
  scenery, effects, ui, abstract.
- **Grid:** fixed-size tiles — preview cell (nearest-neighbour, `BitmapInterpolationMode="None"`),
  name, `@owner`, `600 · 32 x 32 · 4.0 MB`, badge slots for **3D** and **Book** (reserved, inked
  only when true). The grid **pages** with the page size measured off the real row, the CookBook
  card's rule, and never sizes rows from content.
- **Right rail, 288 px:** large preview with the sheet's cells (wheel steps cells), description
  (wrapped, never trimmed), facts, then **Download** (accent) and **Report**. Own entries add
  **Unshare**. Nothing selected: the rail says how to pick one.
- **Sort** gains **Recently updated** (by `updated`, newest first), the default.

### 8.1 New and updated

`.nfty/community/seen.json` records, per entry id (`<owner-id>/<slug>`), the `updated` the user has
seen, plus the time of the first visit. On each refresh:

- **New** — an id not in `seen.json` whose `created` is after the first visit (the very first
  visit marks nothing: every collection would be "new");
- **Updated** — an id in `seen.json` whose `updated` is later than the one recorded.

Tiles carry one reserved badge slot that inks **New** or **Updated** (accent), never both; the
rail shows `Shared 3 Nov 2026 · updated 9 Nov 2026`. An entry counts as seen when the user selects
it. The header status line reads `Updated 14:02 · 2 new · 1 updated` with **Mark all seen**, and
Landing's **Browse the community gallery** button carries the same count in a reserved chip,
computed from the **cached** index (Landing never touches the network). Nothing is marked while
offline beyond what the cached list already says.

- **Empty states say which emptiness:** no entries match the search (**Clear search**), the
  gallery is empty, never fetched and offline.

House rules that bite here, named so the implementation plan carries them: reserve space and
toggle ink (badges, status line, pending mark); every page fits the smallest window
(`MinimumWindowFitTests` gains the page); `DarkModeContrastTests.Screens()` gains the page in its
riskiest state (offline, a warning-ink limit row on the card); `ModalFitTests` measures the card at
the real modal area; thumbnails decode off the UI thread with the `Border.tload` placeholder and
the `tmissing` mark for a preview that failed.

## 9. Download

1. Fetch to temp with progress; stop is honored.
2. Size and sha256 must equal the index entry, else: `The download doesn't match what the gallery
   lists, so it was not opened.`
3. `ShareCheck` on the file (the gallery's own rules again — the index is not trusted to have
   been checked by this version), refusing a sealed Set with `Sealed Sets can't come from the
   gallery.`
4. Move into the cache, open in the Set browser through `SetReader.ReadAsync` under
   `BusyViewModel`. The Set browser's combined-chance line finds the source book by hash when
   it shipped inside, as it does today.

Errors never show exception text: every failure maps to a `ShareRules` sentence or the house
error card.

## 10. Testing

- `ShareCheckTests` (Core): a real tiny Set passes; each refusal in 4.3–4.4 built in memory and
  named (zip bomb by ratio, `..` path, symlink entry, foreign `.cbk`, sealed payload renamed
  `.set`, 513 px canvas, 1,001 assets, `files` block).
- `GitHubShareClientTests`: a fake `HttpMessageHandler` records requests; asserts the device-flow
  poll honors `slow_down`, uploads stream, rollback deletes only what the run created.
- `CommunityShareTests`: whole run against the fake, cancel at every stage.
- `CommunityIndexTests`: schema gate, unknown fields ignored, ordinal sort.
- `PublicRepoScanTests`: a clean repo passes; each extra kind found and named (stray file, extra
  branch, unlisted Release, fourth Release, stray asset, orphan tag, history over 5 MB); a
  superseded Release passes only while its PR is open; `RemoveAsync` deletes exactly the listed
  items against the fake and nothing when the confirmation is declined.
- App: VM tests for the card's limit rows and reasons; `ShareCardGestureTests` presses the real
  button; capture frames of page and card, both themes, at the real sizes.
- The Action: `tools/gallery/check.py` is a thin wrapper around `nfty share check` plus the
  account and repo checks; tested against recorded API fixtures. **The Action's logic beyond
  calling the CLI must stay small**, because the CLI is where the tested rules live.

## 11. Open questions for the author

1. **Owner of `nfty-gallery`.** Your personal account or an organization (e.g. `nfty-art`)? An org
   lets you add co-maintainers for reports without sharing your account.
2. **OAuth scope.** A classic OAuth app needs `public_repo` to create the sharer's repo and open
   the PR — that scope can write to **all** their public repos. A GitHub App can be limited to
   repos it creates, but the install step is clumsier. Ship OAuth first and say so on the connect
   card, or start with the GitHub App?
3. **Account age 30 days and one new share a day** — right numbers?
4. **Image screening.** Accept "report and delist" as the only defense, or require manual approval
   for an account's first share after all?
5. **Tags.** Resolved 2026-11-09: characters, creatures, props, items, scenery, effects, ui, abstract (editable in `nfty-gallery/tags.json`).
6. **License field.** Should a share state a license (e.g. CC BY 4.0 / all rights reserved) shown
   on the rail? Without one, "download" says nothing about reuse.
8. **Near-duplicates.** The fingerprint catches the same Set re-shared, not an extended or trimmed
   copy of someone else's. Refuse any share whose DNAs overlap another owner's listed Set by more
   than some share (needs every listed Set's DNA list in the index, ~65 bytes x assets), or leave
   it to reports?
7. **Version floor.** Should the Action refuse Sets cooked by nfty older than some version, or is
   the schema gate (`v2` floor) enough?
