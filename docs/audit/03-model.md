# Stage 3 — Nfty.Model audit

Status: both review passes complete; fixes subsequently approved, implemented and [verified](03-model-verification.md). The verification stop was observed, then the user approved proceeding on 2026-11-06. Review baseline: `24f2ed6`, locally tagged `audit-core-verified-2026-11-05`. No Model production code, committed tests, fixtures, dependencies or archived designs were changed during the review-only phase. Model implementation is locally tagged `audit-model-verified-2026-11-06`; nothing has been merged or pushed.

The second pass is a separate review of XML documentation and simplification, not an assumption that passing tests makes the code simple or its documentation accurate.

## Coverage and baseline

All 29 production C# files were reviewed, with relevant tests and App/CLI callers:

| Scope | Files | Contents |
|---|---:|---|
| Geometry and mesh | 12 | DepthFill, StackDepth, StackFaces, StackRise, StackPick, Riser, Mesh, MeshBuilder, FaceAtlas, GreedyRects, partial-alpha exceptions |
| Model formats | 9 | ModelWriter, ModelFile, ReadModel, UnsupportedModelException, GltfReader, GltfDocument, GltfBuilder, GltfJson, GlbContainer |
| Rendering | 4 | Camera, Raster, RenderTarget, Viewport |
| Batch/options/integration | 4 | ModelBatch, SetModeler, ModelTokens, RiseOptions |

- Fresh Release solution build before the checkpoint: zero warnings/errors.
- Fresh prebuilt Release Model suite: **500 passed**, no failures/skips.
- Focused rendering suite: 65 passed; overlaps the 500-test run.
- Independent geometry probes checked 1,200 random masks with full-byte depths: 1,186 nonempty meshes passed volume, outward winding and balanced-edge checks; 14 correctly returned empty.
- An independent stack oracle checked 100 stacks of 1–6 layers and 3,194 solid pixels, including depth maxima, upper-layer ties, face ownership, wall seams, atlas gutters and clipped atlas updates.
- Format probes checked a sample GLB across four cultures and a read/write round-trip; the sample bytes matched.

These checks support the ordinary geometry and output paths. The confirmed edge failures below are not covered by the existing passing suite.

## Pass 1 — confirmed findings

Eleven findings: **seven P2**, **four P3**. No P0/P1 finding was established. P2 covers incorrect results or damaged output at an existing boundary. P3 covers lower-impact format, naming or public-API contract edges. API-only and product-workflow cases are distinguished explicitly.

### M01 — P2: near-plane clipping leaves holes inside opaque faces

**Location:** `Render/Raster.cs:265–269,337–338`.

Rise fully opaque 16×16 paper art; use camera yaw 6°, pitch −1°, distance `1.2 × MinDistance`, and a cleared 128×128 target. Pixel `(23,76)` is transparent with infinite depth although its ray intersects the face well inside its outline. All eight neighboring pixels are opaque. Six repeated renders reproduced the same result.

Opposite traversal of a shared diagonal produces clipped screen endpoints at X `4678.8613` and `4678.861`. At the affected pixel center, one piece's edge evaluates −1.5 and the other evaluates zero but rejects it under the top-left rule. Both exclude the pixel.

**Proposed fix:** make shared clipping intersections and edge evaluation watertight, preferably through canonical endpoint ordering and consistent edge arithmetic. A bounded subpixel fixed-point approach is another option. An arbitrary epsilon can create overlapping coverage and is not sufficient proof.

**Verification/preservation:** test opaque near-clipped face coverage and shared edges over multiple orientations. Preserve exported mesh, UVs, DNA and model bytes; only erroneous preview coverage should change. Existing small unclipped top-left tests do not exercise this failure.

### M02 — P2: anchored zoom-in can move the camera outward and lose the anchor

**Location:** `Render/Camera.cs:213`, constructor clamp at `:87`.

Bounds `(-8,0,-1.5)..(8,16,1.5)`, centered target, yaw 70°, pitch 50°, distance `1.11 × MinDistance`; call `ZoomAnchored(.9f, (300,120), 400,300)`. Distance increases from **7.826837 to 7.923032**, and the anchor drifts **11.846564 pixels**. The corrected target remains strictly inside the bounds. Ordinary `Zoom(.9)` reaches 7.0512047.

The corrective pan changes the box exit distance; constructing the corrected camera raises its floor and pushes the eye outward. This is the actual App wheel path, not just an invalid argument.

**Proposed fix:** resolve the pan correction and requested distance constraint together, limiting correction/zoom where necessary. Preserve the target confinement and outside-eye guarantees; document any unavoidable constrained-anchor limitation.

**Verification/preservation:** assert both zoom direction and projected anchor near the floor at oblique angles. Ordinary camera behavior and model bytes should remain unchanged.

