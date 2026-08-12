# Layer depth and stack preview — design

Two features, in this order, because the second cannot be built without the first:

1. **Depth** — make the recipe's paint order visible and editable, from Core through the CLI to the GUI.
2. **Stack preview** — while authoring one Ingredient, composite it against 0..N other layers at their
   real depths, so art can be lined up before it is drawn.

## What the request assumed, and what is actually true

The task was raised as "there is no layer position enforced, so there's no guarantee your body
ingredient draws before a shirt". That premise is **false**, and recording why matters because the
correction is what shapes the design.

`RecipeManifest.LayerOrder` is an `IReadOnlyList<string>` of ingredient ids, documented and
implemented as **bottom-to-top**. `Generator.PlanLayers` resolves layers by walking it;
`Compositor.Composite` draws that sequence in order; `Validator.CheckLayerOrder` rejects an empty
order, an unknown id, a **repeated** id, and any ingredient absent from it. That last set of rules
makes the order a strict **bijection** over the recipe's ingredients.

So body-before-shirt is already guaranteed, and — the point the request was really reaching for —
**two layers cannot occupy the same depth, because a list has one item per position.**

What was genuinely missing:

| Gap | Evidence |
|---|---|
| The GUI can never reorder | `RecipeDetailViewModel` renders `LayerRow(i + 1, …)` read-only; `CookBookEdits.UpsertIngredient` always appends to the end |
| The CLI can only choose a position at insert time | `add ingredient --index`; there is no move command |
| Depth is never a number the author sees or types | It is an implicit ordinal |

Those three are what this design fixes. The enforcement was never the problem.

## Decision: depth is a projection, not a stored field

**`layerOrder` stays authoritative. Depth is its 1-based position, dense, 1..N.**

An integer `z` written into the manifest was considered and rejected. It would *introduce* the
collision the request wanted prevented — `"z": 20` twice is expressible, and would then need a
Validator rule, a tie-break for books already on disk, and an absent-default on every read path for
every `schemaVersion: 1` archive in existence. A list makes the illegal state unrepresentable
instead of merely invalid, which is the stronger guarantee and the cheaper one.

Three consequences worth stating plainly:

- **No manifest changes. `Schema.Current` does not move.** `tests/fixtures/VaporPets.cbk` and every
  archive ever written are untouched by this work, by construction rather than by care.
- **Depth cannot disagree with paint order**, because it is the same field read two ways.
- **Inserting a layer renumbers the ones above it.** Accepted: the `#` column in `explorer.html` and
  `LayerRow.Index` are already dense 1..N, so this is the numbering the locked design already shows.

## Depth does not enter DNA — and must not

`Dna.Compute` sorts its selections with `OrderBy(x => x.IngredientId, StringComparer.Ordinal)`
before hashing. **A given selection therefore hashes identically no matter what order its layers are
in.** That is the property the compatibility argument needs: reordering cannot invalidate an
already-minted Set, and putting depth into the hash would invalidate every Set ever minted. Depth
stays out.

> **Correction.** An earlier draft of this spec claimed reordering yields "the same DNA over
> different pixels". **That is false**, and the stronger truth matters. `Generator.RollOne` walks the
> layers in `layerOrder` and consumes one `WeightedRoller.Roll` per layer, so moving a layer moves
> *which RNG draw reaches it*. On any layer with more than one variant, the rolled selection itself
> changes. Same seed + reordered book therefore produces **different assets — different pixels and
> different identities.** Verified empirically on a two-layer, two-variants-each book before this was
> written down.

So a reorder is not a cosmetic re-render; it is a different collection. The only thing recording that
is `set.json`'s `cookbookSha256`, and `extend` onto a Set cooked before a reorder silently mixes two
generations.

`extend` already carries the source hash on both sides — `SetManifest.CookbookSha256` is written at
cook time and `LoadedCookBook.SourceSha256` is populated on read, so this needs no new plumbing. It
gains a warning when the two differ: a warning, not a refusal, since re-cooking a deliberately edited
book is a legitimate thing to want.

## Shape — depth

```
Core/Editing/LayerDepth.cs      DepthOf(manifest, id) · At(manifest, depth) · MoveTo · MoveBy · Ordered
Core/Editing/CookBookEdits.cs   + MoveLayer(book, recipeId, ingredientId, toDepth)
```

