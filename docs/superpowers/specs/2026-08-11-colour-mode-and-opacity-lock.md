# Colour painting mode, palettes, and the opacity lock — design

Two features, plus a storage decision that turns out to contradict something already shipped.

1. **Colour mode** — a second painting mode producing full-colour art, saveable only as a Custom
   ingredient, with a palette that swaps between a grey ramp and a rainbow ramp.
2. **The opacity lock** — partial alpha becomes possible, and is refused by default.

## What is already true

Four facts from the code that shape everything below.

**Custom layers already bypass the paint stack.** The editor holds them as `Image<Rgba32>` in
`_importedCustom`, and `CanPaint => !IsCustom` disables every tool. So colour mode is not a new
concept bolted onto the editor — it is *making that dictionary paintable*. That seam already exists;
this feature fills it in.

**The paint stack is one payload away from being colour-ready.** All five commands — `BrushStroke`,
`EraseStroke`, `FloodFill`, `DrawShape`, `MoveSelection` — are 21 to 60 lines each, and every one
reduces to *geometry* plus `(x, y, byte value, byte alpha)`. `RegionEditCommand.StampDiscs` already
separates the disc geometry from the per-pixel paint decision.

**`ValueMap`'s grayscale guarantee is deliberate**, and its own summary says so: *"Grayscale is
guaranteed by construction — there is no way to store independent R/G/B."* Nothing here weakens it.

**Alpha is already binary in practice.** Brush writes 255, erase writes 0, fill copies the seed's.
There is no opacity control anywhere. So "allow transparency" is genuinely *new capability*, not the
removal of a restriction.

## Decision: the paint stack goes generic over its pixel

`ValueMap` keeps its API and its guarantee. `ColorMap` joins it. The machinery they share — region
snapshot/undo, disc stamping, shape rasterising, flood-fill region detection — is written once,
generic over the pixel type.

```
Editing/IEditSurface.cs      IEditSurface<TPixel> : Width/Height/InBounds/Get/Set
Editing/GrayPixel.cs         readonly record struct GrayPixel(byte Value, byte Alpha)
Editing/ValueMap.cs          + : IEditSurface<GrayPixel>   (existing API unchanged)
Editing/ColorMap.cs          : IEditSurface<Rgba32>
Editing/RegionEditCommand.cs → RegionEditCommand<TPixel>
Editing/IEditCommand.cs      → IEditCommand<TPixel>
Editing/EditHistory.cs       → EditHistory<TPixel>
```

The alternative — one RGBA raster with grayscale enforced only at the edges — was rejected. It is the
least code by a distance, and it deletes the one property that makes a whole class of bug
unrepresentable. `GrayPixel` cannot hold independent R/G/B, so a value-map still cannot be made
non-grey by any code path, including a future one nobody has written yet.

Generics rather than a parallel command set because the duplicated part would be the **undo
snapshot** — the piece where a divergence between two copies is silent and produces corrupted
history rather than a compile error.

## Colour mode, end to end

**Entering it.** The mode follows the ingredient's kind by default: Custom opens in colour, Dynamic
and Static open in grayscale. Switching is allowed, and switching a Dynamic/Static ingredient to
colour mode changes what Save does:

> **Save creates a NEW Custom ingredient by default**, leaving the original grayscale ingredient
> exactly as it was on disk. A checkbox in the confirm dialog switches to **overwrite**, which
> converts the original in place and discards its colorization block.

Non-destructive by default because the colorization block is not recoverable — hue and saturation
ranges, entry weights and the DNA quantize steps all go, and with them the layer's entire colour
space. The overwrite path states that in the dialog rather than in a tooltip.

**Saving.** Colour mode can only ever produce `LayerKind.Custom`. There is no path from a `ColorMap`
to a Dynamic or Static ingredient, and `Validator.CheckKind` already refuses a Custom layer that
carries a colorization, so the two ends agree without a new rule.

**The palette.** Ten slots plus user-saved swatches.

- Grayscale mode: ten ascending greys, plus a custom grey picker (one axis, 0–255).
- Colour mode: the same ten slots become a rainbow ramp, plus a full colour picker.
- Saved swatches append to the palette and persist (see storage, below).

"Swap the greys for rainbow" changes **what the palette offers**, never the artwork. Recolouring an
existing grayscale drawing through a rainbow ramp is a different feature and is out of scope.

**Palette colours are artwork data, not theme tokens.** The house rule that no raw hex lives outside
`Themes/Tokens.axaml` governs *chrome*. A swatch the user mixed is user data and belongs in settings.
Stated here so a later audit does not "fix" it into a theme dictionary.

## The opacity lock

**Locked is the default.** Locked means **binary alpha**: every pixel is either fully opaque or fully
erased. It does not mean "no alpha" — erasing is how a sprite gets its shape, and layers have to
composite over one another, so a fully opaque rectangle per layer would make stacking meaningless.

Unlocking enables partial alpha and warns, once, that semi-transparent pixels do not voxelise
cleanly — the daughter application that turns these assets into models cannot resolve a half-present
voxel.

