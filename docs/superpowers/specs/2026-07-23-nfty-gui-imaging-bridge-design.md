# nfty GUI — Imaging Bridge: real art across the UI (design spec)

**Date:** 2026-07-23
**Status:** Approved (design), pending implementation planning
**Scope:** The next Phase-2 behavior slice of the Avalonia GUI: introduce an
`Image<Rgba32>` → Avalonia `Bitmap` bridge and use it to render **real art** through the
`Nfty.Core` cook path everywhere images appear — the Explorer detail panes (ingredient hero,
variant thumbnails, colorways swatches, composited recipe hero with functional reroll) and the
Ingredient Editor (canvas + live Colorize preview), wiring the editor to the opened ingredient.
**Builds on:** `2026-07-22-nfty-gui-phase2a-open-explorer-design.md` (Phase 2a, merged: Open/Import
→ Explorer bound to real data, text/metrics only — variant images were explicitly deferred to this
slice). Detail VMs already hold real `Loaded*` graphs; this slice adds the imagery.

## 1. Goals & non-goals

**Goals**
- A single, reusable `Image<Rgba32>` → `Bitmap` conversion, head-agnostic, in `Nfty.App`.
- Every image surface in the shipped screens renders **real** art via existing `Nfty.Core` seams:
  - Single-layer colorized value-maps (`Colorizer.Apply`) for ingredient hero / variant thumbnails /
    colorways swatches / editor canvas + preview; **custom** kind rendered as-is (never colorized).
  - A **composited recipe hero** rolled through the real pipeline (`Generator.GenerateStreaming`
    pinned to the recipe), with a functional **Reroll**.
- The **Ingredient Editor** wired to the opened `LoadedIngredient`: real variant filmstrip, canvas
  showing the selected variant's value-map, and a Colorize-rail **Preview** that re-renders live as
  Mode / Hue / Sat / Fixed-colour change.
- Correct **ownership**: VMs own the `Bitmap`s they create and dispose them; no Core image outlives
  the bridge call; no leaks on selection churn or navigation.

**Non-goals (this slice)**
- **No `Nfty.Core` change.** Every render uses an existing public seam.
- Real pixel **painting**, **undo/redo**, and draft **Save** in the editor — remain stubs (the
  dedicated editor slice).
- **Cook-to-disk** generation (`generate`/`extend` write path), the **Set-browser** grid over large
  collections, **mobile heads**, and the **visual mockup-fidelity pass** — later slices.

## 2. Components

### 2.1 `IImageBridge` — the conversion seam
New head-agnostic service in `Nfty.App.Services`, registered as a stateless **singleton** in
`AddNftyApp()`:
```
public interface IImageBridge
{
    Avalonia.Media.Imaging.Bitmap ToBitmap(SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> image);
}
```
Implementation (`ImageBridge`): allocate a
`WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul)`,
lock its framebuffer, and copy pixels straight from ImageSharp with `image.CopyPixelDataTo(span)` —
ImageSharp `Rgba32` is byte order R,G,B,A, matching `Rgba8888`; no PNG encode/decode round-trip. The
returned `Bitmap` holds an **independent copy** of the pixels, so the caller disposes the source
`Image<Rgba32>` (or `GeneratedAsset`) immediately after the call — no `Nfty.Core` image is retained by
the App layer beyond conversion.

`Bitmap`/`WriteableBitmap` are managed Avalonia types available to `Nfty.App`; the bridge is therefore
**not** head-specific (unlike the file picker). It needs no `TopLevel` and constructs under
`Avalonia.Headless`, so it is unit-testable.

### 2.2 Colour derivation (single-layer surfaces)
For an ingredient of a given `LayerKind`, a display image is produced from a variant's value-map:
- **Custom** → the variant image **as-is** (`LoadedIngredient.VariantImages[variantId]`); no colorize.
- **Static** → `ColorRoller.FromFixed(entry.Fixed, colorization.Model)` gives `(H,S)`; then
  `Colorizer.Apply(valueMap, H, S, Model)`. Deterministic, consumes no RNG.
- **Dynamic** → `ColorRoller.Roll(colorization, rng)` for `(H,S)`, then `Colorizer.Apply(...)`. The RNG
  is a `SplitMix64` seeded from a **stable string** (the variant id) via `SeedHash`, so a thumbnail/hero
  is identical across sessions; a **Reroll** advances to a new seed (e.g. an incrementing salt appended
  to the id) to sample a different legal colour.