`LayerDepth` is pure: every operation takes a `RecipeManifest` and returns a new one via `with`.
It never touches images, so it is trivially testable and reusable by both front-ends. Out-of-range
depths clamp to `1..N` rather than throwing — a move-up on the top layer is a no-op the UI should not
have to guard.

`Validator` gains **nothing**. The bijection rules it already enforces are exactly the depth
invariant; a second rule restating them would be redundant and would drift.

## Shape — stack preview

```
Core/Imaging/StackPreview.cs    PreviewLayer(Ingredient, VariantId, ColorSpec?)
                                StackSplit(Below, Above)
                                Render(canvas, bottomToTop) → Image<Rgba32>
                                PickVariant(ingredient) → string
                                Split(recipe, editedId, enabledIds) → StackSplit
```

`Split` is in Core, not in each front-end. An earlier draft of this block omitted it while the GUI
section three paragraphs down called the above/below split "the entire point" — taken literally that
would have left the CLI's `--only` and the GUI panel deriving the same split two different ways,
which is the exact failure this file keeps legislating against. It returns **ids**, not
`(depth, id)` pairs: the caller already maps ids to ingredients to build `PreviewLayer`s, and two
bottom-to-top lists feed `Render` directly — which is what the caching below wants.

An unknown **edited** id throws (there is no depth to split around). An unknown **enabled** id is
ignored — that is the Kitchen case, where a loose layer has no depth in this recipe, and it also
keeps a stale checkbox from throwing mid-repaint.

`Render` colorizes each layer through **`VariantPreview.Render`** and stacks them through
**`Compositor.Composite`** — the two rules that already exist. It introduces no third rendering
path, which is the property CLAUDE.md protects: the CLI's `preview` and the GUI's preview must be
the same image, not a similar one.

`PickVariant` is the deterministic default when a caller does not name one: **highest weight, ties
broken by ordinal-first id.** No RNG, so a reference layer does not change appearance between
repaints.

**A `PreviewLayer` carries its rolled colour as well as its spec, and draws from the rolled one.**
A colour spec is what a person reads, pastes and re-runs — but every spec resolves through 8-bit RGB,
so a *rolled* hue that fell between two representable colours cannot be spelled exactly. Carrying
only the string put the preview about a quarter-degree of hue off the asset it claims to show. That
is invisible, which is exactly how it would have survived: mutation-probing the fix, only one of four
sampled draws moves a single 8-bit channel — a spot check would almost always have agreed. Static
carries no rolled value (its spec is author-written, and generation resolves the same string through
the same parser, so it is already exact) and Custom has no colour at all.

A layer whose image does not match the canvas is rejected with a message naming both sizes. Never
scaled — a preview that silently resizes is lying about alignment, which is the one thing this
feature exists to show.

## Shape — CLI

The existing single-ingredient form is untouched, so every current invocation and test keeps working.
Two additions:

```bash
nfty preview cat.rcp --seed alpha                        # the whole stack, one deterministic roll
nfty preview cat.rcp --seed alpha --only body,shades     # just those layers, at their real depths
nfty preview cat.rcp --seed alpha --with ~/kitchen/hat.igt   # plus a loose layer, on top
nfty move ingredient cat.rcp --id body --to 1            # reorder; also --up / --down
```

Rejected: a `--with path:variant:color` micro-syntax. Colour specs contain `:` by mandate
(`hex:d6249f`), so the separator would have to be `|` or `@` and every example would need shell
quoting. Seeding the roll instead is both simpler and more honest — it is what generation does.

## Shape — GUI

**Reorder** lands in the Recipe detail's existing Layers table, which already has the `#` column.
Move up/down per row, gated behind `ExplorerViewModel.IsEditing` — the same unlock that gates every
other edit, and the behaviour `explorer.html` already describes ("Unlock, top-right, to edit").
Persisted through `CookBookPersistence.PersistAsync`, as saves already are.

**Stack preview** lands in the Ingredient Editor as a reference-layer panel. Rules:

- The ingredient being authored is **pinned at its own depth** and cannot be removed from the stack.
  Layers below it composite under; layers above composite over. That split is the entire point —
  sunglasses sit above a face and below a hat, and a flat underlay would show neither correctly.
