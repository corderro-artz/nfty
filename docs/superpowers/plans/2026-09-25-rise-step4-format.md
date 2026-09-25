# Rise step 4 — Format Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Archives carry per-variant depth maps and a derived `PartialAlpha` ("2D only") flag, under schema 2, with schema 1 no longer read.

**Architecture:** A pixel-free `DepthMap` type lives beside the manifests in `Nfty.Ticket/Model` so both `Nfty.Core` (archives) and `Nfty.Ticket` (Menu) use it. One `Opacity` class in Core answers "solid?" and "partial?" for every caller. Writers stamp `PartialAlpha` from pixels at every level; readers that decode pixels recompute it; only `ArchivePeek` trusts the stored value. The schema bump comes LAST, after every new field exists, so the fixtures are regenerated once — from the v1 files, while v1 is still readable.

**Tech Stack:** .NET 10, C#, ImageSharp 3.1.11 (pinned), System.Text.Json, xunit.v3. Python 3 only for `tools/demo/build-demo.py`.

**Spec:** `docs/superpowers/specs/2026-09-25-rise-design.md` — sections "Where depth lives", "2D only — derived, never set", "Format". Read those before starting any task.

## Global Constraints

- Zero new NuGet packages. `Directory.Packages.props` does not change.
- `TreatWarningsAsErrors` + `GenerateDocumentationFile`: every public member gets an XML doc comment; a warning fails the build.
- US English in code, comments, messages and docs.
- Never `git add -A` / `git add .` — add files by path.
- Tests named `Snake_case_sentences`, xunit.v3, fixtures built in memory from tiny solid-fill images (see `GeneratorTests.Ing`). Filesystem tests use `Directory.CreateTempSubdirectory()`.
- Every sort that reaches a file uses `StringComparer.Ordinal`.
- All manifest JSON goes through `Json.Options` (camelCase).
- A fix is shown RED before it is made GREEN; state the red run in the task report.
- Depth layout (spec, verbatim): "`width × height × 2`, row-major, top row first; for each pixel the `front` byte then the `back` byte." Entry name `depth/{variantId}.dep` in `.igt`; `depth/{cellKey}.dep` in `.mnu`.
- Solid means `A == 255`. Partial means `0 < A < 255`. One function answers each.
- An all-zero depth map is never written. Depth on a non-solid pixel is zeroed by the writer.
- Full suite command: `dotnet test nfty.sln` (baseline before this plan: 2369 passing).

## File structure

| File | Responsibility |
|------|----------------|
| `src/Nfty.Ticket/Model/DepthMap.cs` (new) | the per-variant depth grid; bytes in, bytes out; no imaging |
| `src/Nfty.Core/Imaging/Opacity.cs` (new) | `IsSolid`, `IsPartial`, `HasPartialAlpha` at image / ingredient / recipe / book level |
| `src/Nfty.Ticket/Model/{Ingredient,Recipe,CookBook}Manifest.cs` | gain `bool PartialAlpha = false` |
| `src/Nfty.Core/Formats/Loaded.cs` | `LoadedIngredient.Depths` |
| `src/Nfty.Core/Formats/ArchiveIo.cs` | `WriteBytes` / `TryReadBytes` (+ async) for raw entries |
| `src/Nfty.Core/Formats/{Ingredient,Recipe,CookBook}Archive.cs` | stamp the flag; read/write depth |
| `src/Nfty.Core/Editing/CookBookEdits.cs`, `src/Nfty.App/ViewModels/IngredientEditorViewModel.cs`, `src/Nfty.Cli/CommandFactory.Authoring.cs`, `src/Nfty.App/Services/LooseWorkspace.cs` | carry depth through every rebuild/write |
| `src/Nfty.Ticket/Menu/{MenuBook,MenuArchive}.cs`, `src/Nfty.Core/Publish/MenuWriter.cs`, `src/Nfty.Core/Formats/MenuProjection.cs` | Menu flag + depth |
| `src/Nfty.Ticket/Model/Schema.cs`, Menu schema | 2 / 2 |
| `tests/fixtures/*`, `src/Nfty.Core/Demo/ChestDemo.cbk` | regenerated at v2 |
| `CLAUDE.md`, `tests/fixtures/README.md` | format contract rewritten |

