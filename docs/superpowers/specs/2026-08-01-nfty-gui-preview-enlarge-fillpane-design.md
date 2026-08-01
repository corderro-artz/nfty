# nfty GUI — Editor preview: Enlarge / Fill pane (C1) design spec

**Date:** 2026-08-01
**Status:** Approved (design), pending implementation planning
**Scope:** Wire the Ingredient Editor's two remaining preview stubs — **Enlarge preview** and **Fill pane
with preview** — so the colorized preview can be inspected at a useful size. Both exist as buttons in
`docs/design/mockups/ingredient-editor.html`; both are currently `_notify` stubs.

## 0. Program bar
Rock-solid, efficient; best practices; escalate anything off. Pure view-state on the existing
ViewModel — no new window, no new service, no `Nfty.Core` change. Token brushes only.

## 1. Goals & non-goals
**Goals**
- **Enlarge preview** toggles the preview between its normal inset size and a large size, in place.
- **Fill pane with preview** toggles the preview taking over the **canvas pane** (the big central
  surface), so the colorized result can be judged at full size; toggling again restores the paint canvas.
- Both are **toggles** (press again to return) — the mockup's buttons carry no separate "restore"
  affordance, so each button must undo itself.
- While the preview fills the pane, the paint canvas is not shown and therefore **cannot be painted on**
  (its pointer handlers are attached to the canvas `Image`, which is hidden) — that is the intended
  read-only "look at it" mode.

**Non-goals (this slice)**
- A separate/floating preview **window** (the mockup shows an in-place blip, not a window). Moving the
  preview to a canvas-overlaid "blip" position — that is layout parity, deferred to the **E** polish
  pass. Zoom/pan of the preview. Any `Nfty.Core` change.

## 2. Components

### 2.1 Preview view-state (`IngredientEditorViewModel`)
- `[ObservableProperty] private bool _previewEnlarged;` — with `[NotifyPropertyChangedFor(nameof(PreviewHeight))]`.
- `[ObservableProperty] private bool _previewFillsPane;` — with `[NotifyPropertyChangedFor(nameof(ShowPaintCanvas))]`.
- `public double PreviewHeight => PreviewEnlarged ? 320 : 120;` — the rail preview's height.
- `public bool ShowPaintCanvas => !PreviewFillsPane;` — the canvas pane shows the paint canvas unless the
  preview has taken it over.
- Commands (replacing the two `_notify` stubs):
  ```csharp
  [RelayCommand] private void EnlargePreview() => PreviewEnlarged = !PreviewEnlarged;
  [RelayCommand] private void FillPanePreview() => PreviewFillsPane = !PreviewFillsPane;
  ```
- Nothing else changes: `Preview`/`Canvas` bitmaps, `RebuildSurfaces`, painting, Save are untouched — this
  is presentation state only.

### 2.2 View (`IngredientEditorView.axaml`)
- The rail preview `Border` binds `Height="{Binding PreviewHeight}"` (was a literal `120`).
- The canvas host shows **either** the paint canvas **or** the preview:
  - the existing canvas `Image` (`x:Name="CanvasImage"`) gets `IsVisible="{Binding ShowPaintCanvas}"`;
  - a sibling `Image` bound to `Preview` with `IsVisible="{Binding PreviewFillsPane}"` fills the same cell.
  Both live in the existing canvas-host `Border`, so the pane's framing is unchanged.
- The two buttons keep their existing command bindings; no new controls. Token styles; no raw hex.

## 3. Data flow
```
Enlarge   → PreviewEnlarged = !PreviewEnlarged   → PreviewHeight (120 ↔ 320) → rail preview resizes
Fill pane → PreviewFillsPane = !PreviewFillsPane → ShowPaintCanvas flips      → canvas pane shows the
                                                                                preview instead of the
                                                                                paint canvas (and back)
```

## 4. Error handling
None — pure toggles over already-rendered bitmaps. If `Preview` is null (zero-variant ingredient, where
`RebuildSurfaces` no-ops), the bound `Image` simply renders nothing; the toggles remain harmless.

## 5. Testing
- **VM:** `EnlargePreviewCommand` toggles `PreviewEnlarged` and moves `PreviewHeight` (120 ↔ 320) and
  back; `FillPanePreviewCommand` toggles `PreviewFillsPane` and flips `ShowPaintCanvas`; the two are
  independent (enlarging does not fill the pane, and vice versa); neither dirties the draft
  (`IsDirty` stays false) nor rebuilds/changes the `Canvas`/`Preview` bitmap instances.
- **Visual:** render the editor with the preview filling the pane (both themes) and confirm the pane
  shows the colorized preview rather than the grayscale canvas; render with Enlarge on and confirm the
  rail preview is visibly taller.
- **No regression:** the editor suites (paint, variant CRUD, save, loose) stay green; full suite green;
  build 0 warnings; no raw hex.
- **Manual smoke:** open an ingredient → **Enlarge** grows the rail preview and pressing it again
  restores; **Fill pane** replaces the paint canvas with the preview and pressing it again restores;
  painting still works after restoring.

## 6. Risks & escalation
- **Painting while filled:** the paint canvas is hidden, so pointer events don't reach it — intended.
  Confirm in the smoke that painting resumes correctly after toggling back (the pointer handlers are
  attached once in the view's constructor and are unaffected by visibility).
- **Layout parity is deferred:** the mockup places the preview as a small blip **over** the canvas with
  these buttons on it; the current Avalonia layout has the preview in the right rail. This slice matches
  the *behaviour*, not that placement — the **E** polish pass moves it. Do not attempt the blip layout
  here.
- **Magic sizes:** 120/320 are literal heights matching the current rail. If E introduces size tokens,
  these should move with it; keep them in one place (`PreviewHeight`) so that is a one-line change.
