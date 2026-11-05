# Stage 1: Nfty.Ticket

Status: both review passes complete; fixes approved and implemented in the isolated worktree. Current-source adaptation and verification are tracked in [REVIEW.md](REVIEW.md).

**Historical report:** the findings below were first reproduced on stale local `main` (`fea71d8`). After the user identified the missing seed die, the branch was rebased onto current `origin/main` (`5dece66`). Upstream already fixes some T03 malformed-input examples and replaces System.Text.Json with the shared package-free `MenuJson` reader. Current fixes retain that reader and both target frameworks. D05's obsolete TryGetInt32 discussion is no longer applicable. Current verification, rather than the original counts below, determines readiness.

Baseline: `fea71d8dc532074b67a4bd595a4f72cd546ab8fb`, reviewed 2026-11-04.

Scope: all 28 production C# files, project configuration, all six Ticket test files, and relevant Core/CLI callers and integration coverage. The `Nfty.Core.*` namespaces in this assembly are intentional; they are not circular assembly references.

## Verification

- `dotnet build src/Nfty.Ticket/Nfty.Ticket.csproj --no-restore`: passed, zero warnings/errors.
- `dotnet test tests/Nfty.Ticket.Tests/Nfty.Ticket.Tests.csproj`: 70 passed, zero failed/skipped.
- Targeted `Nfty.Core.Tests`: 113 passed, zero failed/skipped. Covered DNA, determinism guards, color parsing/conversion/rolling/buckets, nonfinite weights, weighted rolling, RNG, rules, kinds, optional layers, Menu writing/depth/fidelity.
- Additional temporary C# probes executed against built assemblies, including real Core validation, generation and pixel rendering. Results below are observations, not merely suspected paths.
- Initial `dotnet test --no-restore` ran no tests; it is not counted as a pass. Restore needed network permission, then actual execution succeeded.
- Initial solution `dotnet build nfty.sln --no-restore` exited 1 and reported six ImageSharp advisory warnings, zero compiler errors. Its output did not identify the failure cause. No clean whole-solution result is claimed; the complete solution test suite was not run in this stage.
- ImageSharp remains pinned at 3.1.11. Advisory applicability belongs to the Core/dependency stage; warnings do not establish exploitability here.

## Pass 1: defects

### T01 — P1: valid identifiers collide in DNA

Location: [Dna.cs](M:/Repositories/nfty/src/Nfty.Ticket/Generation/Dna.cs:34), with [Validator.cs](M:/Repositories/nfty/src/Nfty.Core/Formats/Validator.cs:544).

The hash input joins ingredient and variant IDs with an unescaped `=`. Both IDs may legally contain `=`. Selection `(ingredient="a", variant="b=c")` and selection `(ingredient="a=b", variant="c")` therefore produce the same `a=b=c|` segment.

**Confirmed end to end:** a recipe with those two Custom layers, each 50% optional, passes validation with zero problems. `UniqueSpace.Count` reports 4; `Generator.Generate` requesting 4 throws:

```text
Could not produce a unique asset: this cookbook allows exactly 4 unique DNA, but 4 were requested (3 generated).
```

**Proposed remedy:** reject ambiguous ingredient IDs at authoring/validation boundaries while preserving the existing hash for supported books. Work out treatment of existing archives explicitly. Do not silently escape/reformat hash inputs or add depth to DNA: that changes minted identities. A new DNA encoding would require a separately approved version/migration design.

**Regression:** reproduce the complete optional-layer book, pin existing DNA vectors, and verify accepted books no longer overpromise their space. This fix necessarily crosses into Core validation.

### T02 — P1: ticket order overrides the Menu's paint order

Location: [Plating.cs](M:/Repositories/nfty/src/Nfty.Ticket/Menu/Plating.cs:66).

`Plate` enumerates `ticket.Layers`. DNA deliberately sorts by ingredient ID, so reversing wire layer fields preserves DNA and passes `AssetTicket.Parse`. Plating then renders the reversed stack instead of the Menu's `LayerOrder`.