---

### Task 1: `DepthMap` and `Opacity`

**Files:**
- Create: `src/Nfty.Ticket/Model/DepthMap.cs`
- Create: `src/Nfty.Core/Imaging/Opacity.cs`
- Modify: `src/Nfty.Core/Stats/VoxelReport.cs` (`CountPartial` uses `Opacity.IsPartial`)
- Test: `tests/Nfty.Ticket.Tests/DepthMapTests.cs`, `tests/Nfty.Core.Tests/OpacityTests.cs`

**Interfaces:**
- Produces (namespace `Nfty.Core.Model`, assembly `Nfty.Ticket`):
  ```csharp
  public sealed class DepthMap
  {
      public int Width { get; }
      public int Height { get; }
      public static DepthMap Flat(int width, int height);
      public static DepthMap FromBytes(int width, int height, ReadOnlySpan<byte> bytes); // ArgumentException when bytes.Length != width*height*2
      public byte Front(int x, int y);
      public byte Back(int x, int y);
      public bool IsFlat { get; }            // every byte zero
      public byte[] ToBytes();               // a copy, layout as above
      public DepthMap With(int x, int y, byte front, byte back); // a new map
  }
  public static class DepthMaps
  {
      public static IReadOnlyDictionary<string, DepthMap> None { get; } // empty, shared
  }
  ```
- Produces (namespace `Nfty.Core.Imaging`, assembly `Nfty.Core`):
  ```csharp
  public static class Opacity
  {
      public static bool IsSolid(Rgba32 p);    // p.A == 255
      public static bool IsPartial(Rgba32 p);  // p.A is not (0 or 255)
      public static bool HasPartialAlpha(Image<Rgba32> img); // stops at the first partial pixel
      public static bool HasPartialAlpha(IEnumerable<Image<Rgba32>> images);
      public static bool HasPartialAlpha(LoadedIngredient ing);
      public static bool HasPartialAlpha(LoadedRecipe recipe);
      public static bool HasPartialAlpha(LoadedCookBook book);
  }
  ```

- [ ] **Step 1: Write failing tests.**

`tests/Nfty.Ticket.Tests/DepthMapTests.cs`:
```csharp
using Nfty.Core.Model;

namespace Nfty.Ticket.Tests;

public class DepthMapTests
{
    [Fact]
    public void Bytes_are_front_then_back_row_major_top_row_first()
    {
        // 2x2: pixel (1,0) front 3 back 4; pixel (0,1) front 5 back 6
        byte[] bytes = [0, 0, 3, 4, 5, 6, 0, 0];
        var map = DepthMap.FromBytes(2, 2, bytes);
        Assert.Equal(3, map.Front(1, 0));
        Assert.Equal(4, map.Back(1, 0));
        Assert.Equal(5, map.Front(0, 1));
        Assert.Equal(6, map.Back(0, 1));
        Assert.Equal(bytes, map.ToBytes());
    }

    [Fact]
    public void A_wrong_length_is_refused_with_both_sizes_named()
    {
        var ex = Assert.Throws<ArgumentException>(() => DepthMap.FromBytes(2, 2, new byte[7]));
        Assert.Contains("8", ex.Message);
        Assert.Contains("7", ex.Message);
    }

    [Fact]
    public void Flat_is_all_zero_and_says_so()
    {
        var map = DepthMap.Flat(3, 2);
        Assert.True(map.IsFlat);
        Assert.Equal(new byte[12], map.ToBytes());
        Assert.False(map.With(2, 1, 0, 1).IsFlat);
    }

    [Fact]
    public void With_returns_a_new_map_and_leaves_the_original_alone()
    {
        var a = DepthMap.Flat(1, 1);
        var b = a.With(0, 0, 255, 7);
        Assert.Equal(0, a.Front(0, 0));
        Assert.Equal(255, b.Front(0, 0));
        Assert.Equal(7, b.Back(0, 0));
    }

    [Fact]
    public void FromBytes_copies_so_the_caller_cannot_mutate_the_map()
    {
        byte[] bytes = [1, 2];
        var map = DepthMap.FromBytes(1, 1, bytes);
        bytes[0] = 9;
        Assert.Equal(1, map.Front(0, 0));
    }
}
```

