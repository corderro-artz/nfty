# Tooling and CI audit

Date: 2026-11-08. Worktree: `ticket-audit-fixes/nfty`. Scope: all executable code under
`tools/`, both GitHub workflows, and shared build configuration. This review followed the completed
Ticket, Core, Model, CLI and App reviews; it does not reopen them. Desktop and sample changes belong
to the concurrent reviewers. No dependency versions, frozen fixtures, archived designs or product
generation algorithms were changed by this review.

## Inventory and first pass

The first pass traced inputs, filesystem boundaries, subprocess failures, disposal, ordering and
the actual gates that run each tool.

| Executable or configuration | Review result |
|---|---|
| `tools/api-docs/build.py` | XML/public-surface join, inheritance, rendering, lint and committed-tree comparison reviewed. Generation uses ordinal Python sorting and explicit LF. Corrected the Ticket framework description and inaccurate temporary-folder help. |
| `tools/api-docs/surface/Program.cs`, `Surface.csproj` | Reflection visibility, XML IDs, signatures, nullable/default values and inherited members reviewed. Fixed record-struct detection and generic constraint declarations. Removed one duplicate branch producing identical output. |
| `tools/demo/build-demo.py` | CLI authoring route, depth, ordinal ordering and recursive archive comparison reviewed. `--check` writes scratch output, not the embedded archive. Not rerun merely to redraw existing art. |
| `tools/demo/draw-chest-art.py` | All art/depth constructors and overlay contract reviewed. Coordinates and ramps are deterministic; there is no RNG. No changes or regenerated sprites. |
| `tools/demo/contact-sheet.py` | Art/depth inspection output reviewed. This is a developer preview, not a product asset writer. No changes. |
| `tools/diagrams/build.py` | Browser render and loopback write path reviewed. Fixed unauthenticated file writes, path traversal and acceptance of invalid/incomplete render pairs. |
| `tools/docs-capture/pair.py` | Subprocess and theme verification reviewed. Fixed ignored capture failures, stale temporary-frame reuse, and default height disagreement with the documented crop size. Image handles now close explicitly. |
| `tools/docs-capture/shot.ps1` | Window selection, focus, geometry, screen capture and resource disposal reviewed. Fixed width verification, undefined dimension labels and ignored terminating errors. |
| `tools/docs-capture/click.ps1` | Pointer placement and click loop reviewed. Failed placement now stops before issuing mouse input. |
| `tools/docs-capture/crop.py`, `label.py` | Figure mapping, identical theme boxes and pair relabeling reviewed. Missing optional captures remain visibly reported rather than silently generated. No figure assets regenerated. |
| `tools/icons/build.py` | Source ordering, geometry count, XML comment escaping and explicit LF reviewed. Existing `IconSourceTests` owns the source/generated agreement. No icon generation. |
| `tools/icons/extract.py` | Historical one-shot reverse extraction reviewed. Its README/module warning correctly says to use SVGs as the ongoing source; not rerun against the current glyph groups. |
| `tools/icons/make-app-icon.py` | Per-size ICO frames, Linux PNG, compositing and font-independent drawing reviewed. No binary icons regenerated. |
| `tools/mkdocs-hooks/fonts.py` | Font enumeration and license redistribution reviewed. It now refuses to create a downloadable font zip when OFL.txt is missing. |
| `tools/release/build.py` | Four shapes, two front ends, RID routing, demo verification, licenses, tar executable modes, Linux launcher and cleanup reviewed. Fixes below. No release packaging invoked by this reviewer. |
| `tools/verification/headless-callback-probe/Program.cs`, `probe.csproj` | Existing production callback regression and friend-assembly setup reviewed. Kept outside the solution/xUnit process; added CI execution in both live and disposed modes on Windows and Linux. |
| `tools/tests/test_tooling.py` | Added focused behavioral regressions for the defects below. Uses temporary output, a loopback server, mocked native calls and isolated shell installation paths. |
| `.github/workflows/ci.yml` | Existing build, cross-platform tests, samples, generated API/demo and strict-manual gates reviewed. Added tool regressions and the standalone callback proof. |
| `.github/workflows/notify-site.yml` | Fixed destination, least repository permissions, optional secret, quoting and curl failure propagation reviewed. No dispatch or credential access performed; unchanged. |
| `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `nuget.config`, `.editorconfig` | Warning/XML policy, central pins, SDK roll-forward, source isolation, publish stripping and formatting reviewed. No shared configuration changes required; ImageSharp remains 3.1.11. |

## Findings and corrections

**T01 — successful publish diagnostics were discarded.** The release wrapper printed only a
truncated tail on nonzero exit and printed nothing on zero exit. A linker/native-tool warning could
therefore disappear from the release evidence. The wrapper now emits complete stdout and stderr on
every exit. This preserves evidence; the release owner must still inspect the captured package
logs for the zero-warning requirement.

**T02 — a runnable broken published CLI passed demo verification.** An extraction error or missing
demo after exit zero fell back to the source archive, disguising a broken embedded resource or
single-file artifact. Such failures now stop packaging. The explicitly documented source fallback
remains only when the host cannot run the target at all. Linux binaries are no longer treated as
native executables on macOS. Version/RID arguments are validated before cleanup, and resolved
cleanup targets must remain inside this checkout's real `.release` directory, including symlink
resolution.

**T03 — local diagram rendering could write arbitrary paths.** The unauthenticated POST handler
used JSON keys in output filenames. A crafted key containing path segments escaped the diagram
folder. The handler now requires the render page's random token, bounds request size, accepts
exactly the light/dark keys, and validates both SVG roots before opening either output. A custom
header prevents a cross-origin form POST from acquiring write access. Existing rendering and
output names remain the same.

**T04 — capture errors could produce apparently successful stale figures or incorrect clicks.**
The pair wrapper ignored PowerShell exit codes, so existing `-a.png`/`-b.png` files could pass its
luminance test after capture failed. The wrapper now propagates failures and diagnostics. The shot
script verifies both dimensions, reports the requested size, refuses missing/unfocused windows,
and disposes bitmap/graphics after capture failures. The click script refuses to click after its
placement retries fail. `pair.py` now defaults to the documented 792-pixel window height; the direct
shot command retains its 950-pixel tall-page default.

**T05 — public record structs were documented as ordinary structs.** The reflection tool searched
only public methods for synthesized `PrintMembers`, but record structs synthesize a private
method. The detector now checks the exact private compiler-generated signature. The generated
declarations and assembly indexes consequently identify record structs correctly and omit their
incidental equality interface. API pages are regenerated by the tool, never hand-edited.

**T06 — missing font license still produced a distributable archive.** Strict MkDocs already failed
on the warning, but a non-strict build still wrote the zip without its required license. The hook
now checks the license before opening the archive and refuses that redistribution.

**T07 — Linux launcher installation corrupted paths containing reserved characters.** Interpolating
the extraction path into a sed replacement expanded `&` into the matched Exec line and interpreted
the delimiter. The installer now escapes the desktop command argument and replaces lines through
`printf`, keeping the path out of sed replacement syntax. Dollar/backtick/quote/backslash escaping
and literal percent handling follow the
[freedesktop Exec specification](https://specifications.freedesktop.org/desktop-entry/latest/exec-variables.html).
The actual installer is executed in a temporary directory containing `&`, `%` and `$`; its XDG
destination is another temporary directory.

**T08 — generic declaration snippets omitted required constraints.** The public surface includes
12 generic types with struct constraints, including `TargetedHistory<TKey, TPixel>` whose key is
`notnull`, the generic `UnsupportedSchemaVersionException.Require<T>` method constrained to
`ISchemaVersioned`, and `Footprint.Region<TPixel>` constrained to `struct`. A declaration without
those clauses omits restrictions that a consumer must
satisfy. A narrow reflection helper now appends each type's own and each method's generic clauses,
using CLR struct/class/default-constructor flags and base/interface constraint types. It avoids
printing the implied `ValueType` or `new()` for `struct`, and uses ordinal type ordering. `notnull`
uses the parameter's nullable metadata, falling back to the nearest declaring method/type context;
the existing key parameter inherits context 1 rather than carrying an explicit per-parameter
attribute. No general type-nullability renderer was redesigned. All existing public constraints
are checked against the actual Release surface; no current public `class`/`new()`-only or
`IPixel<T>` constraint was found in these three libraries.

**T09 — Linux package instructions omitted libraries named by the startup diagnostic.** Coordinated
with the Desktop reviewer after they verified the official distro package pages. The release
READ-ME's apt/dnf/pacman commands now include Xrandr, Xi and Xcursor packages alongside the existing
ICE, SM, X11 and fontconfig dependencies. The corresponding Desktop diagnostic and manual install
page are owned by the other reviewers; no package installation was performed here.

## Second pass: documentation and bounded simplification

Reviewed each script's invocation, output location and side effects against its module comments
and adjacent README. Updated the capture instructions to use `--no-embed`, the actual embedded
demo location, current default dimensions and failure behavior. Corrected the diagram module's
claim that it parses theme XAML: its palette mirrors the tokens and must be maintained with them.
Corrected API check-mode help to say generation happens in memory and the Ticket description to
name both `net10.0` and `netstandard2.1`. Removed only an unused renderer dictionary, an unused
import and the identical reflection branch. No framework extraction, dependency change, generated
art rewrite or unrelated cleanup was added.

## Verification

Focused tests first demonstrated the old failures: hidden successful diagnostics, source fallback
after a runnable CLI error, unauthorized/traversing diagram writes, invalid SVG acceptance,
successful stale capture, wrong-width capture, incorrect pointer input, unlicensed font zip and
corrupt Linux launcher Exec. The fixes then passed the same behavioral tests. The first
wrong-width baseline probe inadvertently reached the old script's actual screen-copy path and
created an untracked `unused.png`; that identified artifact was deleted, and the harness now
intercepts bitmap allocation before screen access and uses a temporary working directory.

Python AST parsing covers all 14 Python scripts. Both workflow files parse as YAML, and
`git diff --check` passes. PowerShell guard tests replace Win32 methods before executing the real
scripts. The release installer proof runs through Git Bash on this Windows host with a temporary
XDG destination. Symlink creation is unavailable on this Windows host; the dedicated symlink case
is skipped locally and runs on Linux CI. Foreign-root and child-escape checks run locally.

Final focused evidence:

- `dotnet build tools/api-docs/surface -c Release --nologo -v minimal`: succeeded, zero warnings,
  zero errors. Its referenced libraries built successfully in the same invocation.
- Executed the Release surface tool; saved its real JSON to the ignored
  `artifacts/audit/tooling/surface.json`. Regenerated all 269 pages from that dump. The resulting
  diff corrects 19 record-struct declarations, 12 generic type constraints, two generic method
  constraints and the assembly/root indexes; no prose was manually
  rewritten in generated pages.
- `python tools/api-docs/build.py --surface artifacts/audit/tooling/surface.json --check`: all 269
  pages current. The matching `--lint` invocation is clean.
- `NFTY_TEST_API_SURFACE=1 python -m unittest discover -s tools/tests -v`: 19 cases, 18 passed,
  one skipped because this Windows account cannot create directory symlinks. This includes real
  Release reflection execution and XML lint, actual isolated Bash installer execution, PowerShell
  guard execution, and loopback HTTP request/response tests.
- `python -m mkdocs build --strict`: succeeded. Material prints its existing upstream MkDocs 2
  migration notice; there are no strict-build documentation warnings. The generated font bundle
  contains all eight TTFs and OFL.txt, each byte-identical to the app's source files.
- All 14 Python scripts parse with `ast.parse`, both workflow files parse as YAML, and
  `git diff --check` is clean.

## Scope limits

The maintainer's GitHub-hosted jobs have not run in this local review. No site dispatch, publishing,
credentials, real Linux desktop installation, browser diagram rendering, fresh demo drawing or
native app capture was needed to validate these tool changes. Package creation and extracted-CLI
verification belong to the release owner after all source changes finish. Tar/zip package metadata
still contains build/filesystem timestamps; the product's seeded image/manifest determinism is a
separate existing guarantee. The diagram renderer still loads Mermaid from its existing CDN and
has a 180-second interactive rendering deadline. Those are visible developer-tool constraints,
not changes to asset generation.

The generic-constraint omission initially recorded for follow-up was corrected before completion
as T08. The renderer remains documentation tooling rather than a general C# decompiler; this review
verified every generic constraint actually present on the libraries' exported types and methods.