### M03 — P2: reader silently discards unsupported model semantics

**Location:** `GltfReader.cs:112–148,175–198`.

The structural guard does not inspect all represented glTF objects. Probes accept a black `baseColorFactor`, linear filtering, an empty scene and asset version `999`; another probe removes asset, scene, scenes, nodes, materials, textures and samplers together. The reader still returns the original mesh and texture.

These fields can change visible meaning. The glTF specification defines base-color factors as multiplying the texture. The correct behavior for nfty's intentionally narrow reader is refusal of unsupported semantics, not generic support or silent skipping. [Khronos glTF specification](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html).

**Proposed fix:** validate required fixed-subset structure, references and values throughout the represented objects, including nested PBR/texture objects. Keep optional names flexible and preserve all supported writer output.

**Verification:** mutate one semantic field/reference at a time and require `UnsupportedModelException`; retain supported GLB and both glTF round-trips.

### M04 — P2: texture validation accepts truncated PNGs and JPEGs labeled PNG

**Location:** `GltfReader.cs:148–160`.

`ImageFormats.Identify` supports PNG and JPEG and does not prove pixel decoding succeeds. A JPEG substituted under `image/png` is accepted as `TexturePng`. A real PNG truncated to its first **33 bytes** is also accepted; decoding the returned bytes then throws `InvalidImageContentException` outside the reader.

**Proposed fix:** verify actual PNG format and successful pixel decoding through the shared `ImageFormats` boundary, disposing the temporary decoded image while retaining original encoded bytes. Reframe failures as `UnsupportedModelException`.

**Caller impact:** standalone model opening misses its specialized unsupported-model handling and reaches an outer error card. The recorded Set fallback already catches codec exceptions, so no lost Set fallback or unhandled App crash is claimed.

### M05 — P2: buffer views are bounded against storage rather than declared buffer length

**Location:** `GltfReader.cs:134,224–230`.

Changing a valid model's `buffers[0].byteLength` to **0 or −1** still returns the original mesh. The reader checks actual bytes are at least the declared length, then checks views against all actual storage instead of the declared buffer. The specification requires a positive declared buffer length. [Khronos glTF specification](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html).

**Proposed fix:** validate the declared length and bound every view against it. Preserve legal GLB padding; extra physical bytes must not authorize an out-of-declaration view.

**Verification:** zero/negative lengths, a view one byte beyond declaration, truncation and valid padding. Valid writer output should remain byte-identical.

### M06 — P2: failed folder export can corrupt an existing glTF/BIN pair

**Location:** `ModelBatch.cs:208–212,322–326,445–450`.

Start with a valid `a.gltf`/`a.bin` pair. Hold the existing BIN open without write sharing and export a differently sized replacement model in `GltfWithBin` format. The batch overwrites `a.gltf`, then fails with `IOException` writing the BIN. The old BIN survives but the JSON has changed; `GltfReader.Read` now refuses the previously valid pair as having a buffer shorter than declared.

This is an I/O-failure issue, not a claim that the existing cooperative cancellation path interrupts each pair. The current cancellation tests stop between models and pass.

**Proposed fix:** treat each model's file set as a commit unit: stage its new files and preserve/restore the prior pair if replacement fails. Notify per-file callbacks after the model reaches a safe committed state. Keep already completed models in a canceled folder; do not replace the author's entire export directory.

**Verification:** locked companion BIN, failure during the second replacement, callback failure and normal replacement, in sequential/windowed exports. Preserve both prior files on failed replacement and ordinary output bytes on success.

### M07 — P2: an accepted public scale can collapse a solid mesh

**Location:** `Riser.cs:56`, `MeshBuilder.cs:119–121`; contract in `RiseOptions.cs:37`.

One opaque pixel with `VoxelSize=float.Epsilon` and `Pivot.Center` returns 12 triangles with every position collapsed to signed zero. Default `BottomCenter` also produces 12 degenerate triangles. The scale is positive and finite, satisfying the documented public contract.

**Scope:** public API. CLI/UI scale limits prevent this case; `0.001f` and `1000f` passed. Overflowing maximum-float depth transforms are already rejected by Mesh, so no second escaping-infinity defect is claimed.

**Proposed fix:** refuse nonrepresentable scale/transform combinations, or deliberately align the public scale range with the existing product range. Do not silently clamp or change ordinary coordinate arithmetic.

**Verification:** paper/max-depth columns, all pivots, smallest positive float and neighboring values, product endpoints and ordinary model byte identity.

### M08 — P3: malformed GLB framing is accepted

**Location:** `GltfReader.cs:85–101`.