`tests/Nfty.Core.Tests/OpacityTests.cs`:
```csharp
using Nfty.Core.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class OpacityTests
{
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, true)]
    [InlineData(128, false, true)]
    [InlineData(254, false, true)]
    [InlineData(255, true, false)]
    public void Solid_and_partial_are_decided_by_alpha_alone(byte a, bool solid, bool partial)
    {
        var p = new Rgba32(10, 20, 30, a);
        Assert.Equal(solid, Opacity.IsSolid(p));
        Assert.Equal(partial, Opacity.IsPartial(p));
    }

    [Fact]
    public void One_partial_pixel_makes_the_image_partial()
    {
        using var img = new Image<Rgba32>(3, 3, new Rgba32(0, 0, 0, 255));
        Assert.False(Opacity.HasPartialAlpha(img));
        img[2, 2] = new Rgba32(0, 0, 0, 0);
        Assert.False(Opacity.HasPartialAlpha(img));   // fully erased is not partial
        img[1, 2] = new Rgba32(0, 0, 0, 200);
        Assert.True(Opacity.HasPartialAlpha(img));
    }
}
```
Add one test per Loaded level (ingredient, recipe, book) built with the in-memory builders used by `VoxelReportTests` — a book is partial iff any variant image anywhere in it has a partial pixel.

- [ ] **Step 2: Run to see them fail** — `dotnet test tests/Nfty.Ticket.Tests --filter FullyQualifiedName~DepthMapTests` and `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~OpacityTests`. Expected: build errors (types missing).

- [ ] **Step 3: Implement `DepthMap`.** Private `byte[] _bytes`; `FromBytes` checks `bytes.Length == checked(width * height * 2)` and throws `ArgumentException($"A {width}x{height} depth map is {width*height*2} bytes; got {bytes.Length}.")`; width/height must be positive (`ArgumentOutOfRangeException.ThrowIfNegativeOrZero`). Index = `(y * Width + x) * 2`. `IsFlat` computed once in the constructor (`_bytes.AsSpan().IndexOfAnyExcept((byte)0) < 0`) — eager, never lazily cached (spec D7). `DepthMaps.None` = `new Dictionary<string, DepthMap>()` exposed as `IReadOnlyDictionary`. Doc comments must state the byte layout and the 0..255 cap.

- [ ] **Step 4: Implement `Opacity`**, and change `VoxelReport.CountPartial` to call `Opacity.IsPartial(row[x])` so there is one definition.

- [ ] **Step 5: Run the two test classes plus `VoxelReportTests`** — all pass. Then `dotnet build nfty.sln` — 0 warnings.

- [ ] **Step 6: Commit** — `git add` the four files by path; message `feat: DepthMap and Opacity, the two primitives Rise's format needs`.

---

### Task 2: `PartialAlpha` stamped by writers, recomputed by readers

**Files:**
- Modify: `src/Nfty.Ticket/Model/IngredientManifest.cs`, `RecipeManifest.cs`, `CookBookManifest.cs`
- Modify: `src/Nfty.Core/Formats/IngredientArchive.cs`, `RecipeArchive.cs`, `CookBookArchive.cs`
- Test: `tests/Nfty.Core.Tests/PartialAlphaFlagTests.cs`

**Interfaces:**
- Consumes: `Opacity.HasPartialAlpha(...)` (Task 1).
- Produces: each manifest record gains a LAST positional parameter `bool PartialAlpha = false` (after `SchemaVersion`/`TargetSupply`/`Palette`/`RecipeOrder`/`AbsentPercent` — append after the current last parameter of each). Documented as: "Whether any variant under this manifest carries a partial-alpha pixel — the book-level value is what the UI calls '2D only'. A listing cache: writers stamp it from the pixels on every write, and anything that decoded the pixels recomputes it rather than reading it."