**Confirmed:** the same two opaque layers and same DNA render `Rgba32(0, 0, 255, 255)` originally and `Rgba32(255, 0, 0, 255)` after wire reordering. CLI `serve --ticket` reaches this through `MenuRenderer.Render`.

**Proposed remedy:** resolve the recipe once, reject duplicate/unknown layer IDs, then produce draws in authoritative Menu order, or explicitly reject a noncanonical ticket order. Preserve optional omissions and unchanged DNA. Validate the recipe even for an empty ticket; currently an empty ticket bypasses every recipe lookup.

**Regression:** reverse valid wire fields, parse, and assert canonical pixels or a named refusal; include duplicate layers, optional omissions and an empty ticket naming an unknown recipe. DNA verification is not authentication and should not be described as such.

### T03 — P2: malformed Menus escape validation

Location: [MenuArchive.cs](M:/Repositories/nfty/src/Nfty.Ticket/Menu/MenuArchive.cs:207), [schema reader](M:/Repositories/nfty/src/Nfty.Ticket/Menu/MenuArchive.cs:265).

Validation stops at selected recipe members. It does not validate null collection elements, layer/variant/color/rule structure, ID uniqueness, layer-order membership, or cell geometry. Missing required numeric fields can silently become zero. Duplicate cell/recipe IDs overwrite earlier definitions in lookup caches.

**Confirmed:** root `[]` produces `InvalidOperationException`; `recipes:[null]` produces `NullReferenceException`; omitting layer `variants` is accepted by `Open`, then `Ticketer.Prepare` throws `ArgumentNullException`. Cell depth length also uses unchecked `width * height * 2`, unlike `DepthMap.FromBytes`.

**Proposed remedy:** one dependency-free Menu validation boundary, before indexing or rolling. Check JSON root kind, required nested members and null elements, unique IDs, structural references, kind/color coherence, finite/legal weights and dimensions. Report contextual `InvalidDataException`; use checked or widened size arithmetic. Retain intentionally optional fields and schema-first refusal.

**Regression:** table-driven malformed manifest cases, including null items, missing nested properties, duplicate IDs, bad references and overflowing cell dimensions. Also retain genuine v2 archive round-trips.

### T04 — P2: invalid ZIP construction leaks its owned stream

Location: [MenuArchive.cs](M:/Repositories/nfty/src/Nfty.Ticket/Menu/MenuArchive.cs:119).

`new ZipArchive(...)` executes before the cleanup region. If construction fails, `Open(string)` loses its newly opened FileStream without deterministic disposal; repeated bad-file opens can retain handles until finalization.

**Confirmed:** `Open(new MemoryStream([1,2,3]))` throws `InvalidDataException`, but the stream remains readable afterward.

**Proposed remedy:** make ownership explicit on failure and cover constructor failure as well as manifest failure. The path overload must always release its stream if no archive is returned.

**Regression:** tracking stream for constructor/manifest failures and successful disposal; Windows malformed-file test proving immediate exclusive reopen/delete.

### T05 — P2: nonfinite color components silently become colors

Location: [ColorSpec.cs](M:/Repositories/nfty/src/Nfty.Ticket/Imaging/ColorSpec.cs:75).

`double.Parse` accepts nonfinite components and overflowing exponents; later floating-point conversions produce ordinary RGB bytes. Core validates fixed specs by calling this parser, so these are accepted author inputs, not just misuse of an internal helper.

**Confirmed:** `rgb:NaN,0,0` becomes black; `hsv:Infinity,100,100` becomes red; `hsl:0,NaN,50` becomes black.

**Proposed remedy:** reject every nonfinite component with a contextual `FormatException` before conversion. State existing finite clamping/wrapping behavior separately; changing it is not required for this fix.

**Regression:** NaN, positive/negative infinity and overflowing exponents in all three component positions and all numeric prefixes; preserve valid fractional/invariant inputs.

### T06 — P2: invalid depth coordinates silently address another pixel

