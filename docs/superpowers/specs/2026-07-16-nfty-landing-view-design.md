# Landing view — design spec

**Date:** 2026-07-16
**Status:** Approved (design), pending implementation
**Deliverable:** `docs/design/mockups/landing.html` — a new self-contained HTML mockup
**Companion to:** `docs/design/mockups/explorer.html` (the locked Explorer, ~iteration 22)

## Purpose

The view the app opens on, before any cookbook is loaded — the VS Code "Welcome tab"
equivalent. It states what the app is and offers the only three things a user can
usefully do with nothing open: create a cookbook, open a cookbook, or open a cooked
set.

This is a **mockup**, not Avalonia code. Like `explorer.html`, it exists to settle
visual direction and interaction before any GUI code is written.

## Settled decisions

Each was chosen by the user from rendered visual options during brainstorming.

### 1. Vocabulary — CookBook, with a demoted Set hatch

The buttons act on **CookBooks** (`.cbk`), not Sets. This follows the locked domain:
a CookBook is the authored input the Explorer browses; a Set (`.set`) is the cooked
output of pressing Cook. `mockups/README.md` requires this vocabulary stay consistent
across UX and code.

A third, demoted action — **"Open a cooked .set…"** — is present and deliberately
subordinate to the two CookBook actions.

### 2. Layout — two-column split

Left column: wordmark, tagline, **Start** action stack.
Right column: **Recent**.

Chosen over a centered stack (dead space at 1180px; recents read badly centered) and
over an art-wall variant using the Explorer's procedural pet canvas (art competed with
the wordmark; read as a sample gallery rather than an empty state).

### 3. Recent rows — name + nfty metrics

Each row: icon, filename in `--accent-text`, a mono metrics line, folder path pushed
right. Metrics are `N recipes · N unique DNA`.

**"unique DNA" is required verbiage** — never "combinations", never "unique images".
This matches the Cookbook view's existing headline metric and `Nfty.Core`'s DNA hash.

Rejected: a per-cookbook thumbnail. Rendering one means either rolling an asset just to
draw a list row, or storing a preview PNG inside the `.cbk` — a `Nfty.Core` format
change, out of scope for a GUI mockup.

**Zero state (first run)** is a dashed panel reading:
> Nothing here yet
> Cookbooks you open will collect here. Start with **New CookBook**.

Both states — populated and zero — must be reachable in the mockup. **One window, toggled**,
not two windows stacked: `explorer.html` is a single live window and `landing.html` must
read the same way. The toggle is a `.ghost` button in `.pitch`, beside the existing theme
toggle, swapping the Recent column between populated and zero. (The brainstorming
composite stacked two windows; that was a comparison device for review, not the design.)

### 4. Chrome — titlebar + statusbar, no toolbar

- **Titlebar:** unchanged from the Explorer — brandtile, `n<b>f</b>ty` wordmark, window
  controls. The breadcrumb slot shows a muted `— nothing open —`, preserving the
  titlebar's shape.
- **Toolbar:** **omitted entirely**, not disabled. With no cookbook open, search / Add /
  Delete / Import / lock are all meaningless; a row of greyed controls is a poor first
  impression. The toolbar returns when a cookbook opens.
- **Statusbar:** retained, reading `No cookbook open`, with the `.zoomctl` control on the
  right (`margin-left: auto`, as in the Explorer). It stays because zoom lives there and
  needs a home, and because dropping it would make the landing view read as a different
  app than the Explorer.
  - The Explorer's statusbar also carries `● Valid` and the recipe/ingredient/variant
    totals. Both are **omitted** here: with nothing open there is nothing to validate and
    nothing to count.
  - **The theme toggle is not in the statusbar.** In `explorer.html` it is a `.ghost`
    button in `.pitch`, outside the `.window` — part of the demo scaffold, not the app
    chrome. `landing.html` must do the same. (Early brainstorming mockups drew it inside
    the statusbar; that was an error and must not be carried into the mockup.)

### 5. Copy

- Wordmark: `n<b>f</b>ty` — `font-size: 46px`, `--font-mono`, weight 700,
  `letter-spacing: -.025em`, with the `f` in `--accent-text`. This reuses the `.wordmark`
  markup but not its 15px titlebar sizing; the landing wordmark is its own class.
- Tagline: **"Asset Generator"**.
- Actions: `+ New CookBook` (accent), `↗ Open CookBook…` (plain), `↗ Open a cooked .set…`
  (dashed, muted).
- Shortcut hints `⌘N` / `⌘O` appear on the two primary actions, consistent with the
  Explorer's existing `⌘K` hint in its search field.

## Deferred dependency — the Set view

**"Open a cooked .set…" is inert.** No Explorer view for browsing a cooked Set exists
yet. The action is present to reserve its shape and placement; wiring it is explicitly
out of scope here.

The user has confirmed the Set view is planned as separate later work. When it lands,
this action becomes live and its dashed treatment should be reconsidered — the dashed,
muted styling is what marks it as not-yet-real.

## Known future addition — Learn / docs

A **Learn** link (e.g. "The cooking metaphor →") under Recent was explored and
deliberately cut. The built-in help/docs page is the next design task after this one;
this landing view is the likely home for an entry point to it. Left off now to avoid
smuggling in a docs surface without designing it.

## Style constraints

The Explorer's look is locked. This mockup **reuses, never redefines**:

- The complete token block — `:root`, the `prefers-color-scheme: dark` block, and both
  `:root[data-theme="light"]` / `[data-theme="dark"]` overrides — copied verbatim from
  `explorer.html`. Oxblood `--accent: #a11f31`; the `--bg` / `--bg-alt` / `--panel` /
  `--tile` ramp; `--font-mono`; the `--r-win`/`--r-lg`/`--r-md`/`--r-sm`/`--r-xs` radii.
- Existing component idioms: `.window`, `.titlebar`, `.brand`, `.brandtile`, `.wordmark`,
  `.tdiv`, `.tbtn` / `.tbtn.accent`, `.statusbar`.
- The `@media (prefers-reduced-motion: reduce)` block and the `:focus-visible` outline rule.

**Inventing a color is the drift signal.** Any new hex value means a token was missed.

New CSS is limited to: a dashed/muted button modifier (no such idiom exists today), the
Start/Recent two-column layout, the recent-row, and the zero-state panel.

Structural conventions from `explorer.html`, all preserved: no
`<!doctype>/<html>/<head>/<body>` (the publish host wraps it); everything inline, no
external resources; theme-aware via `prefers-color-scheme` plus a `data-theme` toggle;
the `.stage` → `.pitch` → `.frame` → `.window` → `.note` scaffold; 1180px window width.

## Verification

1. Wrap with the charset shim documented in `mockups/README.md` before viewing locally —
   without it, `↗`, `⌘`, `·`, `—` render as mojibake.
2. Screenshot headless Chrome (per project memory: chrome-devtools MCP is unavailable;
   drive `google-chrome --headless` directly) in **both** light and dark.
3. Confirm both states render: populated recents and the first-run zero state.
4. Diff the token block against `explorer.html` — it must match verbatim.

## Out of scope

- Any Avalonia/C# implementation. Mockup only.
- Any `Nfty.Core` change. Nothing here touches the engine.
- The Set browser view (deferred, above).
- The help/docs page and its entry point (next task, above).
- The New CookBook creation flow itself — this view only offers the entry point.