Enforcement is at the point of painting: with the lock on, every command writes alpha 255 or 0 and
nothing between. It is an **editor setting, not a manifest field** — no format change, `Schema.Current`
stays 1, and no existing archive becomes invalid. `Avalonia`'s `ColorView.IsAlphaEnabled = false`
"fixes alpha to its maximum value", which is exactly the locked behaviour, so the picker follows the
lock rather than needing its own rule.

## Storage — and a contradiction already in the tree

**The requirement:** nothing this app writes should leave the app's own folder. It is downloaded and
run, not installed, so a per-user profile directory is the wrong home for its state.

**`RecentsService` already violates that.** It writes `%APPDATA%/nfty/recents.json` today. That is
shipped behaviour and predates this feature; it is called out here because the same decision governs
both, and fixing one without the other leaves the rule half-true.

**How the app decides where it is** — there is no single "root", and this is worth stating plainly
because three separate ideas get conflated:

| Idea | How it is determined | Scope |
|---|---|---|
| **Kitchen** | `Kitchen.Open(path)` takes a `.ktn`; the workspace is that file's **containing folder**. Membership is a non-recursive scan of that one folder. `Kitchen.TryFindIn(dir)` reports Found / NotAWorkspace / Ambiguous — two `.ktn` files is a state only the user can resolve, so it is never guessed | A folder the user points at |
| **Open CookBook** | An independent file path (`ICookBookSession.SourcePath`). A `.cbk` can be opened from anywhere, with or without a Kitchen | One file |
| **App state** | `%APPDATA%/nfty/` — recents only | Per-user, machine-wide |

So the Kitchen is **a folder you point at, not a root the app owns**, and the app currently has no
notion of "its own folder" at all.

**App-internal state lives in a `.nfty/` folder.** The dot prefix sorts it to the top and signals
"this is not for you" — the same convention `.git` and `.vscode` use. Recents moves there too, and its
existing `%APPDATA%` list is read once and migrated so nobody loses their Landing screen.

**Where that folder is, is DISCOVERED — never recorded.** This is the rule the Kitchen already
follows, for the same reason: a recorded pointer goes stale the moment anything moves, and then the
app is lying about itself. Resolution order, first hit wins:

1. `.nfty/` **beside the executable** — the normal case for a downloaded, unzipped app.
2. `.nfty/` in the **current working directory**.
3. `.nfty/` in the **open Kitchen's folder**, once a Kitchen is open.
4. **In memory**, and say so.

An existing `.nfty/` is honoured even where a new one could not be created, so the order works for
both "where should I write?" and "where did I write last time?" without a pointer file.

**The unwritable case is not an error, it is a state with an exit.** If nothing above is writable the
palette lives in memory for the session and the panel says so plainly. The user can **select any
folder they can write to** at any point — an ordinary folder picker, no requirement to create
anything first — and the app makes `.nfty/` inside it, moves the session's swatches in, and finds it
again next launch through rule 2 or 3. Choosing a folder that is also unwritable is refused at the
point of choosing, with the reason, rather than accepted and silently lost later. That is what lets
the app run from a read-only location, a USB stick, or `Program Files`, and still grow its own
storage wherever the user has somewhere to put it.

A palette is convenience state: a corrupt store loads empty, a failed save is swallowed, and neither
ever blocks or crashes the editor — exactly the discipline `RecentsService` already applies.

**Two palette scopes**, resolved by precedence:

- **App palette** — in `.nfty/`, shared across every CookBook.
- **CookBook palette** — an additive optional field on the CookBook manifest, so a collection carries
  its own colours and survives being handed to someone else. Optional means **no schema bump**:
  `System.Text.Json` ignores unknown properties, so older builds still read the archive, whereas
  bumping would make them reject it.
- With a book open its swatches show as the book's, the app palette beneath. With no book open, the
  app palette is all there is.

## What shipped (GUI)

Variant **A** of `docs/design/mockups/explorations/palette-panel-variants.html`: a permanent 40px
strip under the toolstrip. Chosen for proximity — the colour sits beside the brush that lays it down
— over B (palette in the colorize rail, 300px away and able to scroll out of view while painting)
and C (a popover, zero chrome but a two-click round trip per colour change).

**The paint colour is HSV over three axes the screen already had.** The toolstrip's value ramp is
**V** — it means "how bright" in both modes, so no control changes what it does under the author —
and the colorize rail's hue and saturation tracks become **H** and **S**. Those tracks are the
Dynamic layer's *range* controls; a Custom layer has no range to set, so rather than adding a third
pair of sliders the same two components take the rail's free space and set one colour. While colour
mode is on the range controls are hidden: leaving them up put two hue sliders on one rail meaning
different things, which is worse than a control briefly out of sight.

**The draft carries both rasters.** `VariantDraft` gained `Color`, and which one exports is decided
by `IngredientDraft.Kind` — never by "whichever is non-null". A Dynamic layer painted in colour holds
an untouched `ValueMap` *and* a `ColorMap`; that is exactly what lets the default save write a new
Custom ingredient beside the original and leave the original byte-identical. Entering colour mode
widens **every** variant, because a save writes the whole ingredient and a variant the author never
visited would otherwise reach the exporter with nothing to write.

