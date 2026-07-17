# Help view — design spec

**Date:** 2026-07-17
**Status:** Approved (design), pending implementation
**Deliverable:** `docs/design/mockups/help.html` — a new self-contained HTML mockup
**Companion to:** `docs/design/mockups/explorer.html` and `docs/design/mockups/landing.html`

## Purpose

A single-screen **quick reference**, summoned as an overlay over whatever the user is
doing and dismissed with `Esc`. It is not a docs site or a tutorial — it is a legend.
Its whole job is to define, in one glance, the vocabulary the rest of the UI rests on:
the five domain words, the three layer kinds, the rule and state glyphs, the keyboard
chords, and the colour-spec prefixes.

This is a **mockup**, not Avalonia code. Like the Explorer and Landing mockups, it exists
to settle visual direction and interaction before any GUI code is written.

## Settled decisions

Each was chosen by the user from rendered visual options during brainstorming.

### 1. It is an overlay, not a page

The reference is a modal sheet floating over a dimmed app window, centred, with the app
chrome (titlebar, statusbar) still legible beneath a scrim. This is why the mockup draws
it over the Explorer rather than on a bare stage: the honest depiction of a summoned
sheet is *in situ*. `Esc` closes it; the backdrop is dimmed but not hidden.

### 2. Three columns + a footer, one strict glyph gutter

- **Column 1 — The five words:** CookBook, Recipe, Ingredient, Variant, Set. Each row is
  `icon → term → extension → one-line gloss`. The icon *is* the glossary bullet, so every
  symbol is defined exactly once, where it is explained (an earlier pass listed the icons
  twice — once as a symbol key, once as a glossary — and that redundancy was cut).
- **Column 2 — Layer kinds, then Rules & state:** the D/S/C kind letters in their hues,
  then the rule operators and state glyphs (`✕` never together, `→` always together, `⚑`
  layer is in a rule, `●` cookbook is valid).
- **Column 3 — Keys, then Colour:** the keyboard chords, then the four colour-spec
  prefixes with example values.
- **Footer — Unique DNA:** the one idea that is neither a symbol nor a single word gets
  the full-width base, with the factor equation `4 × 3 × 5 × 6 = 360` pinned right,
  echoing the Cookbook view's DNA-space chips.

Every glyph — SVG icon, kind letter, or dingbat — sits in the same **20px gutter**, so all
terms align down the page regardless of what marks them. That single alignment is most of
what reads as "composed." Hairlines divide the columns; no boxes — restraint over containers.

### 3. Two new icons — Variant and Set

The icon family in `explorer.html` only had marks for CookBook, Recipe and Ingredient.
Variant and Set needed their own, built in the family's construction language (24×24,
`stroke="currentColor"` at ~1.3, `--panel` body fill, exactly one `--accent` highlight):

- **Variant** — a single framed image (sun + mountain horizon), accent on the sun. It
  reads as "one PNG," the singular counterpart to the Ingredient's *stack* of diamonds.
- **Set** — a 2×2 grid, top-left cell washed and bottom-right filled accent. It reads as
  "the finished collection." Input is a book (CookBook); output is a grid (Set).

Two alternates were rendered (a photo *card*, a *foldered* stack) and rejected — the
primaries are more legible at 16px and truer to the family.

### 4. Where it is summoned from

Help must be reachable in **both** the Explorer (cookbook open) and the Landing view
(nothing open). Those two share only the **titlebar** and **statusbar** as chrome — the
Landing view omits the toolbar entirely — so a toolbar button is out. The decision is
three affordances, all opening the one sheet:

- **A quiet `?` button at the far-right end of the statusbar** — the always-present
  trigger. The statusbar is retained in both views and already hosts a right-aligned
  control cluster (the zoom control, via `margin-left:auto`), so the `?` tucks in beside
  it. In the mockup it carries a faint accent wash to mark it as the summon point.