Location: [DepthMap.cs](M:/Repositories/nfty/src/Nfty.Ticket/Model/DepthMap.cs:64).

`IndexOf` flattens coordinates without checking each axis. Array bounds catch only some errors; `(Width,0)` aliases `(0,1)`, and negative/overflowing coordinates can similarly alias valid indices.

**Confirmed:** `DepthMap.Flat(2,2).With(2,0,99,88)` succeeds and sets `Front(0,1)` to 99.

**Proposed remedy:** validate both coordinates in the shared indexing helper before arithmetic. Document valid ranges and exceptions. No existing GUI corruption was established; this is a reproducible public-API correctness defect.

**Regression:** reads and writes at each invalid edge, negative coordinates and overflowing coordinates; retain valid corner access and immutability tests. Check hot-loop impact before adding any separate unchecked API.

### T07 — P2: optional-weight arithmetic overflows unnecessarily

Location: [WeightedRoller.cs](M:/Repositories/nfty/src/Nfty.Ticket/Generation/WeightedRoller.cs:142).

`variantTotal * percent / (100 - percent)` can overflow in the multiplication even when the absent weight and combined total are representable.

**Confirmed:** `AbsentWeight(50, 1e307)` returns infinity; the correct absent weight is `1e307` and combined total `2e307` is finite. The subsequent roll refuses it. Finite weights of this size pass the individual weight checks.

**Proposed remedy:** retain existing arithmetic for ordinary inputs and add an overflow-safe path for representable extreme cases. Avoid globally reassociating floating-point operations: that can change established seeded outcomes. Report genuinely unrepresentable totals clearly.

**Regression:** the example above, near-100 absence, ordinary seeded baselines and truly overflowing totals.

### T08 — P2: rounding can select a zero-weight entry

Location: [WeightedRoller.cs](M:/Repositories/nfty/src/Nfty.Ticket/Generation/WeightedRoller.cs:108), [ordinary overload](M:/Repositories/nfty/src/Nfty.Ticket/Generation/WeightedRoller.cs:178).

The fallback assumes `NextDouble() * total < total`. IEEE floating-point rounding does not guarantee that. When the product rounds to `total`, the scan falls through to the last key, even if its weight is zero.

**Confirmed:** weights `{a: double.Epsilon, z: 0}`, RNG returning `0.75`, selects `z` through both overloads. The same fallback shape exists in `ColorRoller.PickEntry` and needs equivalent boundary coverage.

**Proposed remedy:** handle the rounded upper endpoint without choosing a zero-weight outcome; preserve the single RNG draw and ordinary results. Consolidate the duplicated scan as part of this fix rather than maintaining two boundary fixes.

**Regression:** subnormal totals, trailing zero weights, optional outcomes and color entries, plus the existing distribution and deterministic sequence tests.

## Pass 2: XML documentation

Public-member summaries are present and the compiler's documentation check passes. That check does not establish complete parameter/return/exception contracts or truthful descriptions.