- Two **visually distinct** groups: **in this recipe** (real siblings, real depths) and **from the
  Kitchen** (loose `.igt` files discovered by `Kitchen.Open`). The user must never have to guess
  which a layer is; a Kitchen layer is scratch, a sibling is the actual composition.
- A Kitchen layer has no canonical depth, so the user places it **within the preview only**. Nothing
  is written to the `.igt`. A transient composition gets transient state — inventing a stored depth
  for a loose file would be state that immediately goes stale.
- Zero references on is the default and renders exactly today's single-layer preview.

### Geometry is fixed. Nothing reflows, ever.

**A control appearing or disappearing must not move, resize or reflow anything around it.** A layer
table is the same width locked and unlocked; a reference row is the same height active and inactive;
no component stretches or repositions at any window size or resolution.

This is a hard rule, not a preference, and it is the one most likely to be violated by accident —
both chosen variants violate it *as drawn*:

- The reorder table's grip column is **prepended on unlock**, shifting every other column 26px right.
  Fix: the column is always present and always the same width; only the glyph's **opacity** changes.
  Never `display`/`IsVisible` on anything that occupies layout.
- The reference panel's rows **grow** when a layer becomes active, because the `over`/`under` tag and
  the placement stepper appear. Fix: reserve their space in every row from the start, occupied or
  not.

The general form: **reserve the space, toggle the ink.** Anything that can appear must already own
its box while absent. In Avalonia that means `Opacity`/`IsHitTestVisible` rather than `IsVisible`,
and fixed column definitions rather than `Auto` where the content can vanish.

### The four design decisions

| Decision | Chosen | Consequence for implementation |
|---|---|---|
| Reorder control | **Drag handle** (variant B) | Needs `DragDrop.DoDragDropAsync` + a drop-line adorner. Grip column always present. |
| Reference panel | **Two labelled sections** (variant B) | "In this recipe" / "From the Kitchen", depth in the gutter, placement stepper for Kitchen files. Row geometry fixed. |
| Reading direction | **Keep the locked order, label it** | Table stays `#1` at top = paints first = furthest back, as `explorer.html` has it. A persistent `1 → paints first, furthest back` hint carries the meaning instead of an arrow implying it. |
| Layers above the edited one | **Ghosted by default, toggle to full** | Above-stack composites at reduced opacity so the art being painted is never hidden; a toggle shows the true composite. |

**Drag alone is not accessible, so it is not enough.** Variant B's own cost table records
`Keyboard: none`. `LayerDepth.MoveBy` already exists and the CLI already exposes `--up`/`--down`, so
the GUI binds keyboard reorder to the selected row (`Alt+Up`/`Alt+Down`) alongside the drag. Shipping
a reorder reachable only by pointer would be an incomplete feature, not a scoped-down one.

> **Correction, from building it.** Two claims in the "Deliberately out of scope" note below did not
> survive contact with the code.
>
> 1. **`DragDrop.DoDragDropAsync` is the wrong mechanism here, not merely one option.** It needs a
>    platform drag *source*, and `Avalonia.Headless` registers none — its only drag support is
>    `HeadlessWindowExtensions.DragDrop`, which injects an incoming raw drag event at a *target*. A
>    probe calling `DoDragDropAsync` under the harness never returned. Since this project verifies
>    every visual from a rendered frame produced by that harness, using it would mean the drop line
>    could never appear in a capture and the gesture could carry no test at all. The shipped gesture
>    is **pointer capture** (`Pointer.Capture` + `PointerMoved`/`PointerReleased`) — which is also
>    what the exploration's own script does, works identically under touch, and needs no payload for
>    a reorder that never leaves the control it started in. The drag now has a captured frame in both
>    themes and an end-to-end test driven by simulated pointer input.
> 2. **Moving a row does not preserve its container.** An `ObservableCollection.Move` makes Avalonia's
>    `ItemsControl` drop and recreate the moved container, and the new one's child is not realised
>    until the next layout pass — so focus is lost and there is nothing to focus yet. The keyboard
>    path therefore restores focus to the moved row's grip on a `DispatcherPriority.Loaded` post,
>    behind the layout pass. Without that, `Alt+Up` works exactly once and the second keystroke is
>    silently swallowed. Re-seating the same row *objects* is still right, but for a different
>    reason than assumed: they are the selection's identity, not the container's.

### Visual design: reuse first, then three variants to choose from

