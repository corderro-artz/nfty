# Stage 7 — Nfty.Ticket.Sample

Baseline: 5b208dd. Both review passes completed; implementation authorized by the latest instruction to finish the remaining projects and release the audited version.

## Pass 1: behavior

- TS01: The illustrative tint always called HsvToRgb, even for HSL layers, teaching the wrong color math. It now selects the Menu-authoritative color model, matching the real Core colorizer for a mid-gray pixel. A synthetic HSL Menu fails before the correction; HSV remains unchanged.
- TS02: Hue/saturation and geometry reports used current-culture interpolation. Reports now explicitly use invariant formatting; the same Menu/seed prints identical English/French output and identical wire/DNA.
- TS03: Exhausting a bounded 1,000-attempt loop claimed the rules rejected every possible roll. The diagnostic now states only that no legal ticket was obtained within the attempt budget.

Argument handling, prepared-menu reuse, seeded RNG sequencing, Menu ownership, wire round-trip and bottom-first plating were traced. The sample intentionally delegates PNG decoding/composition to the embedding engine; it does not claim to render a file.

## Pass 2: documentation and simplification

No handwritten public API declarations require XML comments in this top-level sample. Reviewed the inline usage, ownership, namespace/assembly explanation and all manual-included regions. Corrected the HSV-only commentary and the companion embedding sample's claim that this program draws pixels. Documented the bounded-failure meaning in the manual. The existing explicit bounded loop is the clearest form: one prepared Menu and one RNG stream, with no helper framework or extra dependencies.

The project retains one production reference, Nfty.Ticket only, and no package references. Source regions remain compiled and included by MkDocs. Tests reference the sample for build ordering only, without changing its dependency boundary.

## Verification

Five sample regressions cover actual compiled entry points. Initial selection: four failed and one passed; the HSL-color and French-report failures directly reproduce TS01/TS02. Final focused result: five passed; final CLI suite: 411 passed. See [integration verification](10-final-verification.md). Tests use temporary synthetic archives, compare the printed color with a real Colorizer pixel, and restore process-wide Console/culture state. Frozen fixtures were not regenerated.
