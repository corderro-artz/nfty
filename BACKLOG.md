# Backlog

Work that is deliberately not done yet, with the reason and enough context to pick it up cold.
Nothing here is a defect in shipped behaviour; each is a decision to defer.

*Done and removed from this list: splitting `UniqueSpace`'s cap into an enumeration budget and a
reporting ceiling (0.7.2), and `Total` not being a lower bound when the walk is skipped (0.8.0 —
`SpaceCertainty` names the direction, so that case reports "at most N").*

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
window minimum. The minimum is 1280x720 now (see `ShellViewModel`), so that standard is stale.

**The README's hero is already current and is NOT part of this pass.** `assets/readme/` holds one
fresh pair of the Chest Demo, shot with the same tools; see its README. Leave it where it is — a
figure inside the manual has to agree with the prose beside it, and that is what makes this a single
pass; the hero answers to nobody.

---

## The README has no project icon beside the Vaporsoft one

**Status:** deferred by the author, 2026-09-14. Cosmetic; nothing is wrong without it.

`akira` and `kata` both open with two badges — the Vaporsoft logo, then the project's own icon linked
to its home. nfty opens with one, because it has no icon file to link.

**The mark already exists and has a generator.** `tools/icons/make-app-icon.py` reproduces the
titlebar's mark — a lowercase `n` in IBM Plex Mono Bold turned 45 degrees, on the washed and outlined
tile — from the theme's own token values, and writes `src/Nfty.Desktop/nfty.ico` at every size Windows
asks for. So the work is not drawing anything; it is teaching that script to also emit a web-sized
`icons/nfty-icon.png`, and adding the second badge line to the README.

**Do it through the script, not by exporting the `.ico`.** A second copy nobody generates is exactly
the drift `IconSourceTests` exists to prevent, and the values are named in one place on purpose. An
SVG with a `<text>` element is not an option either: GitHub will not have IBM Plex, so the mark would
render in whatever face the reader's browser falls back to.

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
