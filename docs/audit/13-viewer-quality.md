# Viewer quality: lighting, smoothness and anti-aliasing

Measured on Windows on November 9, 2026. The changes below keep the existing CPU renderer and
its projection, depth, clipping, coverage, nearest-neighbor texture sampling and export paths.
Anti-aliasing is assessed only; no anti-aliasing implementation or dependency was added.

## Implemented lighting

The shared `PreviewLightingView` provides one **Light** button in Rise's canvas readout footer
and in the pop-out viewer's 3D footer group. Its flyout provides an integer intensity slider and
number field from 0 through 100, an RGB color field and swatch, and **Reset**. Opening the flyout
does not resize the surface or rearrange the surrounding controls. The viewport stays clear.

- The default is 100% white. The original four-argument `Raster.Render` overload still exists
  and delegates to this default. The new overload accepts immutable `PreviewLighting` state.
- Intensity scales the existing shaded RGB channels. Zero gives a black solid; light color's
  alpha is ignored. Texture alpha, transparent background and z-buffer values are preserved.
- A partial or invalid `#RRGGBB` field keeps the last accepted light and swatch. Intensity is
  clamped by the preview VM. Reset restores 100% white without changing the camera, edge
  overlay or turntable. The edge overlay retains its contrasting ink at every light setting.
- These controls change the preview and a saved viewer **Still** PNG. They do not recolor the
  source texture, paint depth, modify DNA, change an archive, or write lighting into a model.
  Model-export and generation code was not changed.

The flyout has an explicit VM assignment because it is a separate Avalonia tree. The real
Reset button was verified by focusing it and sending Enter; raising a synthetic Click event
alone does not exercise Button's command invocation. Editable controls retain their own keys.
The viewer's Still source is `EncodeStill`, which flushes a pending repaint before encoding
with the current lighting. The immediate light-change/Still test pumps no dispatcher between
the change and capture, then requires Reset to reproduce the original PNG bytes exactly.

## Implemented scheduling and raster work

The existing bitmap reuse, pooled render buffers and one-pending-frame coalescing remain in
place. A drag or host gesture now has a 1/60-second repaint budget instead of 33 ms, and the
turntable uses that interval too. This is a scheduling ceiling, not a promise of 60 displayed
frames per second. Drawing still happens on the UI thread; a large or contended frame takes
as long as it takes. Gesture release still flushes the final exact frame.

`Raster.Render` now derives canonical edge equations and UV sample bounds only after a fan
triangle survives projected backface culling. The equations, inputs, clipping and pixel loop
retain the same arithmetic. Mesh-constructor validation and Raster's null/texture-size checks
remain before the relevant traversal, including for wholly culled or offscreen geometry.

### Paired measurements

The comparison used the same new lighting implementation in both configurations. The before
configuration temporarily restored only pre-cull derived setup and the old 33 ms schedule.
The after configuration used deferred setup and the new interval. Exact backups, a `finally`
restore and SHA-256 checks confirmed both source files were restored byte-for-byte afterward.
Each measurement ran in a fresh Release test process. There are no timing pass/fail thresholds.

The real shared control ran its actual dispatcher turntable for about one second per case,
including rasterization and bitmap copying:

| Source / viewport | Observed cadence before → after | Mean repaint before → after | Mean raster before → after |
|---|---:|---:|---:|
| 64 / 256 px | 21.98 → 46.67 fps | 2.242 → 2.076 ms | 2.072 → 1.934 ms |
| 512 / 512 px | 24.82 → 49.36 fps | 8.778 → 7.573 ms | 8.407 → 7.281 ms |

These are headless dispatcher observations on this machine, not display refresh measurements
or an FPS guarantee. The higher cadence deliberately draws more frames: scoped total repaint
time during those approximately one-second windows rose from 49 to 98 ms and from 219 to
379 ms respectively, while mean work per frame fell. Idle previews still have no turntable
timer and do not redraw continuously.

A separate 30-camera-frame orbit measurement included target clearing and varied source and
target sizes. The dense disc has 24,720 triangles; the smaller disc has 2,972:

| Source / target | Mean before → after | Median before → after | p95 before → after |
|---|---:|---:|---:|
| 64 / 256 px | 4.324 → 4.540 ms | 3.834 → 4.358 ms | 6.443 → 6.755 ms |
| 512 / 512 px | 12.085 → 10.064 ms | 8.316 → 6.171 ms | 40.601 → 35.967 ms |
| 512 / 1024 px | 12.938 → 11.226 ms | 13.437 → 11.757 ms | 17.497 → 14.438 ms |

The small isolated case was slightly slower in this run; the dense cases improved. Short
samples, tiered JIT warmup and unrelated machine load affect these numbers, especially the
512 px p95 outliers. They support the narrower claim that the measured dense workload costs
less and the real control produced more frames. They do not establish a universal speedup.

## Pixel and UI evidence

