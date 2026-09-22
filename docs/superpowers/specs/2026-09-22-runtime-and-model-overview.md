# Runtime drops and 3D models — scope, decomposition, sequencing

**Date:** 2026-09-22
**Status:** design approved in outline; two specs below carry the detail.

## The ask, in one paragraph

A game must be able to take a CookBook, ship it, and — at the moment a drop happens — produce an
asset *from the book* rather than from a folder of pre-cooked PNGs. Separately, nfty should be able
to hand a person a **3D model** of an asset as readily as it hands them a PNG, and a cooked Set
should be able to carry those models for anyone who wants a complete one.

## This is three projects, not one

They were described in one conversation and they are not one change. Two are fully independent of
each other; the third depends on the second.

| # | Project | Depends on | Spec |
|---|---------|-----------|------|
| 1 | **`Nfty.Ticket`** — the roll, extracted; the `.mnu` runtime Menu format; `nfty menu` / `nfty serve` | nothing | `2026-09-22-nfty-ticket-design.md` |
| 2 | **`Nfty.Model`** — PNG → Z-extruded glTF 2.0, ported from `mixel` | nothing | `2026-09-22-nfty-model-design.md` |
| 3 | **Models in a Set, and the UI for both** | project 2 | `2026-09-22-nfty-model-design.md` §"Project 3" |
| 4 | **In-app 3D preview** — a software rasterizer with orbit, pan and zoom | project 2, tasks 2–6 only | `2026-09-22-model-preview-design.md` |

Build order: **1 and 2 can proceed in parallel** (different files, different dependencies, no shared
seam). **4 follows project 2's geometry tasks and can overlap project 3.** **3 follows 2.** Each gets
its own branch, its own plan and its own merge.

Project 4 is the first 3D surface in the app, and it depends on project 2's *geometry* only — not on
its GLB writer, because the preview renders a `Mesh` and never parses a `.glb`. So it can start as
soon as `MeshBuilder` exists, well before models reach a Set.

## Why they are separate

Project 1 is about **deciding** an asset without drawing it. Its whole value is that no pixel is
touched, so its dependency budget is zero and its output is ~100 bytes.

Project 2 is about **drawing** an asset in a different dimension. It needs a glTF library, an image
library, and a mesh builder. Its output is megabytes.

The only place they will ever meet is a future in which a Menu carries per-layer depth so a runtime
drop can be *3D*. That is real and it is not in scope; it is recorded at the end of spec 2 so the
shape of project 2 does not accidentally foreclose it.

## What is deliberately not here

- **Unity.** Named in the original conversation and then explicitly dropped: *"we aren't worried
  about Unity yet. The idea is to get the file format and output working and renderable in any
  viewer."* Neither spec contains a Unity project, a shader, or a `.unitypackage`, and project 1
  targets `net10.0` alone — an earlier draft multi-targeted `netstandard2.1` for engine embedding and
  that is dropped, with the cost of adding it later written into `BACKLOG.md`. Project 2 emits glTF
  2.0, which Blender, Windows 3D Viewer, VS Code and three.js all open without help — which is what
  "renderable in any viewer" asks for.
- **Server-side economy work.** Commit–reveal fairness, DB schema, drop pricing. Project 1 makes
  these possible and does not do them.
- **Rarity/odds at runtime.** `SelectionOdds` and `UniqueSpace` stay in `Nfty.Core`. A client does
  not price a drop; the machine that rolled it does, and that machine can run Core.

## The dependency rule, stated once for both projects

**No new third-party package enters this product for either of these features.** The graph stays at
ImageSharp, System.CommandLine, Avalonia and the test packages.

That is a constraint the author set, and the two places it bites are both places where a package was
the obvious reach:

- **`Nfty.Ticket` reads its Menu with in-box `System.Text.Json`** — the same serializer
  `Formats/Json.cs` already uses — rather than taking a dependency or hand-rolling a parser.
- **`Nfty.Model` writes the GLB container itself** rather than taking `SharpGLTF`. A GLB is a
  12-byte header and two length-prefixed chunks around a JSON document of fixed shape; owning it also
  makes the output deterministic by construction, which is what lets a model fixture be frozen at
  all.

"Except where it makes most sense" is what ImageSharp already is: a real imaging library doing real
imaging work. A glTF library framing 300 bytes of header is not that.

## Naming decisions taken here

**`pack` was rejected as non-specific.** The command is named after the file it writes, the way every
other archive in this product is named after what it *is* rather than what was done to it.

| Term | Extension | What it is |
|------|-----------|-----------|
| **Menu** | `.mnu` | A CookBook flattened for runtime: every weight, rule and colorization, plus the art, and no authoring ability. What a game ships. |
| **Ticket** | *(no file)* | One roll. A recipe, a variant per layer, a colour per colorized layer, and the DNA. ~100 bytes on a wire, one row in a database. |

The pair is the kitchen's own: **the client holds the menu, the server sends a ticket, the client
cooks the dish.** It also gives the domain a noun for "one rolled asset", which it has never had —
today such a thing exists only inside the Set that holds it.

Commands: `nfty menu` writes one, `nfty serve` renders an asset from a Menu and a ticket. Model
export is `nfty model`.