| ID | Priority | Location | Required correction |
|---|---|---|---|
| D01 | P2 | `Menu/Plating.cs:21` | The advertised renderer formula only uses HSV. HSL layers require HSL conversion using source lightness. A third-party renderer following the comment renders HSL incorrectly. |
| D02 | P3 | `Model/LayerKind.cs:8` | Static color consumes no RNG; selecting its variant still does. Static layers with multiple variants do add uniqueness. Qualify the statement. |
| D03 | P3 | `Model/Schema.cs:16`, `Menu/MenuSchema.cs:9`, `Menu/MenuArchive.cs:98,111` | Supported versions are `Oldest..Current`, not every earlier version. `Open` also throws `NotSupportedException` for too-old versions, not only newer ones. Remove stale pre-v2 wording. |
| D04 | P3 | `Menu/AssetTicket.cs:56` | Wire grammar omits the mandatory sixth field, `kind`, and the Custom empty-field form. Show the actual format already pinned by tests. |
| D05 | P3 | `Menu/MenuArchive.cs:202,268` | The claimed complete structural check is incomplete (T03). `TryGetInt32` does throw on nonnumeric kinds; the existing `ValueKind` guard prevents it. Correct the explanation. |
| D06 | P3 | `Model/Colorization.cs:16`, `Generation/WeightedRoller.cs:176` | Distinguish mathematical intervals from floating-point results. `SampleHue([100,101], BitDecrement(1))` returns 101. The supposedly unreachable weighted fallback is reachable (T08). Do not change established sampling arithmetic merely to match prose. |
| D07 | P3 | `Generation/ColorRoller.cs:59`, `Generation/WeightedRoller.cs:36`, `Model/Schema.cs:38`, `Model/DepthMap.cs:71,76` | Add missing public parameter/return contracts, valid-input assumptions and relevant exceptions. `Ticketer.Prepare` also needs a clear validation/precondition contract. Document MenuArchive ownership on failure and lack of concurrent ZIP-read safety. |
| D08 | P3 | `tests/Nfty.Ticket.Tests/TicketerTests.cs`, rules-placement test comment | The claim that Exclude can never expose early checking is false: a three-layer recipe can reject after layer two, skipping layer three. Existing Require test is useful; shorten/correct its rationale. |

Official documentation confirms that nullable-annotation enforcement does not cover collection elements or missing properties: [System.Text.Json nullable annotations](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/nullable-annotations). The nonnumeric exception is documented on [JsonElement.TryGetInt32](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonelement.trygetint32?view=net-10.0). Context7 was not available in this session.

## Pass 2: simplification and performance candidates

These are proposals, not additional proven runtime defects.

| ID | Proposal | Constraint / value |
|---|---|---|
| S01 | Share the two `WeightedRoller.Roll` scans and total validation. | Highest-value cleanup: T08 currently needs two fixes. Preserve zero-absence bit identity, draw count and failure timing. |
| S02 | Replace MenuArchive's success flag/finally with direct ownership/exception cleanup; extract repeated ZIP-entry byte reading where policies match. | Fewer branches and three copies of stream-to-array mechanics. Keep lazy art access and depth length checks; do not introduce global atlas caching. |
| S03 | Restructure XML comments around current contracts, with short remarks for load-bearing reasons. | Long historical narratives obscure API behavior and contain contradictions above. Preserve determinism, compatibility and ownership rationale; this is a user-requested documentation cleanup, not indiscriminate comment removal. |
| S04 | Profile fixed-color parsing and repeated dynamic color-entry sums inside `TryRoll`. | Static colors are reparsed for every asset; dynamic entry totals are resummed. Preparation could cache immutable derived values. No timing benefit was measured, so optimization remains conditional. Keep RNG consumption and floating-point summation order unchanged. |
| S05 | Consider removing the intermediate `LayerSelection` list in `AssetTicket.ComputeDna`. | It allocates per accepted or rejected-by-dedup ticket. Only worthwhile if allocation profiling justifies a small shared hashing overload; do not create a second DNA implementation. |

No broad syntax rewrite, generic framework, new dependency, RNG replacement, hash-format rewrite or eager loading is recommended. File-scoped namespaces, records, pattern matching and existing collection expressions are already modern. Converting isolated `new[]` expressions is optional style churn, not a defect.

## Approval and implementation boundaries

- **Compatibility-sensitive:** T01 needs an explicit existing-archive policy. T07/T08 must preserve ordinary seeded sequences. These warrant stronger reasoning and independent regression checks.
- **Focused fixes:** T02–T06, D01–D08 and S01/S02 are suitable for a cheaper implementation model such as `gpt-6.1-sol`, split by ownership if approved.
- **Measurement only unless separately approved:** S04/S05.
- After approved implementation: full solution build/tests, targeted regressions, and unchanged fixture archives. No GUI changes are proposed here.
- Stop here. Stage 2 (`Nfty.Core`) has not begun; Core reads in this report were only to establish Ticket impact.