The supplied Rise image was stale manual artwork. The current source already had centered
canvas placement and grid/zoom controls in the footer; these were not new fixes in this batch.
The four Rise/viewer manual PNGs were refreshed from the real production windows, shared
resources/services and embedded Chest Demo after moving Light into those existing footers.

Layout proof compares fresh pre-extraction frames with final frames, at the same real window
sizes and with the same demo/pose, in both themes. All four frames have **zero differing pixels
outside the old overlay and new footer Light rectangles plus the separately integrated
Settings/help footer-icon rectangles**. There are no path-text differences. The latter two
rectangles are only in Rise's shell; the viewer needs only the Light rectangles excluded.
The Rise 3D viewport remains 118 × 118 DIP and its footer remains 48 device pixels tall; the
viewer viewport remains 382 × 547 DIP and its footer remains 49.2 device pixels tall at the
captured 1.2 scale. The centered Rise canvas has center deltas of 0 and −0.5 device-independent
pixels. The obsolete manual images were not used as the geometry baseline.

Final footer-focused verification passed **93 App tests**, including both themes at minimum
and wrapped viewer sizes, actual slider/hex/reset bindings, keyboard focus and screenshot
pixel-diffs outside the model pane. Final model pixel/edge correctness passed **26 tests**.
The following earlier measurements are retained as historical evidence and overlap those
final groups; counts must not be added together.

The first full Windows run identified two real integration gaps: the source audit did not
recognize Button's declarative flyout action, and the extra footer width hid one common info
fact at the minimum viewer size. The audit now recognizes a declared flyout and the real
Light button is tested with Enter. The existing info-row assertion remains unchanged.

Measured minimum geometry explains the final compact trigger. Existing groups and gaps use
702 DIP, and the new wrap slot adds a 14 DIP gap. The default 55 DIP trigger required 771 DIP
and forced a second row. Even 39 DIP hit a measure/arrange rounding boundary: 754.33 DIP is
available during measure, then 755 during arrange. The final trigger gives up only its own
horizontal padding, measures 37 DIP and keeps the original font, complete label and named
automation target. The strip now uses 753 DIP. With the trigger present and with only its slot
removed, the minimum footer is **41 DIP**, the existing groups retain identical bounds and
`RowsBelow` is zero. The trigger is a separate wrap slot beside the unchanged 3D group.

The final corrected focused group passed **21 tests**, including both themes at 100%, 120%
and 200% layout scale, minimum/wrapped sizes, visible opaque models, label/hit bounds,
positive changed pixels inside the model pane, exact unchanged pixels outside it, and Reset
restoring the entire baseline frame exactly. An earlier transparent-model diagnostic did not
prove a visible lighting change and is explicitly superseded, not counted as positive proof.

Scaled headless capture has an initial repaint-cache detail: a no-op redraw alone changed
one channel by one level on one clipped pane-edge column (622 pixels at 200%, 372 at 120%).
The recorded transforms, frame dimensions and paired no-op PNGs establish this independently
of lighting. The test compares equivalent, settled repainted frames and still requires exact
outside-pane equality, positive inside-pane changes and exact Reset. It adds no channel
tolerance, expands no mask and changes no renderer math. Raw default-render hashes remain
the original strict values below.

The original pre-edit baseline was captured before production changes. All 30-frame color
digests in each of the three paired cases match that baseline exactly:

| Source / target | SHA-256 over the 30 rendered color buffers |
|---|---|
| 64 / 256 px | `A1E37C47FC91727613D94DA09FB0CEA5F322A739DB02BA47708E7702CB4D7505` |
| 512 / 512 px | `94CDA5F5110E6FB7DC1A562EF47897F7560E90E597EF6451A94417F1100E704E` |
| 512 / 1024 px | `1BDC2C3987BB052112F81FC598C1929B1DB8E8B9C58DFDB7E3FA494D75232B8E` |

Focused verification at this worktree:

- **73 App tests passed**, covering the shared preview, gestures, viewer layout, actual Reset
  input and both-theme screenshot pixel-diffs. The screenshot comparison requires every
  pixel outside the model's alpha mask to remain identical after changing lighting.
- **57 raster/model audit tests passed** in the earlier focused run, including canonical
  shared edges, greedy T-junctions, near clipping, texture boundaries, depth and orbit probes.
- **8 final lighting model tests passed**, covering default parity, zero/half/full intensity,
  alpha/depth/source-texture preservation, invalid intensity and texture validation on culled
  and offscreen meshes. These overlap the preceding model run; the counts are not additive.
- The paired profiler passed all three orbit and both actual-control cadence cases in each
  configuration. API generation wrote **270 pages**; separate `--check` and `--lint` runs passed.
- Light and dark shared-surface/flyout captures were inspected, along with the actual viewer
  at its minimum size. The final shared light control belongs in each host's footer; the
  earlier viewport-overlay captures are superseded. The centered model capture keeps its
  original full-frame bounds assertion, with no control-mask exception.

