# Backlog

Work that is deliberately not done yet, with the reason and enough context to pick it up cold.
Nothing here is a defect in shipped behaviour; each is a decision to defer.

*Done and removed from this list: splitting `UniqueSpace`'s cap into an enumeration budget and a
reporting ceiling (0.7.2).*

---

## Reshoot the manual against the Chest Demo

**Status:** deferred by the author until the manual is reworked, some time before 1.0.0.

Every figure in `docs/manual/images/` except `landing-*.png` still shows **Vapor Pets** — the
project that predated the built-in demo. The pages are not *wrong*: the tutorials build Vapor Pets
by hand, step by step, so the words and the pictures agree. But the app now ships with the Chest
Demo, so a reader's first screen and the manual's screenshots are two different collections.

**What it involves.** `tools/docs-capture/` regenerates the figures from the *running app*, and
`tools/demo/build-demo.py --workspace <dir>` already lays out a Kitchen, loose parts and a cooked
Set for exactly this purpose. The capture README carries the two PowerShell traps (`$h` silently
clobbering an `-H` parameter; a blind `Ctrl+T` that yields the same theme twice). Figures are
**pairs** — `thing-light.png#only-light` beside `thing-dark.png#only-dark`.

**Do it as one pass, not piecemeal.** Half-reshot pages would show two collections in one document,
which is worse than one consistently old one. The tutorial prose has to move with the pictures.

**Note the capture size changed.** Page frames were captured at 1416x950 against the old 1128x924
window minimum. The minimum is 1200x712 now (see `ShellViewModel`), so that standard is stale.

---

## The interactive passphrase prompt has no test

**Status:** accepted, and mostly closed by `--key`.

`Passphrase.ReadMasked` drives `Console.ReadKey` and cannot be exercised by a test runner, where
stdin is always redirected. That was half the passphrase path when the only alternative was
`--key-env`; naming the source (`env:` / `file:` / `stdin` / `prompt`) turned three of the four into
ordinary functions with inputs and outputs, covered by `PassphraseSourceTests`.

What is left untested is the genuinely interactive branch: key-by-key echo suppression, backspace,
and Escape. Testing it would mean an indirection over `Console.ReadKey` whose only consumer is the
test — worth it only if that reader grows logic worth checking. It has none today.

---

## `Total` is not a lower bound when the walk is skipped

**Status:** open, latent, pre-dates the budget/ceiling split.

When a recipe has rules and its unconstrained combination count exceeds the enumeration budget,
`RecipeShapes` returns the budget as the total and `IsExact = false`, and front-ends render that as
*"more than 1,000,000"*.

That reads as a lower bound, and it is not one. Rules can only *remove* selections, so a book with
two million combinations and rules excluding all but four hundred of them genuinely admits four
hundred — while the report claims more than a million.

Every other inexact result really is a floor (an under-counted bucket set, a saturated product), so
this is the one case where the documented reading of `IsExact` is wrong. The honest options are to
report it as uncountable (`Total == 0`, which `IsCountable` already renders as "cannot say") or to
carry an upper bound alongside the lower one. Not urgent: it needs a rules-heavy book with more
combinations than the budget, which nothing shipped comes close to.