Neither surface has a mockup — `ingredient-editor.html` has no reference-layer affordance, and
`explorer.html`'s layer table has a `#` column and no reorder control. The locked mockups are the 1:1
reference and are **never** edited to match the app, so net-new UI needs its own decision.

The rule for this work: **reuse existing components and existing hex wherever the shape allows.** A
reorder control is an icon button in a table row; a reference-layer list is a checkable variant of the
filmstrip. Neither needs a new colour, and no new hex literal enters `Tokens.axaml` unless a variant
is chosen that genuinely requires one.

Where something new is unavoidable, **three variants of it are built and rendered for the user to
pick between** — not one proposal defended after the fact. Variants live in
`docs/design/mockups/explorations/` and are explicitly *not* locked. Each is rendered in both themes
before the choice is made, because a layout that reads in one and not the other is not a real option.

**Chosen: variant B in both files** — the drag handle for reorder, the two labelled sections for the
reference panel. The explorations are **kept, not deleted.** An earlier draft of this section said the
losing two would be thrown away; that was wrong. Their cost tables and "watch out" notes are the
record of *why* B won and what it costs, and two of the four defects this feature shipped against —
the inverted reading direction and the above-layer that hides your own art — were found by drawing
the alternatives. Deleting them would delete the reasoning and leave only the conclusion.

Neither locked mockup was edited. One knowing consequence: the always-present grip column gives the
locked Layers table a 26px gutter that `explorer.html` does not draw (measured — in the mockup the
`#` column sits flush at the table's left edge). That is the price of "reserve the space, toggle the
ink" on a surface the locked set has no affordance for at all, and it was accepted deliberately
rather than discovered later.

### Performance

`RebuildSurfaces()` runs on every brush stroke and every slider tick. Compositing N canvas-sized
layers there would stutter at 1000×1000, so the below-stack and above-stack are **pre-composited
once into two cached images**, invalidated only when the reference *selection* changes. A stroke then
costs two `DrawImage` calls regardless of how many references are on.

## Testing

Per house rules — in-memory `Loaded*` fixtures from tiny solid-fill images, exact-pixel assertions
for anything composited, round-trips for anything archived, no golden files, no real `%APPDATA%`.

The non-obvious ones, which are the ones worth writing:

- **Reorder cannot change an identity that was already decided.** Assert it on a construction where
  the reordered layers each have one variant, so the selection cannot move — then the DNA must match
  **asset-for-asset**, not merely as a set. A whole-space generation proves nothing here: the space
  is order-independent, so set-equality is trivially true either way.
- **Reorder DOES reassign the rolls** on layers with several variants, per the correction above. Pin
  that boundary explicitly, so nobody re-derives the false version of the claim later.
- **Report text is byte-identical** for a book that was not reordered. `CollectionReport` and
  `IdentityReport` both walk `LayerOrder` and are documented as format-invariant.
- **The fixture still reads.** `VaporPets.cbk` must round-trip unchanged; it is the proof that no
  format change leaked in.
- **Above/below actually splits.** A 2×2 with an opaque reference above the edited layer must hide
  it; the same reference below must not.
- **`Schema.Current` is still 1.** A guard, because the whole compatibility argument rests on it.

GUI phases additionally verify from **rendered frames** via `NFTY_CAPTURE`, in both themes, plus
`DarkModeContrastTests` and `ThemeResourceTests` — never from the markup.

## Deliberately out of scope

- **Cross-recipe depth.** Depth is a property of a composition, so the same `.igt` may sit at
  different depths in different recipes. That is correct, not a gap.
- **A stored depth on `IngredientManifest`.** Nothing needs it once Kitchen layers are placed in the
  preview session, and it would be the first field that can go stale against the recipe that owns it.
- ~~**Drag-to-reorder.**~~ **Now in scope** — chosen as the reorder control. Verified available:
  Avalonia 12 ships `DragDrop.AllowDrop` and `DragDrop.DoDragDropAsync` with the `DataTransfer`
  payload type (not the older `DataObject`), no extra package. Within-list reorder additionally needs
  pointer-position hit-testing and a drop-line adorner, which the framework's between-lists example
  does not cover. It ships **with** keyboard reorder, never instead of it.
- **Previewing against a whole other CookBook.** Siblings and the open Kitchen cover the authoring
  case; reaching into arbitrary `.cbk` archives is a file-browser feature wearing a preview costume.
