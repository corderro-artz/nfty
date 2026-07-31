# nfty GUI — Set browser (design spec)

**Date:** 2026-07-26
**Status:** Approved (design), pending implementation planning
**Scope:** Behavior slice: view a cooked Set in-app. Adds a `Nfty.Core` **`SetReader`** (reads a cooked
folder or a `.set` zip into a `LoadedSet` — collection info + per-item metadata + image paths), and a GUI
**Set browser** screen (virtualized thumbnail grid + item detail) reached from Landing's
"Open a cooked .set…". Closes the cook → view loop.
**Builds on:** merged Cook → generate (writes Sets). The output format is what `SetWriter` produces:
`images/NNNN.png` + `metadata/NNNN.json` (OpenSea) + `nfty/NNNN.json` (`NftyMetadata`) + `set.json`
(`SetManifest`); packed is a `.set` zip of that. All JSON is `Json.Options` (camelCase).

## 0. Program bar
Rock-solid, efficient; best practices; pull docs rather than assume; escalate anything off. No locked
mockup for this screen — style it cleanly with the token/foundation styles (visual polish later). This is
a genuine `Nfty.Core` addition (the engine owns its output format); it gets Core round-trip tests like the
other archive readers.

## 1. Goals & non-goals
**Goals**
- `Nfty.Core.Output.SetReader.Read(path)` reads a cooked **folder** or a **`.set`** zip → a `LoadedSet`:
  the `SetManifest` (collection name/symbol-ish/count/seed/distribution/rarity) + `IReadOnlyList<SetItem>`
  (number, **image path**, dna, recipe, per-trait rarity, layer colours) parsed from `set.json` +
  `nfty/NNNN.json`. **Paths, not decoded images**, so a large Set doesn't materialize every PNG in memory.
- A Set browser screen: a virtualized grid of item thumbnails (decoded lazily/downscaled from the image
  paths) + an item-detail rail (number, dna, recipe, rarity table, layer colours).
- Landing's "Open a cooked .set…" opens a `.set` (or folder) → reads → navigates to the browser; read
  failures surface via the error dialog.

**Non-goals (this slice)**
- Sort/filter, rarity leaderboard, per-item export/regenerate/reveal, extend-from-browser, a
  Cook-done → browser handoff, editing. Mockup-fidelity polish. Reading `metadata/NNNN.json` (OpenSea) —
  the browser uses `nfty/NNNN.json` (richer); OpenSea files are ignored.

## 2. Components

### 2.1 `Nfty.Core.Output.SetReader` + `LoadedSet`
```
public record SetItem(int Number, string ImagePath, string Dna, string Recipe,
    IReadOnlyList<RarityAttribute> Rarity, IReadOnlyList<LayerColor> Layers);

public sealed class LoadedSet : IDisposable
{
    public required SetManifest Manifest { get; init; }
    public required IReadOnlyList<SetItem> Items { get; init; }
    // if a .set was extracted to a temp dir, LoadedSet owns + deletes it on Dispose; else Dispose is a no-op.
}

public static class SetReader
{
    public static LoadedSet Read(string path);
    public static Task<LoadedSet> ReadAsync(string path, CancellationToken ct = default);  // async twin (I/O)
}
```
Behaviour:
- If `path` ends with `.set` (or is a file): extract the zip to a fresh temp dir
  (`Directory.CreateTempSubdirectory()`), read that, and record the temp dir on the `LoadedSet` for
  cleanup. If `path` is a directory: read it in place (no temp, `Dispose` no-op).
- Read `set.json` → `SetManifest` (via `Json.Options`; through `ArchiveIo.ReadManifest`-style schema check
  if applicable — else `JsonSerializer` with `Json.Options`). A missing/invalid `set.json` throws a
  clear exception (message worth showing the user).
- Enumerate `nfty/*.json` (ordered by number); each → `NftyMetadata`; `ImagePath = images/{NNNN}.png`
  (`D4` stem, matching `SetWriter.PathsFor`). Build a `SetItem` (Number=`SetNumber`, Dna, Recipe,
  Rarity=`meta.Rarity`, Layers=`meta.Layers`). Skip/■error on an item whose image is missing (decide:
  include with the path anyway — the browser shows a placeholder; do NOT throw for one missing image).
- No ImageSharp use — the reader is JSON + file paths + optional unzip only.
- Round-trip Core tests: `SetWriter.Write(generatedSet, tmp, pack:false)` → `SetReader.Read(tmp)` asserts
  Manifest.Count + item count/dna/recipe; and `pack:true` → `Read(tmp + ".set")` asserts the same (temp
  extraction). Mirror the existing `FixtureArchiveTests`/round-trip style; clean up temp dirs.