`IsCustom` reads the **draft**, not the loaded manifest. The two agree on open and diverge exactly
once — when a colour save converts the draft — and from then on the draft is the truth, or Save
re-prompts for the conversion every time and grayscale stays on offer for a value-map no archive
sees.

This deleted the old `_importedCustom` dictionary, `EffectiveCustomImage`, the per-variant Save gate
and the disposal bookkeeping around all three: a Custom variant's pixels are now just its `ColorMap`,
which `DuplicateVariant` clones and `RemoveVariant` drops for free.

## Three failures only a rendered frame found

Each was silent — no exception, no failing test, nothing in the markup that looked wrong.

1. **The saved swatches did not render at all.** The strip's fixed cells came to 612px inside a
   594px pane at the minimum window width, so the one star-sized column — theirs — was arranged at
   **zero width**. The ViewModel's collection was correct throughout. *Width is a budget*, and
   `PaletteStripLayoutTests` is what keeps it one. Note a clipped control still reports its own
   non-zero `Bounds`, so "is it laid out?" is the wrong question; containment is.
2. **`Colour` was clipped to `Colou`** in the mode tray, at a cell width that looked ample in the
   markup.
3. **Every image in the app was being smoothed.** An 8×8 variant at 320px was a blur. Fixed with
   `RenderOptions.BitmapInterpolationMode="None"` on each view root — it cannot be a Style setter, as
   Avalonia 12's `RenderOptions` is a Get/Set pair rather than an `AvaloniaProperty` — and enforced by
   `PixelPerfectRenderingTests`, which derives its view list from the markup and reads the *effective*
   mode off live controls (the value is composited at draw time, so a child reads `Unspecified`).

A fourth was found by an existing test rather than a frame: the first cut of the strip **disabled**
the `Grey` button on a Custom layer, and `DarkModeContrastTests` scored it at 1.41:1. The app dims
disabled controls to 0.38 opacity, which is fine over a full-strength foreground and unreadable over
an already-muted one. Unreadable is worse than absent, so the cell swaps its ink instead: a tray with
a choice, or a caption with none, at a fixed 128px either way.

## Also on the record

- **A `$parent[…]` lookup from inside a `ContextMenu` cannot resolve** — a ContextMenu is not in the
  visual tree — and it does not throw: the whole item template comes up empty. The saved swatches
  carry their own `ForgetCommand` for this reason (exempted in `WiringCoverageTests` with the why).
- **Pixel-perfect is a stated product rule**, not just a display default: nothing automatically
  smooths, blurs, anti-aliases or cleans up an author's pixels. Partial alpha is unrelated — it is a
  value the author opts into, not a filter applied to their art.

## Deliberately out of scope

- **Recolouring existing artwork** through a rainbow ramp. A palette that offers colours and a tool
  that rewrites pixels are different features; the second needs a mapping curve, a preview and its
  own undo entry.
- **Per-variant opacity settings.** The lock is an editor mode, not a property of a variant.
- **A colour picker in the CLI.** Colour specs already carry explicit prefixes there.

## What shipped (CLI)

`inspect` gained two things the archive already held but nothing on the command line ever showed:

- **The book's palette**, printed as the prefixed specs it is stored as — the same form an author
  types, so what is shown and what is stored cannot drift.
- **`--voxel`**, the readiness report: which variants carry *partial* alpha, how much of each image,
  and what to do about it. Opt-in, because partial alpha is legal (it is a report, not a validation)
  and because it costs a full scan of every variant image. Refused on a `.ktn`: a Kitchen lists paths
  without opening them, and an option that silently does nothing is worse than one that says no.

`Validator` also gained the rule this feature kept running into: **two layers in one recipe may not
share a display name.** A layer's name is the `trait_type` it is published under, so duplicates merge
into one trait and one rarity bucket — percentages above 100. The colour save's "Aura (colour)"
naming exists to satisfy exactly this, and the reserved-name check for `"Type"` was its special case
all along.

## Testing

House rules throughout: in-memory `Loaded*` fixtures, exact-pixel assertions on tiny synthetic
images, round-trips for anything archived, no golden files, no real `%APPDATA%`, rendered frames in
both themes for anything visual.

The ones that carry the design:

- **A value-map cannot become non-grey**, through any command, in either mode. The generic refactor's
  whole justification.
- **Colour mode saves Custom, never anything else** — and the default save leaves the original
  Dynamic ingredient byte-identical on disk, while the overwrite path converts it.
- **The lock admits only 0 and 255**, across every command including flood fill, whose alpha comes
  from the seed pixel rather than from the brush.
- **Unlocking warns once**, not on every stroke.
- **`Schema.Current` is still 1**, and `tests/fixtures/VaporPets.cbk` still reads.
- **Palette storage degrades**: unwritable location loads empty, saves silently, never throws.