Raw logs, source-restore hashes and PNG frames are local ignored evidence under
`artifacts/preview-lighting/`. Reproduce the measurements with `PreviewOrbitPerfTests` and
`PreviewCadencePerfTests`, using Release and the detailed console logger. Capture the new
states by setting `NFTY_CAPTURE=1`, `NFTY_CAPTURE_DIR=<directory>` and selecting
`PreviewLightingTests`.

### Final combined gates

These gates use main baseline `d6147c6b219d4e5d080a1812bff41a83d652bc40` (including the
verified Settings/footer-icon changes) plus this complete preview diff:

| Gate | Result |
|---|---|
| Windows full solution Release build | 0 warnings, 0 errors |
| Windows full App tests | 2,132 passed, 3 platform skips, 0 failed |
| Linux full solution Release build | 0 warnings, 0 errors |
| Linux full App tests | 2,133 passed, 2 platform skips, 0 failed |
| Linux full Model tests | 679 passed, 6 Windows file-lock skips, 0 failed |
| API reference | 270 pages up to date; separate documentation lint clean |
| Manual | Strict MkDocs build passed; all four published PNG bytes match built-site copies |

Linux ran in a separate `/tmp/nfty-preview-*` source copy. SHA-256 validation matched all
1,656 working files, including untracked new source and the refreshed screenshots, before
and after the gates; all five fixture files were unchanged. Windows `bin` and `obj` outputs
were excluded from that copy. The original three default-render digests still match all
90 frames, and the final `Raster.cs` SHA is unchanged from that strict comparison.

Earlier overlay captures, the transparent-model diagnostic and the first failed full Windows
attempt are retained as clearly superseded investigation evidence. The final opaque-model
tests require visible inside-pane changes and exact Reset; none of the unsuccessful attempts
is presented as final passing coverage. Evidence is preserved with a SHA-256 inventory at
`artifacts/audit/preview-lighting/` in the integrating checkout. Commit, merge, packaging and
publication remain the integrating coordinator's responsibility.

## Anti-aliasing: assessed, not implemented

The current rasterizer samples one pixel center, and the image display remains nearest
neighbor. Smoothing the model's polygon silhouette is a different operation from filtering
its pixel-art texture. A future option can retain nearest-neighbor texel sampling while
accumulating multiple geometry-coverage samples. Khronos describes per-sample coverage,
color and depth plus resolution to a display pixel in [OpenGL 4.6, section 14.3.1, printed
page 466 / PDF page 487](https://registry.khronos.org/OpenGL/specs/gl/glspec46.core.pdf#page=487);
texture minification and magnification are separate operations in sections 8.14 and 8.15.

| Possible approach | Work and tradeoff for this renderer |
|---|---|
| Supersample at twice each dimension | Reuse the existing rasterizer at four times the pixel area, then resolve. RGBA plus float depth is 8 bytes per pixel: a 1024² target grows from 8 MiB to 32 MiB at 2048², before the display bitmap. Vertex/setup cost does not quadruple, so CPU cost must be measured rather than assumed. |
| Multisample geometry coverage | Add multiple coverage/depth samples and a resolve to `RenderTarget` and the triangle loop. More invasive than supersampling; shared-edge ownership, clipping and transparent-background behavior need new exact tests. |
| Postprocess edge filter | Less sample storage, but can soften intentional texture boundaries and the edge overlay. Requires an explicit quality decision and comparisons against real pixel art. |
| GPU renderer | A separate architecture and verification project already deferred in `BACKLOG.md`; outside this change. |

For transparent PNG stills, a future resolve must handle color and alpha together. Using
premultiplied color while averaging avoids mixing hidden transparent RGB into visible edges;
this is an implementation inference supported by [W3C's explanation of interpolation with
alpha](https://www.w3.org/TR/css-color/#interpolation-alpha), not a feature of the current code.

An optional idle-only supersampled frame is a plausible next experiment: keep motion cheap,
then refine after interaction. Before implementing it, decide the visual result, whether
stills use that quality, high-DPI/large-window memory limits and how the default remains
pixel-identical. No quality toggle, filtering change or AA behavior is shipped by this work.

## GPU decision

The author's current decision is to retain CPU rendering: identical images and views matter
more than adding a second rendering path. GPU work is deferred, with no implementation
authorized in this batch.

GPU rendering is technically possible. [Three.js's `WebGLRenderer`](https://threejs.org/docs/pages/WebGLRenderer)
uses WebGL 2. For this existing C#/Avalonia application, the native
[`OpenGlControlBase`](https://docs.avaloniaui.net/api/avalonia/opengl/controls/openglcontrolbase)
already provides OpenGL context/surface lifecycle hooks, so that would be worth assessing
before introducing an embedded browser and JavaScript renderer. This is an architectural
assessment, not a selected or implemented design.

If that decision is revisited, keep the CPU implementation as the reference and fallback.
GPU/driver rasterization and shading can differ at individual pixels, so a future GPU view
would require explicit cross-backend visual acceptance criteria rather than an assumption of
byte-identical frames. Generation, exported textures/models and their deterministic bytes
are a separate contract: choosing a faster preview must not alter them.
