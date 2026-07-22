# nfty — Avalonia GUI completion design spec

**Date:** 2026-07-22
**Status:** Approved (design), pending implementation planning
**Scope:** The Avalonia desktop GUI that realizes the five locked mockup specs
(`explorer`, `landing`, `help`, `ingredient-editor`, `creation-flows`) on top of `Nfty.Core`.
**Governs:** all GUI implementation phases. Only **Phase 1** (the fully-wired shell) is planned in
detail next; Phases 2…N are enumerated here so Phase 1's wiring anticipates them.

## 1. Goals & non-goals

**Goals**
- Build the Avalonia app the mockup specs describe, backed by `Nfty.Core` (engine + `Nfty.Core.Editing`).
- **Wire everything before implementing behavior.** Phase 1 delivers a fully clickable, navigable,
  themed app in which **every** button, link, tree node, breadcrumb segment, sort header, toggle, and
  keyboard shortcut across all six screens is bound to a command or navigation on a ViewModel — no dead
  control. Actions that need `Nfty.Core` are wired to a visible stub; Phases 2…N replace stubs with real
  Core calls behind the seams Phase 1 establishes.
- Keep `Nfty.Core` UI-free; keep the shared UI head-agnostic so mobile/WASM heads drop in later.

**Non-goals (this spec)**
- Building the deferred features the mockups already reserve: the **Kitchen** workspace, the **Set
  browser** view, the editor's **custom full-colour mode**, and the **⌘K command palette** (§11).
- Mobile/WASM heads and touch/responsive layouts (structure is ready; layouts are later).
- Any redesign of the locked mockups. Two small mockup corrections are noted in §11 but are not GUI work.

## 2. Solution & project structure

New projects added to `nfty.sln`. `Nfty.Core` and `Nfty.Cli` are unchanged.

| Project | Kind | Responsibility |
|---------|------|----------------|
| **`Nfty.App`** | Avalonia class library (net10) | All UI: `Views/` (`.axaml`), `ViewModels/`, `Services/`, `Controls/`, `Themes/`, `Assets/`. References `Nfty.Core`. **Head-agnostic** — no desktop-only APIs. |
| **`Nfty.Desktop`** | Avalonia desktop app (net10) | The desktop head: `Program.cs` (`BuildAvaloniaApp`), `App.axaml`, the classic-desktop lifetime, the frameless `MainWindow`. References `Nfty.App`. This is what runs. |
| **`Nfty.App.Tests`** | xUnit | ViewModel/wiring tests + a few headless-Avalonia smoke tests. References `Nfty.App`. |

**Deferred heads** (`Nfty.Android`, `Nfty.iOS`, `Nfty.Browser`) are **not created now**. `Nfty.App`
stays free of head-specific APIs so they are thin additions later (Core + ImageSharp are fully managed).

`Nfty.App` internal layout (by concern, one responsibility per folder):
- `ViewModels/` — `ShellViewModel`, one VM per screen (`LandingViewModel`, `ExplorerViewModel`,
  `IngredientEditorViewModel`), one per dialog (`HelpViewModel`, `NewCookBookViewModel`,
  `NewRecipeViewModel`, `NewIngredientViewModel`), plus detail sub-VMs for the Explorer
  (`CookBookDetailViewModel`, `RecipeDetailViewModel`, `IngredientDetailViewModel`) and a `ViewModelBase`.
- `Views/` — the matching `.axaml` + code-behind (thin; no logic).
- `Services/` — `INavigationService`, `IDialogService`, `IFilePickerService`, `IRecentsService`,
  `INotYetWired` (the stub notifier), `IThemeService`.
- `Controls/` — reusable chrome and idioms: `TitleBar`, `StatusBar`, `KindMarker`, `Breadcrumb`,
  `ZoomControl`, `ValueMapCanvas` (Phase-2 paint host; a placeholder control in Phase 1),
  `ProceduralPetHost` (Phase-1 placeholder for value-map colorization).
