# Final integration verification

Date: 2026-11-08. This report joins the completed two-pass reviews of Ticket, Core, Model,
CLI, App, Desktop, both samples, and executable tooling. It supplements their individual finding
tables; those tables retain the original failed-before evidence and scope decisions.

## Remaining-stage result

- Desktop: W01–W08 corrected, R01 centered main/viewer startup implemented, D01–D08 XML groups
  corrected, S01–S05 simplifications completed. See [Desktop](06-desktop.md).
- Ticket sample: TS01–TS03 corrected; the example remains package-free and prepares its Menu once.
  See [Ticket sample](07-ticket-sample.md).
- Embedding sample: ES01–ES02 corrected; one prepared Menu and one continuing RNG stream handle
  bounded rule retries. See [embedding sample](08-embedding-sample.md).
- Tooling: T01–T09 completed, including public record-struct/generic-constraint documentation,
  release/capture failure propagation, loopback write guards, license enforcement and Linux
  launcher escaping. See [tooling](09-tooling.md).

## Final local gates

The forced non-incremental Release solution build succeeded with zero warnings and zero errors.
The six test targets contain 5,368 passing cases and one platform-specific skip: Ticket 235,
Ticket netstandard 235, Core 1,753, Model 674, CLI 411, App 2,060. The skipped secret-tool stdin
case requires Linux and is covered by the Linux CI job; it is not a Windows pass.

The first full run passed every product test but failed the terminology guard on new sample
documentation saying “unique assets.” That sentence now says “assets with distinct DNA.” A CLI
recheck exposed an audit-test mistake: it compared whole nested ZIPs, whose DOS timestamps can
change between culture iterations. The assertion now compares every ordinally named nested
payload using SHA-256, retaining exact manifest/image verification without asserting wall-clock
metadata equality. The corrected CLI suite passes all 411 cases. No production archive writer
was changed for this test correction. Failed attempts remain in the ignored evidence directory.

Fresh broad output comparison is **470/470 byte-identical** to the accepted App checkpoint.
This covers generation/extension, one/four threads, both Menu art modes, seeded Menu rendering
and models. Raw ZIP container timestamps are excluded; extracted entries are compared.

All **431 rendered frames** were compared with the App checkpoint: 423 exact, six random temporary
path labels, one in-flight loading frame and one ten-pixel single-level control-edge variation.
Every changed region was visually inspected; none indicates a layout or content regression.
The independent Desktop-focused comparison is 18/18 exact and includes both themes and actual
viewer-window states. These content captures do not prove native monitor placement.

The standalone forced-reset callback probe passes in both live-owner and disposed-owner modes:
the second isolated application's delegate enters with no failure. This exercises the actual
production callback, without retrying tests or weakening Headless isolation.

Tool regressions: 19 cases, 18 passed, one Windows symlink-permission skip. The enabled reflection
cases use the real Release public surface. All 269 generated API pages are current, XML lint is
clean, and the manual builds strictly. All 14 Python scripts and both workflow files parse.
Frozen fixtures, archived design documents and dependency pins remain unchanged.

## Independent model check

A synthetic asymmetric orientation/depth image was exported to GLB and glTF/bin and opened in
Don McCurdy's independent Three.js viewer. An existing Ingredient was also exported and opened.
Front/back orientation, face ownership, transparency and depth were visually inspected.
GLB and glTF/bin front captures are RGBA-identical. All three Khronos Validator reports contain
zero errors and zero warnings; their sole informational notice concerns an intentional non-power-
of-two face atlas using supported nearest/clamp sampling. Evidence and screenshots are retained in
`artifacts/audit/external-viewer/`. The corresponding external-viewer backlog check is complete.

## Publication boundary and limits

The release must be built afresh from the merged main commit. Earlier candidate packages predate
the final App/Desktop fixes and must not be published. Extracted packaged CLIs, cross-platform
payload parity, native startup and GitHub asset verification are separate release gates. Their
final outcome belongs in the release record and [after-action report](AFTER-ACTION.md).

Local checks cover Windows x64 and Linux x64/WSL where stated. They do not certify native macOS
Keychain behavior, every mixed-DPI monitor arrangement, live provider rollback, every CPU
architecture or every possible input. Deferred feature/performance work remains in BACKLOG.md;
it is not represented as completed fixes.