Rules:
- `IngredientArchive.Write*` writes `manifest with { PartialAlpha = Opacity.HasPartialAlpha(variantImages.Values) }` — the caller's value is ignored.
- `RecipeArchive.Write*` stamps from its ingredients' images; `CookBookArchive.Write*` from its recipes'.
- `IngredientArchive.Read*` returns a `LoadedIngredient` whose manifest is `with { PartialAlpha = <recomputed from decoded images> }`. Recipe/CookBook readers recompute from their loaded children.
- `ArchivePeek` is untouched: it returns the stored manifests, so it reads the stamped value for free.

- [ ] **Step 1: Failing tests** in `PartialAlphaFlagTests.cs` (temp dirs; in-memory builders):
  1. `The_writer_stamps_the_ingredient_flag_from_pixels_whatever_the_caller_said` — write an ingredient whose one variant has a pixel at `A=128` with a manifest saying `PartialAlpha: false`; open the ZIP, parse `manifest.json` with `System.Text.Json`, assert `"partialAlpha": true`.
  2. Same at recipe and cookbook level (write a `.cbk` with one partial pixel in one variant of one recipe; assert the outer `manifest.json` says true, and a clean book says false).
  3. `A_reader_recomputes_rather_than_trusting_the_stored_flag` — write a clean ingredient, then rewrite its `manifest.json` entry by hand to `"partialAlpha": true`; `IngredientArchive.Read` returns `Manifest.PartialAlpha == false`.
  4. `Peek_reads_the_stored_flag_without_decoding` — `ArchivePeek.CookBookTree` on a book with a partial pixel reports `PartialAlpha == true` on the book, that recipe and that ingredient.
