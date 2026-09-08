# Backlog

Work that is deliberately not done yet, with the reason and enough context to pick it up cold.
Nothing here is a defect in shipped behaviour; each is a decision to defer.

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

## Split `UniqueSpace`'s cap in two

**Status:** open. Small, and it removes a tax that is paid every time the demo grows.

`UniqueSpace.DefaultCap` (1,000,000) currently bounds **two different things**, and only one of them
is expensive:

1. **Enumeration.** With rules present, `RecipeShapes` walks legal selections, and `bound` — the
   product of each layer's variant count — is what decides whether that walk is affordable. This cap
   is real and load-bearing.
2. **The reported total.** With no rules it is pure multiplication, and even with rules the colour
   buckets multiply in afterwards. Saturating here costs nothing to compute and only *loses*
   information: a book with five million distinct assets is exactly countable in one multiply, and
   reports "more than 1000000" instead.

The visible cost is that adding any layer to the demo means re-tuning quantize steps to stay under a
ceiling that is not defending anything on the multiplication path. `DemoCookBookTests` asserts the
count is exact, so this is a build failure rather than a cosmetic one, and it will recur.

**The change:** give `Count` two limits — an enumeration budget (stays ~1e6, bounds the walk) and a
reporting ceiling much higher, guarded against `long` overflow. `IsExact` then means "we did not
give up", which is what every caller already reads it as.

**Check before doing it:** `UniqueSpaceExhaustedException`'s message quotes the maximum, and the
Ingredient editor prints `CountColors`. Both should read better, not worse, with a higher ceiling.

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