Appending one byte to a valid GLB and increasing its total-length header is accepted, leaving an incomplete trailing chunk header. Parsing lacks a final exact-consumption check; alignment and JSON-before-BIN arrangement also require explicit validation.

**Proposed fix:** validate complete consumed framing, ordered/aligned supported chunks and total lengths. Preserve every current writer output and any deliberately supported optional-chunk policy.

### M09 — P3: writer accepts unsafe names that its own reader refuses

**Location:** `ModelWriter.cs:51–55,83–87`.

Both public writer entry points accept `a:b`, `a?b`, embedded NUL and `CON`. Separate-BIN output for `a:b` contains `a%3Ab.bin`, which the reader refuses. Other accepted names cannot be normal Windows output filenames.

**Scope:** direct public API. Ordinary ModelBatch and generated Set names are sanitized; no generated Set regression was established.

**Proposed fix:** reject unsafe basenames before encoding, with one shared deterministic validator. Do not silently rename a caller's requested public-writer filename. Retain URI escaping for valid names.

### M10 — P3: batch sanitizer misses reserved superscript device names

**Location:** `ModelBatch.cs:69–77`.

`SafeBaseName("COM¹")`, `SafeBaseName("LPT²")` and `SafeBaseName("COM³.txt")` return those names unchanged. Windows reserves these superscript-digit COM/LPT device names, including names followed by extensions. The API claims every input is made safe across platforms. [Microsoft filename rules](https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file).

**Proposed fix:** cover the documented superscript variants in the existing device-name rule. Keep ordinary sanitization and collision suffixes unchanged.

**Verification:** sanitizer/build filename results are enough; do not open or write a potential device during tests. The review deliberately performed no device I/O.

### M11 — P3: narrow-aspect framing exceeds its documented fit guarantee

**Location:** `Render/Camera.cs:230–247`.

For the M02 bounds, aspect `.05`, target 50×1000, `FramedOn` clamps at `20R` and a front corner reaches NDC X **1.9386468**, outside the viewport. The public contract accepts positive finite aspect and promises margin; normal product panes are unaffected by this reproduction.

**Proposed policy:** qualify framing as best effort under `MaxDistance`—the smallest change—or intentionally redesign distance/clip limits for arbitrarily narrow viewports. Do not change the separately documented `20R` limit incidentally.

## Pass 2 — XML completeness and correctness

All reviewed public members have summaries; current generated API lint is clean. These are semantic corrections and ownership clarifications. Regenerate generated API pages after approved XML edits.

| ID | Location | Required correction |
|---|---|---|
| D01 | `StackRise.cs:48` | `Art` is the finished 2D composite, not necessarily the actual front surface; a lower protruding layer can own `Faces.Front`. |
| D02 | `StackDepth.cs:240` | `ForAsset` returns an owning disposable StackRise; explicitly assign disposal to its caller. |
| D03 | `StackDepth.cs:182–194` | Document forwarded ArgumentException cases for empty selection, size mismatch and finished solid-mask disagreement. |
| D04 | `PartialAlphaBookException.cs:6–7` | Explain whole-book upfront refusal without claiming every possible asset selects the offending variant. |
| D05 | `StackFaces.cs:12,73` | Clear non-solid pixels are a Combine guarantee; Uniform clones supplied alpha without equivalent validation. |
| D06 | `Riser.cs:49`, `RiseOptions.cs:37` | Align the scale contract with the selected M07 policy. |
| D07 | `GltfReader.cs:24–27` | Distinguish unsupported/malformed-content errors from ordinary file/permission failures that propagate. |
| D08 | `UnsupportedModelException.cs:3–11` | Describe malformed JSON, truncation and invalid structure as well as unsupported features. |
| D09 | `ModelWriter.cs:31–40,67–77` | Writing may compact vertex numbering; only Format is consumed from options because the existing mesh already carries pivot/scale. |
| D10 | `ModelFile.cs:6`, `ReadModel.cs:5` | Payload arrays remain mutable and constructors retain references; no disposable decoded image is returned. |
| D11 | `Render/RenderTarget.cs:5–8` | Construction leaves zero colors and zero depth. Callers must Clear before rendering; do not promise an initialized infinite depth buffer. |
| D12 | `Render/Camera.cs:165–180` | Anchor is a current-distance pick-ray heuristic, not a mesh-surface pick. Include floor constraints alongside target-boundary constraints. |
| D13 | `Render/Camera.cs:249–251` | WithSubject may reduce distance to the new maximum as well as raise it to a floor. |
| D14 | `Render/Camera.cs:230–236` | Qualify FramedOn's fit promise under MaxDistance as decided in M11. |
| D15 | `Render/Camera.cs:68–76` | Universal validity wording needs finite-input preconditions; constructor does not reject NaN/infinity. No ordinary product-path failure was established. |
| D16 | `ModelBatch.cs:257–259` | A sequential degree has the same bytes/order/calling thread, but currently releases inputs before writes, unlike the other overload. Do not promise identical callback behavior. |
| D17 | `SetModeler.cs:16–29` | State that the modeler retains borrowed recipe/art/depth references: the book must remain live and stable while model calls run. Read-only use is not an ownership snapshot. |