### 2.2 `SetBrowserViewModel(LoadedSet set)`
- Header: `set.Manifest.Name`, `Count`, `Seed`.
- `Items`: one row VM per `SetItem` exposing `Number`, a **lazily-decoded thumbnail** `Bitmap` (from
  `ImagePath` via `Bitmap.DecodeToWidth(stream, thumbW)` — small, e.g. 96px — so the grid holds
  downscaled bitmaps, not full-res). Decode on demand (first bind) or eagerly with a cap; MVP: decode all
  at a small size (acceptable for typical set sizes; note large-set lazy/virtualized decoding as deferred
  refinement).
- `SelectedItem` → detail: Number, Dna (mono), Recipe, the **rarity table** (`RarityAttribute` rows:
  trait · value · %), layer colours (`LayerColor`).
- `IDisposable`: disposes every thumbnail `Bitmap` and the `LoadedSet` (which deletes its temp dir).

### 2.3 `SetBrowserView`
A page `UserControl` (`ViewLocator`-resolved): a header (collection name · N items · seed); a
**virtualized** thumbnail grid — an `ItemsControl`/`ItemsRepeater` with a virtualizing `WrapPanel`/uniform
layout inside a `ScrollViewer` (so thousands of tiles don't all realize); click a tile → `SelectedItem`;
an item-detail rail (number/dna/recipe/rarity table/colours). Token/foundation styles; no raw hex. Confirm
the Avalonia 11.2 virtualization approach (ItemsRepeater vs virtualizing panel) via docs before building.

### 2.4 Entry point
- `LandingViewModel.OpenSet` (currently a disabled `Report` stub): enable it; `path = await
  _picker.OpenFileAsync("Open a cooked .set", ".set")` (a folder variant may use `PickFolderAsync` later —
  MVP is the `.set` file). Null → return. `try { set = SetReader.Read(path); } catch → error dialog`.
  Then `_nav.To(_setBrowserFactory(set))`.
- DI: `Func<LoadedSet, SetBrowserViewModel> setBrowserFactory` (mirrors the explorer/cook factories);
  `LandingViewModel` gains it. The browser is a nav-stack **page** — freed by `NavigationService.Back()`
  (already disposes popped `IDisposable` pages), which disposes its thumbnails + `LoadedSet`.

## 3. Data flow
```
Landing.OpenSet → OpenFileAsync(".set") → SetReader.Read(path) → LoadedSet   [throws → ErrorDialog]
  → nav.To( SetBrowserViewModel(set) )
       header + Items(thumbnail per SetItem) ; select → item detail (dna/recipe/rarity/colours)
  → Back → NavigationService disposes the browser (thumbnails + LoadedSet + temp dir)
```

## 4. Testing
- **Core `SetReader`** (`Nfty.Core.Tests`): write a small generated Set to a temp dir (folder + packed),
  read it back, assert `Manifest.Count`, item count, and a sample item's dna/recipe/rarity; a `.set`
  extracts to temp and `Dispose` removes it; a missing `set.json` throws. Temp dirs cleaned up.
- **`SetBrowserViewModel`** (`[AvaloniaFact]`): from a real cooked temp Set (write via `SetWriter`, read
  via `SetReader`) → `Items` non-empty with non-null thumbnails of the expected small size; selecting an
  item exposes its dna/recipe/rarity; `Dispose` frees thumbnails + the set (temp dir gone).
- **`LandingViewModel.OpenSet`**: fake picker returns a real `.set`/folder → `SetReader.Read` + nav to a
  `SetBrowserViewModel`; picker null → no nav; a bad path → error dialog, no nav. (Enable the command.)
- **Visual:** render `SetBrowserView` (fixture Set) via the capture harness, both themes — grid + detail;
  view it, iterate for clean layout. No golden images.
- Full suite green; build 0 warnings; no raw hex outside `Tokens.axaml`.

## 5. Out of scope
Sort/filter, rarity leaderboard, per-item export/reveal, extend, Cook→browser handoff, editing, OpenSea
metadata reading, mobile heads, dialog/screen mockup-fidelity polish.

## 6. Risks & escalation
- **Virtualization** — confirm the Avalonia 11.2 virtualizing panel/`ItemsRepeater` approach for a
  thumbnail grid before building; a non-virtualized grid over a large Set would OOM. If lazy per-tile
  decode is needed for very large Sets, note it; MVP may decode all at small size for typical Sets.
- **`Bitmap.DecodeToWidth`** — confirm the exact 11.2 API for decoding a downscaled bitmap from a file
  stream; fall back to `new Bitmap(path)` (full-res) only if necessary, noting the memory cost.
- **`set.json` schema** — `SetManifest` carries `GeneratorVersion` but is not an `ISchemaVersioned`
  archive manifest; read it with `Json.Options` directly. If a schema/version check is warranted, mirror
  `ArchiveIo.ReadManifest`; otherwise a clear parse-failure message suffices.
- **Temp-dir lifetime** — `LoadedSet.Dispose` deletes an extracted temp dir; ensure the browser is the
  sole owner and `NavigationService.Back` disposes it. A never-popped browser (app exit) leaves the temp
  dir to the OS — acceptable.
- **No `Nfty.Core` regression** — `SetReader` is additive; existing Core tests must stay green.