- **The Landing view's reserved "Learn" link** — the discoverable entry for a new user
  staring at an empty window who does not yet know a shortcut exists. (This link is the
  one the Landing spec carved out under "Known future addition — Learn / docs.")
- **`⌘/`** — the keyboard path, consistent with the cmd-chord convention. `⌘/` is the
  near-universal "show shortcuts/help" chord (Slack, Linear, Notion), so it costs no
  learning. **Deliberately not a bare `?` leader** — the chord stays a cmd-chord for
  consistency. The Keys column lists `⌘/` (not `?`) as "this sheet."

It is deliberately **not** in the titlebar: that edge is spatially tight and its right
side belongs to the window controls (min/max/close); a help button there would break that
grouping.

### 5. Copy

- Header: brandtile + `nft<b>y</b>` wordmark + `.slbl` "Quick reference" + an `Esc` chip.
- The five glosses are one line each, plain-language, and lean on the accent only for the
  file extension and the word **Cook** (what produces a Set).
- **"Unique DNA" is required verbiage** — never "combinations", never "unique images".
- The accent stays scarce: the wordmark `y`, the file extensions, **Cook**, `✕`, the four
  colour prefixes, and the statusbar `?`. Everything else is ink and muted ink.

## Style constraints

The Explorer's look is locked. This mockup **reuses, never redefines**:

- The complete token block — `:root`, the `prefers-color-scheme: dark` block, and both
  `:root[data-theme]` overrides — copied **verbatim** from `explorer.html` (lines 4–48).
  Verified identical by diff.
- Existing component idioms: `.titlebar`, `.brandtile`, `.wordmark`, `.tdiv`, `.crumbs`,
  `.wc`, `.exp-toolbar`, `.search`, `.tbtn`, `.statusbar`, `.zoomctl`, `.pitch`, `.ghost`,
  `.frame`, `.window`, `.note`.
- The `@media (prefers-reduced-motion: reduce)` block and the `:focus-visible` outline rule.

**Inventing a colour is the drift signal.** Any new hex value means a token was missed.

New CSS is limited to: the `.sheet` and its interior (`.sh-h`/`.sh-b`/`.sh-f`, `.col`,
`.e`/`.g`, the kind/rule/glyph marks, the keycap and colour-spec lines, the DNA footer),
the `.scrim`/`.overlay` positioning, the dimmed-app placeholder (`.winbody`, `.ghost-rows`,
`.gr`), and the statusbar `.helpbtn`.

Structural conventions from `explorer.html`, all preserved: no
`<!doctype>/<html>/<head>/<body>` (the publish host wraps it); everything inline, no
external resources; theme-aware via `prefers-color-scheme` plus a `data-theme` toggle; the
`.stage` → `.pitch` → `.frame` → `.window` → `.note` scaffold; 1180px window width; the
theme toggle is a `.ghost` button in `.pitch`, outside the `.window`.

## Verification

1. Wrap with the charset shim documented in `mockups/README.md` before viewing locally —
   without it `✕`, `→`, `⚑`, `●`, `⌘`, `›`, `·`, `—` render as mojibake.
2. Screenshot headless Chrome (chrome-devtools MCP is unavailable; drive
   `google-chrome --headless` directly) in **both** light and dark.
3. Diff the token block against `explorer.html` — it must match verbatim.
4. Confirm the two new glyphs (Variant, Set) sit in the same 20px gutter as the three
   existing icons and read at 16px.

## Deferred / downstream

- The `?`, the Landing "Learn" link, and `⌘/` are all mockup affordances; wiring them (and
  the actual `Esc`-to-close behaviour) is Avalonia work, out of scope here.
- The Landing spec's "Known future addition — Learn / docs" is now **resolved by this
  spec**: the Learn link is one of the three summon points for this sheet.

## Out of scope

- Any Avalonia/C# implementation. Mockup only.
- Any `Nfty.Core` change. Nothing here touches the engine.
- A full docs/tutorial surface — this is a one-screen legend, deliberately not a manual.