- **Colorways swatches** (ingredient rail): for dynamic, N (default 6) evenly-spaced hues across the
  colorization's hue range at a representative saturation → N colorized thumbnails, previewing the
  palette. For static, the single fixed colour. For custom, the raw image (kind note: "composited
  as-is").

Static's single fixed colour lives in a `ColorEntry.Fixed`; a static ingredient is validated to carry
exactly one. Dynamic ranges come from the `ColorEntry.Range`s. The App never re-implements colour math —
it only calls `ColorRoller` + `Colorizer`.

### 2.3 Recipe hero (composited, real pipeline)
The recipe detail hero is one asset of **that recipe**, produced by the real generator pinned to it:
```
var opts = new GenerateOptions(Count: 1, Seed: RollSeed.ToString(), RecipeId: recipe.Manifest.Id,
                               EnforceUniqueDna: false);
using var asset = Generator.GenerateStreaming(book, opts).First();   // rolls → rules → colorize → composite
Hero = _bridge.ToBitmap(asset.Image);                                // copy out, then asset disposes
```
`EnforceUniqueDna: false` so a single-asset roll can never hit dedup exhaustion; `RecipeId` pins the
weight table to `{ recipe: 1 }` (Core already supports this — the CLI `generate --recipe`). **Reroll**
increments `RollSeed` and rebuilds the hero (disposing the previous hero bitmap first). Rules,
colorization, and compositing are honoured exactly as a cooked asset would be. The `GeneratedAsset` is
disposed as soon as its pixels are copied into the `Bitmap`.

### 2.4 Explorer detail VMs — imagery added
The three detail VMs (constructed by `ExplorerViewModel` on node selection, Phase 2a) gain the bridge
(via ctor) and expose `Bitmap` properties, computed in their constructors:
- **`CookBookDetailViewModel`** — unchanged text/metrics; optionally a small montage is **out of scope**
  (the cookbook pane shows recipe cards, no hero in the mockup) — no bitmaps this slice unless the
  recipe-card thumbnails are cheap to add; if added they follow §2.3 pinned per recipe. Default: no
  cookbook-level bitmap.
- **`RecipeDetailViewModel`** — a `Bitmap Hero` per §2.3; `Reroll` rebuilds it.
- **`IngredientDetailViewModel`** — `Bitmap Hero` (the selected/first variant per §2.2), a `Bitmap` per
  `VariantRow` (thumbnail), and the colorways swatch bitmaps (§2.2). Selecting a variant updates the
  hero.

### 2.5 Ingredient Editor wired to real data
`IngredientEditorViewModel` today is a Phase-1 stub: ctor `(INavigationService, INotYetWired)`, an
in-memory `EditorVariant` filmstrip, and colorize controls with no image. This slice:
- **Ctor** gains the opened `LoadedIngredient` (+ `LoadedRecipe`, `LoadedCookBook` for canvas size /
  context) via a DI factory `Func<LoadedIngredient, LoadedRecipe, LoadedCookBook, IngredientEditorViewModel>`,
  mirroring the Phase-2a `Func<LoadedCookBook, ExplorerViewModel>` pattern.
- **Navigation:** the Ingredient detail's ✏ (`EditIngredient`, today a `Report` stub) navigates to the
  editor built from the current ingredient. `ExplorerViewModel` supplies the factory (it already holds
  the `(LoadedRecipe, LoadedIngredient)` for the selected node).
- **Filmstrip** = the ingredient's real variants, each with a thumbnail (§2.2). Selecting one sets the
  canvas source.
- **Canvas** (`Column 2`, today an empty `Border`) shows the selected variant's value-map, colorized by
  the current editor colorize state (Mode/Hue/Sat/Fixed) — for a custom ingredient, the raw image.
- **Colorize rail Preview** (the small `Border`, today empty) re-renders whenever Mode / HueMin/Max /
  SatMin/Max / FixedColor change (hook the existing `[ObservableProperty]` setters). **Reroll** samples
  a new dynamic colour; **Enlarge / Fill pane** swap which surface the preview drives (ui-state only,
  no new window this slice — they re-target the existing preview/canvas). Painting, undo/redo, Save
  stay `Report` stubs.
- The editor's colorize controls already exist as observable properties; this slice feeds them into a
  `Colorization`/`FromFixed` call and renders — it does **not** yet persist them back to a manifest.

### 2.6 Lifetime & disposal (VM-owns-and-disposes)
Avalonia `Bitmap` holds an unmanaged surface and is `IDisposable`. Ownership:
- Each detail VM and the editor VM implement **`IDisposable`**, build their bitmaps in the ctor (and,
  for surfaces that change, on the relevant property change — disposing the old bitmap first), and
  dispose all held bitmaps in `Dispose()`.
- **`ExplorerViewModel` implements `IDisposable`.** In `OnSelectedNodeChanged` it disposes the
  **previous** `CurrentDetail` (if `IDisposable`) before assigning the new one — the high-churn path,
  fully internal. Its own `Dispose()` disposes the current detail.
- **`NavigationService.Back()`** disposes the **popped** page if it is `IDisposable` (frees the editor's
  bitmaps when the user leaves it). `NavigationService` implements `IDisposable` and disposes every page
  still on the stack at container shutdown. `To(page)` does **not** dispose the outgoing page — it
  remains in history beneath the new one.
- Re-opening a different cookbook from Landing goes through `Back` (popping/disposing the current
  Explorer) before `Open`, so no buried Explorer is stranded. The session still owns and disposes the
  `LoadedCookBook` (decoded PNGs); detail bitmaps are independent copies freed by their VMs.
- **No `GeneratedAsset` or `Image<Rgba32>` is held** past a `ToBitmap` call; sources are disposed
  immediately, so the only long-lived image resources in the App are the VM-owned `Bitmap`s.

## 3. Data flow

```
select Ingredient node
  → IngredientDetailViewModel(ing, recipe, book, bridge, …)
      custom → bridge.ToBitmap(VariantImages[id])
      static → Colorizer.Apply(map, ColorRoller.FromFixed(fixed, model)…) → bridge.ToBitmap
      dynamic→ Colorizer.Apply(map, ColorRoller.Roll(coloriz, seededRng)…) → bridge.ToBitmap
      colorways → N hue samples → N bitmaps

select Recipe node → RecipeDetailViewModel.Hero
  → Generator.GenerateStreaming(book, {Count 1, Seed RollSeed, RecipeId, EnforceUniqueDna false}).First()
  → bridge.ToBitmap(asset.Image) ; asset.Dispose()
  Reroll → RollSeed++ → dispose old Hero → rebuild

Ingredient detail ✏ → nav.To( editorFactory(ing, recipe, book) )
  → filmstrip thumbnails + canvas(selected variant) + live Colorize preview
  → Back → NavigationService disposes the popped editor (its bitmaps)
```

## 4. Testing

Headless (`Avalonia.Headless.XUnit`); in-memory `Loaded*` graphs built as the Core/App tests do (tiny
solid-fill `Image<Rgba32>`s). Anything constructing a `Bitmap`/`WriteableBitmap` or other Avalonia
control uses **`[AvaloniaFact]`**; pure records/logic use `[Fact]`.

- **`ImageBridge`** — a 2×2 `Image<Rgba32>` with known pixels → a `Bitmap` of matching `PixelSize`, and a
  sampled pixel round-trips through `CopyPixels` (asserts channel order / no premultiplication surprise).
- **`IngredientDetailViewModel`** — hero + per-variant thumbnails + colorways bitmaps are non-null with
  expected dims; **custom** ingredient path returns the raw image (no colorize) while **dynamic/static**
  go through `Colorizer`; selecting a variant swaps the hero and disposes the old one.
- **`RecipeDetailViewModel`** — `Hero` is non-null and canvas-sized; `Reroll` changes `RollSeed` and
  produces a (re-)built hero, disposing the previous.
- **`IngredientEditorViewModel`** — built from a real `LoadedIngredient`: filmstrip has the real variants
  with thumbnails; changing Mode / Hue / Sat / Fixed re-renders the preview (a new bitmap, old disposed);
  a custom ingredient shows the raw image.
- **Disposal** — `Dispose()` frees held bitmaps and is idempotent; `ExplorerViewModel` selection swap
  disposes the outgoing detail; `NavigationService.Back()` disposes a popped `IDisposable` page.
- Colour-math correctness stays in `Nfty.Core.Tests`; App tests assert **wiring** (right seam chosen,
  right dims, disposal), not pixel colour values.

## 5. Open items / deferred (reserved)
- **Async render offload** — `GenerateAsync` / `Task.Run` for the composited hero if a full-size canvas
  ever stalls the UI thread; sync is adequate for a single 1-asset composite this slice.
- **Editor persistence** — writing colorize state + painted pixels back to a draft manifest (editor
  slice).
- **Set-browser** thumbnails over large cooked collections (needs the cache/streaming story the Phase-2a
  parent spec §11 reserved).

## 6. Out of scope
- Any `Nfty.Core` change.
- Editing behaviour (paint/undo/redo/save), Cook-to-disk, enlarge/fill as real separate windows.
- Visual-fidelity polish of any pane to the mockup — the imagery is real but the **look** is delivered
  by the dedicated visual-fidelity pass.

> **Program requirement (recorded, delivered later):** the *final* GUI must be a faithful **visual
> mirror of the locked mockups** (`docs/design/mockups/*.html`). This slice makes the imagery real; the
> dedicated visual-fidelity pass makes the composition pixel-faithful. Real-but-plain here is expected,
> not the finished look.