D16 was reproduced: ordinary sequential callback order is `open,written,release` with the file present at release; explicit `Parallelism.Sequential` gives `open,release,written`, with no file present at release. Either document that difference or deliberately delegate the sequential degree to the original overload; do not change lifetime timing accidentally during refactoring.

## Pass 2 — bounded simplification proposals

| ID | Location | Proposal and preservation proof |
|---|---|---|
| S01 | `ModelWriter.cs:51–55,83–87` | Share the duplicated basename validation while fixing M09. Preserve valid names and error timing before encoding. |
| S02 | `GltfReader.cs:33,75` | The path overload already owns a new byte array; avoid passing it through the span overload's second whole-file copy. Keep span ownership behavior and guarded error framing unchanged. |
| S03 | `MeshBuilder.cs:127–128` | Use ordinary List.ToArray snapshots instead of CollectionsMarshal.AsSpan(list).ToArray for normals, UVs and indices; remove the specialized import. Compare arrays and complete model bytes. |
| S04 | `DemoModelsTests.cs:121,146–151,175–183,258,267` | Cache each unchanged StackRise.Depth snapshot or use DepthAt. The getter clones a whole map; repeated per-pixel access duplicates megabytes of arrays without strengthening assertions. |
| S05 | `SetModeler.cs:42–48`, `ModelBatch.cs:117–129` | Delegate model filename enumeration to the existing FileNamesFor helper instead of maintaining a second switch. Preserve format/order and document any invalid-argument behavior change. |
| S06 | `ModelTokens.cs:35–44` | Consider direct table loops for NameOf, matching the reverse lookup methods and removing unnecessary LINQ iterator machinery. No new registry, reflection or generic lookup framework is needed. |
| S07 | `ModelBatch.cs:189–194,284–289,228–230,337–339` | Share duplicated name/order preparation and skip-report ordering. Keep the distinct sequential/windowed execution paths; merging their lifetimes into a generic callback pipeline would increase complexity. |

Two optional measurement-dependent candidates remain proposals, not automatic work:

- `StackDepth.cs:72`: when finished art is supplied, validated binary-alpha layers could be checked by their solid-mask union rather than constructing a full temporary color composite. Preserve validation/refusal order and face semantics; measure before approval.
- `Render/Raster.cs`, DrawLine: off-screen samples are rejected after iterating the projected line. A restricted original DDA index interval might bound work while retaining phase, but no measured regression or safe pixel-parity proof was established.

No independent renderer restructuring is justified. Keep immutable Camera, centralized viewport/projection transforms, pooled scratch cleanup and deterministic single-threaded coverage. A canonical clip helper belongs to M01's correctness fix.

## Deferred decisions and verification limits

- GPU acceleration, greedy wall emission, wall UV changes, incremental mesh rebuilding and generic glTF support remain deliberate backlog decisions.
- The backlog's missing external-viewer confirmation remains a process gap, not a newly demonstrated mesh defect. This review did not install or drive another viewer.
- Ordinary mesh topology/depth/atlas probes passed; underflow is explicitly public-API-only. Do not generalize it into a claim that normal product scale geometry is broken.
- No failed Set fallback, unhandled App crash, arbitrary file traversal or cross-architecture discrepancy was demonstrated.
- Runtime probes used current Release DLL references in isolated projects; they did not rebuild or modify shared production outputs.
- Context7 was not exposed in the available tools. Format/name requirements were checked against primary Khronos/Microsoft documentation.

## Evidence and next boundary

Ignored local evidence:

- `artifacts/audit/model-review-tests.log`
- `artifacts/audit/model-formats-probe/{Program.cs,results.txt}`
- `artifacts/audit/model-render-probe/{Program.cs,evidence.log,hole.png}`
- `artifacts/audit/model-batch-probe/{Program.cs,results.txt}`
- Geometry probe in `%TEMP%/nfty-model-geometry-probe/{Program.cs,results.txt}`

Historical review checkpoint: stop for user approval of M01–M10, D01–D17 and bounded simplifications, with an explicit M11 framing policy. The user subsequently approved implementation. M01–M11, D01–D17 and S01–S07 are now complete; M11 preserves the distance cap and documents best-effort fitting. The two unmeasured candidates remain deferred. See the [implementation checklist](03-model-implementation.md) and [final verification report](03-model-verification.md). Work is stopped for user verification before CLI.