- [ ] **Step 2: Run, see red** (compile error on the new property, then assertion failures).
- [ ] **Step 3: Implement** as specified above, sync AND async paths identically (the file's own pattern: share a private helper so they cannot drift).
- [ ] **Step 4: Run the new tests, `ArchivePeekTests`, `FixtureArchiveTests`, `SchemaCompatibilityTests`, then the full suite.** Expected all green (the v1 fixtures lack the field; it reads as the default and is recomputed).
- [ ] **Step 5: Commit** — `feat: PartialAlpha, stamped from pixels on write and recomputed on read`.

---

### Task 3: Depth maps in the `.igt`, carried through every edit

**Files:**
- Modify: `src/Nfty.Core/Formats/Loaded.cs` — `LoadedIngredient.Depths`
- Modify: `src/Nfty.Core/Formats/ArchiveIo.cs` — raw-byte entry helpers
- Modify: `src/Nfty.Core/Formats/IngredientArchive.cs`, `RecipeArchive.cs`, `CookBookArchive.cs`
- Modify (carry-through): `src/Nfty.Core/Editing/CookBookEdits.cs` (`RemoveVariant`, ~line 194), `src/Nfty.App/ViewModels/IngredientEditorViewModel.cs` (save path, ~lines 1490–1525), `src/Nfty.Cli/CommandFactory.Authoring.cs` (`add variant`, ~line 501; `new ingredient`, ~line 97), `src/Nfty.App/Services/LooseWorkspace.cs` (~line 57)
- Modify: every test that calls `IngredientArchive.Write`/`WriteAsync` (22 call sites) — pass `DepthMaps.None`
- Test: `tests/Nfty.Core.Tests/DepthMapArchiveTests.cs`, `tests/Nfty.Core.Tests/DepthCarryThroughTests.cs`, plus one App test and one CLI test (below)

**Interfaces:**
- Consumes: `DepthMap`, `DepthMaps.None`, `Opacity.IsSolid` (Task 1).
- Produces:
  ```csharp
  // Loaded.cs
  public IReadOnlyDictionary<string, DepthMap> Depths { get; init; } = DepthMaps.None; // keyed by variant id; absent = paper

  // ArchiveIo.cs
  public static void WriteBytes(ZipArchive zip, string name, byte[] bytes);
  public static byte[]? TryReadBytes(ZipArchive zip, string name);            // null when the entry is absent
  public static Task WriteBytesAsync(ZipArchive zip, string name, byte[] bytes, CancellationToken ct);
  public static Task<byte[]?> TryReadBytesAsync(ZipArchive zip, string name, CancellationToken ct);

  // IngredientArchive.cs — the depths parameter is REQUIRED on every Write/WriteAsync overload,
  // so the compiler names every caller that could drop depth:
  public static void Write(ZipArchive zip, IngredientManifest manifest,
      IReadOnlyDictionary<string, Image<Rgba32>> variantImages,
      IReadOnlyDictionary<string, DepthMap> depths);
  // (and the path overload, and both async twins, with the same added parameter)
  ```

Rules (spec, "Depth maps"):
- Write: for each variant with an entry in `depths`, zero front/back on every pixel where `!Opacity.IsSolid(image[x,y])`; if the result `IsFlat`, write nothing; otherwise write `depth/{variantId}.dep` = `ToBytes()`. A depth map whose width/height differ from its variant image throws `ArgumentException` naming the variant (a caller bug).
- Write ignores `depths` entries whose key is not a variant of this manifest.
- Read: for each variant, `TryReadBytes("depth/{id}.dep")`; null → no entry in `Depths`; wrong length → `InvalidDataException("Ingredient '{ingId}' variant '{variantId}' has a {len}-byte depth map; its {w}x{h} image needs {w*h*2}.")` (same class of refusal as `EnsureUniqueVariantIds`). Disposes any decoded images before throwing (follow the existing catch pattern).
- `RecipeArchive`/`CookBookArchive` pass `ing.Depths` down when writing nested ingredients.
- Carry-through, each with its own test:
  - `CookBookEdits.RemoveVariant` → the trimmed ingredient's `Depths` = the old `Depths` minus the removed variant id.
  - Ingredient editor save → the saved ingredient keeps the ORIGINAL ingredient's depth maps for every variant id that survives the edit (a variant whose pixels changed keeps its map; the writer zeroes any depth now over transparency).
  - CLI `add variant` → the rewritten `.igt` keeps every existing variant's depth map.
  - `LooseWorkspace` save → passes `ing.Depths`.
  - CLI `new ingredient` and the App's new/import paths create fresh ingredients: pass `DepthMaps.None` explicitly.

- [ ] **Step 1: Failing archive tests** (`DepthMapArchiveTests.cs`):
  1. `A_depth_map_round_trips_through_an_igt` — 2x2 all-solid variant, map with (1,0)=front 3/back 4; write, read, assert `Depths["v"].ToBytes()` equals what was written.
  2. `A_flat_map_is_not_written_at_all` — write with a `DepthMap.Flat`; the ZIP has no `depth/` entry; read gives no `Depths` key.
  3. `Depth_over_a_transparent_pixel_is_zeroed_on_write` — pixel (0,0) `A=0` with front 9; after round trip (0,0) is 0/0 and the solid pixel's depth survives.
  4. `A_map_that_is_only_depth_over_transparency_is_not_written` — combining 2 and 3.
  5. `A_depth_map_of_the_wrong_length_is_refused_naming_the_variant` — write a clean igt, add a 3-byte `depth/v.dep` entry by hand, `Read` throws `InvalidDataException` whose message contains the ingredient id, the variant id, `3` and `8`.
  6. `Depth_survives_nesting_in_a_recipe_and_a_cookbook` — a `.cbk` round trip keeps the map.
  7. Async twins of 1 and 5.
- [ ] **Step 2: Run, red.**
- [ ] **Step 3: Implement** `Loaded.cs`, `ArchiveIo` helpers, the three archives. Then fix every compile error from the new required parameter: src callers pass the ingredient's real `Depths` (never `DepthMaps.None` where an existing ingredient is at hand); test callers pass `DepthMaps.None`.
- [ ] **Step 4: Failing carry-through tests** (`DepthCarryThroughTests.cs` in Core for `RemoveVariant`; an App test beside the existing editor save tests — find them with `grep -rln "SaveCommand\|Save()" tests/Nfty.App.Tests/IngredientEditor*` — that opens a book whose ingredient has a depth map on variant `a`, edits one pixel, saves, reopens the file and asserts the map is still there; a CLI test beside `AuthoringCommandsTests`' `add variant` tests asserting the existing variant's map survives). Show each red by temporarily passing `DepthMaps.None` at that site, then restore.
- [ ] **Step 5: Implement the carry-through** at the four sites listed above.
- [ ] **Step 6: Full suite green; 0 warnings.**
- [ ] **Step 7: Commit** (may be two commits: archive, then carry-through) — add every touched file by path.

---

### Task 4: Menu carries the flag and the depth maps

**Files:**
- Modify: `src/Nfty.Ticket/Menu/MenuBook.cs` — `MenuBook` gains a LAST positional `bool PartialAlpha = false`
- Modify: `src/Nfty.Core/Formats/MenuProjection.cs` — sets it from `Opacity.HasPartialAlpha(book)`
- Modify: `src/Nfty.Core/Publish/MenuWriter.cs` — writes `depth/{cellKey}.dep` for every cell whose variant has a non-flat depth map (same zeroing rule as Task 3; a map already written by an `.igt` is already zeroed, but the Menu writer must not rely on that)
- Modify: `src/Nfty.Ticket/Menu/MenuArchive.cs` — reads depth lazily like `ArtBytes`
- Modify: the Menu schema constants (`MenuSchema.Current` and `MenuSchema.Oldest` → 2) and the comment in `MenuArchive` that says nothing has ever been older than schema 1 (reword: versions below `Oldest` are either malformed or from a build this one no longer reads)
- Test: `tests/Nfty.Ticket.Tests/MenuArchiveTests.cs`, `tests/Nfty.Cli.Tests/MenuCommandTests.cs` (extend)

**Interfaces:**
- Consumes: `DepthMap`, `LoadedIngredient.Depths`, `Opacity`.
- Produces:
  ```csharp
  // MenuArchive
  public DepthMap? Depth(string cellKey); // null = paper; sized to the cell (Cell(cellKey).Width x Height)
  ```
  `KeyNotFoundException` for an unknown cell key (same as `Cell`); `InvalidDataException` naming the key for a wrong-length entry.

- [ ] **Step 1: Failing tests:** a Menu written from a book whose one variant has a depth map returns that map from `Depth(key)` in both `atlas` and `loose` modes; a variant without one returns null; the manifest's `partialAlpha` is true for a book with one partial pixel and false otherwise; a `menuSchemaVersion: 1` manifest is refused with a message naming 1.
- [ ] **Step 2: Red. Step 3: Implement. Step 4: Full suite green.** Existing Menu tests that write `menuSchemaVersion: 1` by hand are updated to 2 (they test other things).
- [ ] **Step 5: Commit** — `feat: .mnu carries depth maps and the 2D-only flag (menu schema 2)`.

---

### Task 5: Schema 2 — v1 refused, fixtures and demo regenerated, contract rewritten

**Files:**
- Modify: `src/Nfty.Ticket/Model/Schema.cs` (`Current = 2`, then `Oldest = 2`)
- Modify: `src/Nfty.Core/Formats/UnsupportedSchemaVersionException.cs` (messages only if they claim nothing is older than 1)
- Modify: `tests/Nfty.Core.Tests/SchemaCompatibilityTests.cs`, `SchemaVersionTests.cs`, and any test that writes `schemaVersion: 1` JSON by hand (`grep -rn "schemaVersion" tests`)
- Replace: `tests/fixtures/VaporPets.cbk`, `cat.rcp`, `aura.igt`
- Modify: `tests/Nfty.Core.Tests/FixtureArchiveTests.cs`, `FixtureFreezeTests.cs`
- Replace: `src/Nfty.Core/Demo/ChestDemo.cbk` (via `python tools/demo/build-demo.py`)
- Modify: `tests/fixtures/README.md`, `CLAUDE.md` (the format paragraph under "Big picture" and the `tests/fixtures/` bullet at the end of Conventions)
- Check: `AGENTS.md`, `.clinerules/project.md`, `.github/copilot-instructions.md`, `.opencode/AGENTS.md` for the same claims; fix any that state "every field added after v1" or "schemaVersion 1".

**Interfaces:**
- Consumes: everything above.
- Produces: `Schema.Current == 2`, `Schema.Oldest == 2`; v2 fixtures containing one depth map.

- [ ] **Step 1: Regenerate the fixtures FROM the v1 files, before v1 stops being readable.** Set `Schema.Current = 2` only (leave `Oldest = 1`). Write a temporary test `tests/Nfty.Core.Tests/RegenerateFixturesOnce.cs` that: reads `tests/fixtures/VaporPets.cbk`, `cat.rcp`, `aura.igt` with the current readers; adds to ingredient `aura`, variant `glow`, a depth map where every solid pixel in the top half has front 2 / back 1 (the rest 0); writes all three back with the current writers to the same paths (delete first — `Write` opens in Create mode). Run it once. DELETE the temporary test file. It must never be committed.
- [ ] **Step 2: Extend `FixtureArchiveTests`** with what the new files contain: every manifest's `schemaVersion` is 2; `PartialAlpha` is false at every level; `aura/glow` carries the depth map described in Step 1 (assert a top-half solid pixel is 2/1 and a bottom-half one is 0/0); `spark` has none. Update the frozen hashes in `FixtureFreezeTests` to the new files' hashes (the test prints what it saw when it fails — use that output, never a hash computed any other way).
- [ ] **Step 3: Set `Schema.Oldest = 2`.** Rewrite `SchemaCompatibilityTests`/`SchemaVersionTests` so they pin: a v2 manifest reads; a v1 manifest is refused with `UnsupportedSchemaVersionException` whose message names version 1; a v3 manifest is refused as newer. Fix every other test that hand-writes a v1 manifest (bump to 2 — they test other things).
- [ ] **Step 4: Rebuild the demo.** `dotnet build src/Nfty.Cli` then `python tools/demo/build-demo.py` (read its header first; it builds through the CLI's own authoring commands). Then run `DemoCookBookTests` and whichever test reads `tests/fixtures/roll-baseline-chest-demo.txt` (`grep -rln roll-baseline tests`). **The roll baseline must not change** — the demo's content is identical, only its schema moved. If it changes, stop and report; do not regenerate the baseline.
- [ ] **Step 5: Docs.**
  - `CLAUDE.md` format paragraph: the gate is still `Schema.Oldest..Schema.Current` and still refuses newer; state that **v2 is the floor** (Rise, 2026-09-25: v1 dropped by the author's decision, no archives kept), that `.igt` carries `depth/{variantId}.dep` and every manifest carries `partialAlpha` (a listing cache, recomputed by anything that decodes pixels), and that the additive-field contract now reads "every field added after **v2** is optional". Keep the file's compressed style.
  - `CLAUDE.md` `tests/fixtures/` bullet: the v1 fixtures were replaced by v2 ones on 2026-09-25 by the author's decision; from v2 on the rule "don't regenerate to make a test pass; a format change adds a fixture beside" holds again.
  - `tests/fixtures/README.md`: provenance (written at schemaVersion 2 on 2026-09-25 by regenerating the v1 files through the v2 writer), and the `aura/glow` depth map in the "What's here" description.
- [ ] **Step 6: Full suite green, 0 warnings.** `git status` must show no stray files (the regenerator is gone).
- [ ] **Step 7: Commit** — two commits are fine: `feat: schema 2 — v1 is no longer read` (code, tests, fixtures, demo) and `docs: the format contract from schema 2` (CLAUDE.md, README, mirrors).

---

## Self-review notes

- Spec coverage: depth per variant in `.igt` (T3), absent = paper (T3), all-zero not written (T3), transparent zeroed (T3), wrong length refused (T3 — at READ, as corruption, not a Validator problem: the reader cannot construct a malformed map; recorded as a deviation from the spec's wording), `PartialAlpha` at igt/rcp/cbk/mnu (T2, T4), recomputed when pixels decoded (T2), `.mnu` depth by cell key (T4), schema 2 with v1 refused (T5), v2 fixtures replacing v1 (T5), CLAUDE.md paragraph (T5), demo still readable (T5).
- Deviation recorded: `PartialAlpha` is a defaulted record parameter rather than a non-nullable required one, because 500+ positional constructor calls exist; the writer always stamps it, so it is always present in written JSON.