- `Themes/` — `Tokens.axaml` (the single source of truth for colours/radii/typography), `Styles.axaml`
  (the mockups' component idioms as `ControlTheme`/`Style`), light/dark via `ThemeVariant`.

## 3. App architecture

- **MVVM:** CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`) over a `ViewModelBase`.
- **DI:** `Microsoft.Extensions.DependencyInjection`. `App` builds the service provider, registers services
  and VMs, and resolves `ShellViewModel` for the `MainWindow`.
- **Shell:** one frameless `MainWindow` bound to `ShellViewModel`. `ShellViewModel` holds:
  - `CurrentPage` — the active screen VM (`LandingViewModel` | `ExplorerViewModel` |
    `IngredientEditorViewModel`), rendered by a **`ViewLocator`** (`IDataTemplate` mapping a VM type to its
    View). Navigation = assigning `CurrentPage`.
  - `ActiveDialog` — the current modal overlay VM (`HelpViewModel` | a wizard VM) or null, rendered in an
    overlay layer above `CurrentPage` with a scrim. `Esc`/scrim-click clears it.
  - Shared chrome state the shell owns directly: window state (min/max/close), `Zoom` (50–300%), and the
    `?` help trigger.
- **Chrome contribution model.** The shell renders the window frame, window buttons, the `.kroot` chip
  slot, and the status-bar right cluster (zoom + `?`). Everything context-aware — the toolbar, the
  breadcrumb, and the status-bar left content — is **contributed by the current page VM** through
  bindable properties on a small `IChromeHost` surface (e.g. `Breadcrumb`, `Toolbar`, `StatusLeft`), so
  the Landing (no toolbar) and the Explorer (full toolbar) differ without a monolithic chrome.
- **Navigation & dialogs are services**, injected and fakeable in tests:
  - `INavigationService.To(pageVm)` swaps `CurrentPage`; `.Back()` where a back stack applies (editor →
    explorer).
  - `IDialogService.ShowAsync(dialogVm) : Task<TResult>` sets `ActiveDialog`, awaits its close, returns
    its result (wizard → a create request; help → void).

## 4. Screen inventory

| Screen | VM | Presentation | Chrome |
|--------|----|--------------|--------|
| **Landing** | `LandingViewModel` | `CurrentPage` (nothing open) | titlebar (no `.kroot`, no toolbar) + statusbar |
| **Explorer** | `ExplorerViewModel` (+ 3 detail sub-VMs) | `CurrentPage` (cookbook open) | full: titlebar + `.kroot` + breadcrumb + toolbar + statusbar |
| **Ingredient Editor** | `IngredientEditorViewModel` | `CurrentPage` (always edit) | titlebar + `.kroot` + breadcrumb (`editing value-map`) + statusbar; no toolbar, no lock |
| **Help** | `HelpViewModel` | `ActiveDialog` modal sheet over dimmed page | reuses page chrome beneath scrim |
| **New CookBook** | `NewCookBookViewModel` | `ActiveDialog` centered pane | wizard `.foot` + thin statusbar |
| **New Recipe** | `NewRecipeViewModel` | `ActiveDialog` centered pane | wizard `.foot` + thin statusbar |
| **New Ingredient** | `NewIngredientViewModel` | `ActiveDialog` centered pane | wizard `.foot` + thin statusbar |

## 5. The wiring model

Phase 1's governing rule: **no dead control.** Every interactive element is bound to a command or
navigation on its VM. Wiring is classified into three tiers, and the tier fixes Phase-1 behavior:

- **`nav`** — moves between screens/dialogs (open a screen, launch a wizard, open Help, breadcrumb jump,
  tree select, close/back). **Fully real in Phase 1** — no Core needed.
- **`ui-state`** — mutates VM/view state only (lock↔edit, zoom, theme, tree expand/collapse, table sort,
  active-tab/tool/variant, wizard field state + live previews, `Esc`-close). **Fully real in Phase 1.**
- **`stub`** — needs `Nfty.Core` (open/import/save archives, add, cook, persist edits, real
  colorization). In Phase 1 the command exists, is bound, and its enablement is correct, but its body
  calls **`INotYetWired.Report(actionName)`**, which surfaces a consistent, visible status-bar message
  *"Not wired yet: {action}"* — never a crash, never silent. Phase 2 replaces the body with the real
  Core call; the control, binding, and enablement are untouched.

`INotYetWired` is a service (real impl writes to the shell status line; the test fake records calls), so
every stub is observable in tests. A stub command that a later phase will implement names its target in
the Wiring Map's **Phase-2** column, so the seam is pre-declared.

## 6. The Wiring Map

Every interactive element, per screen. Columns: **Control** → **VM member** (the `[RelayCommand]` or
`[ObservableProperty]` it binds to) → **Tier** (`nav`/`ui-state`/`stub`) → **Phase-2** (the real Core
behavior that replaces a stub; `—` if already real in Phase 1). Command **enablement** is noted inline.

### 6.0 Shell chrome (present on every screen except where noted)

| Control | VM member | Tier | Phase-2 |
|---|---|---|---|
| Minimize / Maximize-Restore / Close | `Shell.Minimize/ToggleMaximize/Close` | ui-state | — |
| `.kroot` Kitchen chip (absent on Landing) | `Shell.OpenKitchen` | stub | Kitchen workspace (deferred, §11) |
| Zoom `−` / `%` / `+` (`Ctrl-` / `Ctrl0` / `Ctrl+`) | `Shell.ZoomOut/ZoomReset/ZoomIn`, `Shell.Zoom` | ui-state | — |
| Help `?` (status bar, rightmost) + `⌘/` | `Shell.ShowHelp` | nav | — |
| Theme (follow OS; optional toggle) | `IThemeService`, `Shell.ToggleTheme` | ui-state | — |
| Global keys `⌘N`/`⌘O`/`⌘I` | route to Landing/Explorer create/open/import commands | nav→stub | see below |

### 6.1 Landing (`LandingViewModel`)

| Control | VM member | Tier | Phase-2 |
|---|---|---|---|
| **New CookBook** (accent, `⌘N`) | `NewCookBook` | nav | opens `NewCookBookViewModel` dialog (real in P1); its **Create** is stub→P2 |
| **New Kitchen…** (dashed, reserved) | `NewKitchen` (disabled) | stub | Kitchen workspace (deferred) |
| **Recipe** | `NewRecipe` | nav | opens `NewRecipeViewModel` dialog |
| **Ingredient** | `NewIngredient` | nav | opens `NewIngredientViewModel` dialog |
| **Open CookBook…** (`⌘O`) | `OpenCookBook` | stub | `IFilePickerService` → `CookBookArchive.Read` → `To(Explorer)` |
| **Import…** (`⌘I`, kind-agnostic) | `Import` | stub | picker → `Archives.KindOf` → read + route |
| **Open a cooked .set…** (dashed) | `OpenSet` (disabled) | stub | Set browser (deferred) |
| **Recent** row × N | `OpenRecent(item)` | stub | read the recorded path → open |
| Recent **empty state** → New CookBook | `NewCookBook` | nav | — |
| **Learn** link | `Shell.ShowHelp` | nav | — |

Recents themselves: `IRecentsService` (real list + persistence) is **ui-state/nav** — the list renders
and its empty state is real in P1; only *opening* a row is a stub until open/read lands.

### 6.2 Explorer (`ExplorerViewModel` + detail sub-VMs)

Titlebar/toolbar/status:

| Control | VM member | Tier | Phase-2 |
|---|---|---|---|
| Breadcrumb segment (CookBook › Recipe › Ingredient) | `SelectNode(node)` | nav | data comes from a real `LoadedCookBook` |
| `lockflag` (titlebar) — mirrors lock | bound to `IsEditing` | ui-state | — |
| Search field + `⌘K` | `Search` | stub | ⌘K command palette (deferred, §11) |
| **Add** (context-aware: recipe/ingredient/variant) | `Add` (label + target via `SelectedNode`) | nav→stub | Add recipe/ingredient → wizard/editor; the **write** is P2 |
| **Delete** (enabled under lock) | `DeleteSelected` (enabled iff `IsEditing`) | stub | mutate `LoadedCookBook` + persist via `CookBookArchive.Write` |
| **Import…** (kind-agnostic) | `Import` | stub | picker → `Archives.KindOf`; **canvas-mismatch reject** + **no silent upsert** (§8) |
| **Lock** toggle (pushed right) | `ToggleLock` → `IsEditing` | ui-state | — |
| Contents **tree** node select / expand | `SelectNode` / `ToggleExpand` | nav / ui-state | data from `LoadedCookBook` |

Cookbook detail (`CookBookDetailViewModel`): identity header, 2×2 metric statband, Mint-distribution
bar+legend, per-recipe DNA-space rows (factor chips + share bars), target-supply line — all **display**
(bound to VM properties; sample data in P1, real `LoadedCookBook`/`UniqueSpace`/`RarityCalculator` in P2).

| Control | VM member | Tier | Phase-2 |
|---|---|---|---|
| **Cook set** button (footer) | `Cook` | stub | `Generator.Generate` + `SetWriter.Write`; Set browser deferred |

Recipe detail (`RecipeDetailViewModel`): hero + factor chips + stats (display), Rules rail rows +
empty-state (display).

| Control | VM member | Tier | Phase-2 |
|---|---|---|---|
| **Reroll** dice (hero portrait) | `Reroll` | ui-state | P2 samples via real `ColorRoller`/`Colorizer` on a value-map |
| Layer-table row → open ingredient | `OpenIngredient(id)` | nav | — |

Ingredient detail (`IngredientDetailViewModel`): art hero, `variant N of M`, in-recipe/overall rarity
meters (live), Colorways rail (kind-aware, display).

| Control | VM member | Tier | Phase-2 |
|---|---|---|---|
| Variant-table **sort** (Variant/Kind/Weight/In-recipe) | `SortBy(col)` | ui-state | — |
| Variant-table **row select** → active variant | `SelectVariant(v)` | ui-state | — |
| Variant **weight** inline input (edit mode) | `Variant.Weight` (two-way) | ui-state | persist to archive (P2) |
| Variant **delete** (edit mode) | `DeleteVariant(v)` (enabled iff `IsEditing`) | stub | mutate + persist |
| **`⚑ N rules`** flag → Recipe Rules rail | `JumpToRules` | nav | — |
| **Edit / pencil** on ingredient → editor | `EditIngredient(id)` | nav | opens `IngredientEditorViewModel` on that ingredient |

### 6.3 Ingredient Editor (`IngredientEditorViewModel`)

Backed by `Nfty.Core.Editing` (which already exists and is tested). Tool **selection** and history are
`ui-state`; applying pixels to a `ValueMap` and exporting are `stub`→P2 (the editor's real behavior slice).

| Control | VM member | Tier | Phase-2 |
|---|---|---|---|
| Variant card **select** | `SelectVariant(v)` | ui-state | — |
| Variant **weight** inline | `Variant.Weight` | ui-state | — |
| **+ Add variant** / **duplicate** / **delete** | `AddVariant`/`DuplicateVariant`/`DeleteVariant` | ui-state | (drafts are in-memory; persistence at Save) |
| Tools: **brush/eraser/rect/circle/triangle/select/fill** | `SelectTool(tool)` | ui-state | — |
| **Value ramp** (0–255 + swatch) | `BrushValue` | ui-state | — |
| **Undo / Redo** | `Undo`/`Redo` (enabled via `EditHistory.CanUndo/Redo`) | ui-state | — |
| **Canvas** pointer paint | `ApplyStroke(pointerArgs)` | stub | apply active tool's `IEditCommand` to the `ValueMap` |
| Colorize **Static \| Dynamic** toggle | `Mode` → draft `LayerKind` | ui-state | — |
| Dynamic **hue-range** / **sat-range** dual sliders + numeric | `HueMin/Max`, `SatMin/Max` | ui-state | — |
| Dynamic **quantize** steppers | `HueQuantize`/`SatQuantize` | ui-state | — |
| Static **hue+sat / swatch picker** | `FixedColor` | ui-state | — |
| `Value ← from grayscale` read-out | (display, locked) | ui-state | — |
| Preview blip **⟳ reroll** | `RerollPreview` | stub | `ColorizedPreview` (real cook path) |
| Preview blip **⤢ enlarge** / **⛶ fill-pane** | `EnlargePreview`/`FillPanePreview` | ui-state | — |
| **Save / Done** | `Save` | stub | `IngredientDraftExporter` → into book (`CookBookEdits`) or loose `.igt` (`IngredientArchive.Write`) |
| **Cancel / Back** | `Back` | nav | — |

### 6.4 Wizards (modal dialogs; `Cancel`=nav close, `Create`=stub→P2)

**New CookBook** (`NewCookBookViewModel`):

| Control | VM member | Tier | Phase-2 |
|---|---|---|---|
| Name (→ derived identifier read-out) | `Name` → `DerivedId` | ui-state | — |
| Symbol (+ hover hint; maxlen 255, empty ok) | `Symbol` | ui-state | — |
| Canvas **W** / **H** + **aspect-lock** chain toggle | `Width`/`Height`/`AspectLocked` | ui-state | — |
| Description | `Description` | ui-state | — |
| **Cancel** / **Create** | `Cancel` / `Create` | nav / stub | `Create` builds `CookBookManifest` + writes `.cbk`, opens Explorer |

**New Recipe** (`NewRecipeViewModel`):

| Control | VM member | Tier | Phase-2 |
|---|---|---|---|
| Name | `Name` | ui-state | — |
| Selection **weight** (+ live "Resulting mix" bar) | `Weight` (bar recomputes) | ui-state | — |
| **Save to** radiogroup (into CookBook / loose Kitchen) | `Destination` (toggles weight inert + path) | ui-state | — |
| **Cancel** / **Create** | `Cancel` / `Create` | nav / stub | write `.rcp` / splice into book (or loose to Kitchen — deferred) |

**New Ingredient** (`NewIngredientViewModel`):

| Control | VM member | Tier | Phase-2 |
|---|---|---|---|
| Name | `Name` | ui-state | — |
| **Kind** 3-radio-cards (Dynamic/Static/Custom) | `Kind` (swaps zone) | ui-state | — |
| Dynamic zone: **hue** + **sat** dual sliders (half-open hint) | `HueRange`/`SatRange` | ui-state | — |
| Static zone: **swatch + colour-spec** field | `FixedColor` | ui-state | — |
| Custom zone: none | — | — | — |
| **Save to** radiogroup (+ Canvas field only when loose) | `Destination`, `Canvas` | ui-state | — |
| **Cancel** / **Create** | `Cancel` / `Create` | nav / stub | opens Ingredient Editor on the draft (or writes loose `.igt`) |

### 6.5 Help (`HelpViewModel`, modal sheet)

Legend content is display-only. `Esc` and scrim-click close (`ui-state`/`nav`). Summoned from three real
triggers (§6.0 `?`, §6.1 Learn, `⌘/`).

## 7. Theming

- **`Tokens.axaml`** — the mockups' locked token block ported once: oxblood `--accent #a11f31`, the
  `--bg`/`--bg-alt`/`--panel`/`--tile` ramp, kind hues `--info`/`--warning`/`--custom`, the `--r-*` radii,
  `--font-mono`. This file is the single source of truth; a new colour literal anywhere else is the drift
  signal, exactly as the mockups enforce. Light + dark are Avalonia **`ThemeVariant`** dictionaries
  (matching the mockups' `prefers-color-scheme` + `data-theme`).
- **`Styles.axaml`** — the component idioms (`.titlebar`, `.tbtn`, `.statusbar`, `.zoomctl`, kind markers,
  tables, rails, wizard `.foot`, sheet/scrim) as `ControlTheme`/`Style` resources reused across screens.
- Honour reduced-motion (Avalonia transitions gated) and a `:focus-visible`-equivalent focus adorner.
- The procedural-pet `<canvas>` becomes `ProceduralPetHost` (a drawn placeholder) in P1, swapped in P2 for
  real `Nfty.Core` value-map colorization rendered to an `Image`.

## 8. Services & Core integration seams

- `INavigationService`, `IDialogService` — §3.
- `IFilePickerService` — wraps Avalonia `StorageProvider` (open/save pickers); faked in tests. Used by
  Open/Import/Save (all P2).
- `IRecentsService` — the Landing's Recent list (persisted); real list/empty-state in P1, opening a row P2.
- `INotYetWired` — the stub notifier (§5).
- `IThemeService` — OS theme follow + toggle.

**Flagged `Nfty.Core` follow-ups** (from the creation-flows spec, needed when Import/editor-save land in
P2 — small Core tasks, planned with their phase, not Phase 1):
1. **Import must not silently upsert.** `CookBookEdits.UpsertIngredient` currently *replaces* on id
   collision; the import path must instead reject (or prompt) on collision. Add a non-replacing splice (or
   a collision result) for the import path.
2. **Canvas-mismatch import is rejected, not resampled** — state the mismatch and refuse.

These live behind the P2 Import/Save slices; Phase 1 only wires the commands to `INotYetWired`.

## 9. Testing

- **VM/wiring unit tests (xUnit, no UI):**
  - *Wiring coverage* — each screen VM exposes exactly the commands the Wiring Map enumerates; a table-
    driven test guards against a dead or missing control.
  - *nav* commands set `CurrentPage`/`ActiveDialog` correctly (via fake `INavigationService`/`IDialogService`).
  - *ui-state* commands mutate state (lock, zoom, sort, tool, mode toggle swaps the Colorize zone, aspect-
    lock scales the paired dimension, "Save to" flips the weight field inert).
  - *stub* commands invoke `INotYetWired` (fake asserts the reported action name) and do **not** touch Core.
  - Command **enablement** (Delete only under lock; Undo/Redo per history; Create gating).
- **Headless-Avalonia smoke tests (`Avalonia.Headless.XUnit`):** `MainWindow` builds; the `ViewLocator`
  resolves every page + dialog VM to a View; a click path (Landing → New CookBook wizard → Cancel →
  Landing; Landing → Open [stub notifies]) runs; light/dark variants both apply.
- Core keeps its own suite; GUI tests never re-test Core.

## 10. Phasing

- **Phase 1 — the wired shell (planned next).** Solution scaffold (`Nfty.App`, `Nfty.Desktop`,
  `Nfty.App.Tests`) + DI + `ViewLocator` + `Tokens.axaml`/`Styles.axaml` + `ShellViewModel` + all six
  screens (Views + VMs) with **every control in §6 wired** (nav/ui-state real; Core actions stubbed via
  `INotYetWired`), the dialog/overlay layer, services (with test fakes), and the §9 tests. **Deliverable:**
  a fully clickable, navigable, themed, VM-tested desktop app that does everything except touch `Nfty.Core`.
  This is the "all wiring complete" milestone.
- **Phase 2+ — behavior per screen (own plans, one slice each):** replace stubs behind the established
  seams —
  - **Open/Import** → `CookBookArchive.Read` / `Archives.KindOf` + the two Core follow-ups (§8) → Explorer
    bound to a real `LoadedCookBook`; Recents opening.
  - **Explorer data** → cookbook/recipe/ingredient details from `LoadedCookBook`, `UniqueSpace`,
    `RarityCalculator`; real reroll/colorization.
  - **Ingredient Editor** → painting via `Nfty.Core.Editing`, `ColorizedPreview`, Save via
    `IngredientDraftExporter` (into book / loose `.igt`).
  - **Wizards Create** → build manifests + write archives (`CookBook/Recipe/IngredientArchive.Write`); New
    Ingredient → open the editor.
  - **Cook** → `Generator.Generate` + `SetWriter.Write` (Set browser still deferred).
  Each is its own `writing-plans` plan under this spec.

## 11. Open items & deferred (reserved, not accidents)

- **Kitchen workspace** — the top-level workspace (`.kroot` chip, loose-to-Kitchen destination). The
  mockups reserve its shape; Phase 1 wires the chip + "New Kitchen" to `INotYetWired`. Its own future spec.
- **⌘K command palette** — the undecided in-app global Create/Open/Import surface past the Landing
  (creation-flows Open Item #2). Phase 1 wires Search/`⌘K` to a stub; the palette is a later decision.
- **Set browser view** — the cooked-`.set` viewer. `Open a cooked .set…` and `Cook`'s output destination
  wire to stubs.
- **Editor custom full-colour mode** — deferred in the editor spec; Phase 1 shows the reserved affordance
  only.
- **Mobile/WASM heads + touch/responsive** — structure ready (§2), layouts later.
- **Mockup corrections (not GUI work):** the phantom `colorize HSV` chip in the Explorer spec/mockup, and
  retrofitting `⌘N`/`⌘O`/`⌘I` into `help.html`'s Keys column (creation-flows Open Item #4).

## 12. Out of scope

- Any `Nfty.Core` change beyond the two flagged import follow-ups (§8), and those land with their Phase-2
  slice, not Phase 1.
- Building the deferred features in §11.
- Re-testing `Nfty.Core` from the GUI suite.
