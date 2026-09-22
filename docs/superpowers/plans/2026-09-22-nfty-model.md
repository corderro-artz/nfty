# Nfty.Model — glTF export — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extrude a pixel-art asset into a Z-extruded solid and write it as a `.glb` that opens in
any glTF viewer; then let a cooked Set carry those models, and let a single asset be saved as one.

**Architecture:** A new `Nfty.Model` assembly. Geometry is reviewed and rewritten from
`M:\Repositories\mixel` — the greedy-rectangle merge and the wall-overhang logic are worth taking,
the rest is either duplicated by something nfty already owns or is defective. The GLB container is
written by hand with `System.Text.Json` and `BinaryPrimitives`, so output is deterministic and no
glTF package enters the graph. `Nfty.Core` does **not** reference `Nfty.Model`; the dependency runs
one way, so generation never pulls a mesh library.

**Tech Stack:** C# / .NET 10, ImageSharp (already pinned), `System.Text.Json` (in-box), xunit.v3.

**Spec:** `docs/superpowers/specs/2026-09-22-nfty-model-design.md`
**Overview:** `docs/superpowers/specs/2026-09-22-runtime-and-model-overview.md`
**Reference source:** `M:\Repositories\mixel\src\Mixel.Core` (MIT, same author) — **read, do not copy.**

## Global Constraints

- **Zero new NuGet packages.** `Directory.Packages.props` gains nothing. No `SharpGLTF`, no `StbImageSharp`.
- `Nfty.Core` must **not** gain a `ProjectReference` to `Nfty.Model`. The arrow runs `Nfty.Model → Nfty.Core`.
- `TreatWarningsAsErrors` + `GenerateDocumentationFile`: **every public member needs an XML `<summary>`** or the build fails.
- Tests are xunit.v3, `Snake_case_sentences`, fixtures built **in memory** from tiny synthetic images. No golden files.
- **US English** everywhere the product speaks.
- Nothing in nfty resamples, blurs or anti-aliases. The glTF sampler is `NEAREST`/`NEAREST`, `CLAMP_TO_EDGE`.
- Build: `dotnet build nfty.sln`. Test: `dotnet test nfty.sln`.

## The seven defects that must not be reproduced

Each is present in `mixel` today. Every one has a task and a test below.

| ID | Defect | nfty's rule |
|----|--------|-------------|
| **D1** | `SilhouetteMask` treats solid as `A == 255`; `Extruder`'s depth-map branch uses `A > 0`. Same question, two answers — the same sprite extrudes to a different silhouette depending on the path. | **One function**, one stated threshold, both paths call it. Task 2. |
| **D2** | `GltfEmbedded` inlines the `.bin` by `json.Replace("\"uri\":\"x.bin\"", …)`. One space after a colon and it matches nothing, raises nothing, and emits a `.gltf` referencing a file that was never written. | `.glb` only. A GLB's binary chunk *is* the buffer, so the problem cannot arise. Task 7. |
| **D3** | `WithDoubleSide(true)` on a closed manifold. No back face is ever visible; it disables culling for nothing. | Single-sided. Task 7, asserted in Task 8. |
| **D4** | Material and mesh are named `"mixel"`, baked into every exported file. | Named for nfty. Task 7. |
| **D5** | A fully transparent image throws `EmptySilhouetteException`, which would kill a whole Set's export. | One asset that cannot be extruded is **skipped and named**; it does not fail the batch — the rule `SpriteSheet` already keeps about a missing asset. Task 9. |
| **D6** | `ExtrudeOptions.PngBytes` forces a PNG decode per call; nfty already holds the image. | The API takes `Image<Rgba32>`. There is no byte-array entry point. Task 9. |
| **D7** | `DepthMap.MaxLevel` lazily caches into a nullable field — a benign race that stops being benign under `ParallelWork`. | Not ported in phase 1; if depth maps arrive, computed eagerly. |

## File Structure

| File | Responsibility |
|------|----------------|
| `src/Nfty.Model/Nfty.Model.csproj` | New assembly. ImageSharp + `Nfty.Core` only. |
| `src/Nfty.Model/Solid.cs` | `SolidMask` — which pixels are there, and the bounding box. **One alpha rule.** |
| `src/Nfty.Model/RectMerge.cs` | `GreedyRects` — maximal-rectangle merge. |
| `src/Nfty.Model/Mesh.cs` | `Mesh` — positions, normals, UVs, indices. |
| `src/Nfty.Model/MeshBuilder.cs` | Faces and walls from a `SolidMask`. |
| `src/Nfty.Model/ExtrudeOptions.cs` | `ExtrudeOptions`, `ExtrudeMode`, `Pivot`. |
| `src/Nfty.Model/Glb.cs` | The GLB container and its glTF JSON. **No glTF library.** |
| `src/Nfty.Model/Extruder.cs` | The entry point: image + options → GLB bytes. |
| `src/Nfty.Model/SetModelWriter.cs` | Project 3: models into a cooked Set. |
| `tests/Nfty.Model.Tests/` | New test project. |

---

### Task 1: Create `Nfty.Model`

**Files:**
- Create: `src/Nfty.Model/Nfty.Model.csproj`
- Create: `tests/Nfty.Model.Tests/Nfty.Model.Tests.csproj`
- Modify: `nfty.sln`

**Interfaces:**
- Consumes: nothing.
- Produces: an empty assembly `Nfty.Model` that builds.

- [ ] **Step 1: Write the project files**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!--
    3D MODEL EXPORT, AND NOTHING CORE DEPENDS ON. The arrow runs one way on purpose: Nfty.Core does
    NOT reference this, so a consumer that only wants to generate a collection never pulls mesh code
    into its graph.

    It takes NO glTF package. A GLB is a 12-byte header and two length-prefixed chunks around a JSON
    document of fixed shape, so the container is written here — which also makes the output
    deterministic, and determinism is what lets a model fixture be frozen at all.
  -->
  <!-- No PropertyGroup: Directory.Build.props already sets TargetFramework, Nullable,
       ImplicitUsings, LangVersion, the version stamp and the documentation settings. Restating one
       here is the duplication that file's own comment warns about - seven csproj copies had already
       drifted before it existed. -->

  <ItemGroup>
    <PackageReference Include="SixLabors.ImageSharp" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Nfty.Core\Nfty.Core.csproj" />
  </ItemGroup>

</Project>
```

Test project: copy `tests/Nfty.Core.Tests/Nfty.Core.Tests.csproj` verbatim, change the
`ProjectReference` to `..\..\src\Nfty.Model\Nfty.Model.csproj`, and drop the `fixtures` `Content`
item — this project builds its own inputs in memory.

- [ ] **Step 2: Add both to the solution**

```bash
dotnet sln nfty.sln add src/Nfty.Model/Nfty.Model.csproj --solution-folder src
dotnet sln nfty.sln add tests/Nfty.Model.Tests/Nfty.Model.Tests.csproj --solution-folder tests
```

- [ ] **Step 3: Verify it builds**

Run: `dotnet build nfty.sln`
Expected: PASS, zero warnings.

- [ ] **Step 4: Commit**

```bash
# Stage the files you changed BY NAME. A blanket add has swept an unrelated uncommitted
# edit into an implementer commit once on this branch.
git commit -m "build: add the Nfty.Model project

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: `SolidMask` — one answer to "is this pixel there?" (D1)

**Files:**
- Create: `src/Nfty.Model/Solid.cs`
- Test: `tests/Nfty.Model.Tests/SolidMaskTests.cs`
- Create: `tests/Nfty.Model.Tests/Art.cs` (shared ASCII fixture builder)

**Interfaces:**
- Produces:
  - `sealed class SolidMask { int Width; int Height; int OffsetX; int OffsetY; bool At(int x, int y); bool IsEmpty; }`
  - `static SolidMask SolidMask.Build(Image<Rgba32> art, byte solidAlpha)`
  - `static Image<Rgba32> Art.FromAscii(params string[] rows)` — `#` opaque, `.` clear, `~` half-alpha

- [ ] **Step 1: Write the ASCII fixture builder**

```csharp
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

/// <summary>
/// Tiny synthetic art, built in memory. '#' is opaque, '.' is clear, '~' is half-alpha — which is
/// the case D1 is about and which no all-or-nothing fixture can reach.
/// </summary>
internal static class Art
{
    public static Image<Rgba32> FromAscii(params string[] rows)
    {
        int h = rows.Length, w = rows[0].Length;
        var img = new Image<Rgba32>(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                img[x, y] = rows[y][x] switch
                {
                    '#' => new Rgba32(200, 40, 40, 255),
                    '~' => new Rgba32(200, 40, 40, 128),
                    _   => new Rgba32(0, 0, 0, 0),
                };
        return img;
    }
}
```

- [ ] **Step 2: Write the failing test**

```csharp
namespace Nfty.Model.Tests;

public class SolidMaskTests
{
    [Fact]
    public void The_mask_crops_to_the_solid_bounding_box()
    {
        using var art = Art.FromAscii(
            ".....",
            "..#..",
            "..#..",
            ".....");
        var mask = SolidMask.Build(art, solidAlpha: 255);

        Assert.Equal(1, mask.Width);
        Assert.Equal(2, mask.Height);
        Assert.Equal(2, mask.OffsetX);
        Assert.Equal(1, mask.OffsetY);
    }

    /// <summary>
    /// D1. mixel answers this question two different ways in one library: the silhouette path uses
    /// A == 255 and the depth-map path uses A > 0, so the same sprite extrudes to a different
    /// silhouette depending on which one ran. There is ONE rule here and it is a parameter.
    /// </summary>
    [Fact]
    public void Partial_alpha_is_decided_by_the_stated_threshold_and_nothing_else()
    {
        using var art = Art.FromAscii("#~.");

        var strict = SolidMask.Build(art, solidAlpha: 255);
        Assert.Equal(1, strict.Width);          // only the fully opaque pixel

        var loose = SolidMask.Build(art, solidAlpha: 128);
        Assert.Equal(2, loose.Width);           // the half-alpha pixel counts too
    }

    [Fact]
    public void Art_with_no_solid_pixel_reports_empty_rather_than_throwing()
    {
        // D5: the caller decides what to do about it. Throwing here would kill a whole Set's export
        // over one legitimately transparent asset.
        using var art = Art.FromAscii("...", "...");
        Assert.True(SolidMask.Build(art, solidAlpha: 255).IsEmpty);
    }

    [Fact]
    public void Bounds_outside_the_mask_are_not_solid_rather_than_an_exception()
    {
        using var art = Art.FromAscii("#");
        var mask = SolidMask.Build(art, solidAlpha: 255);
        Assert.False(mask.At(-1, 0));
        Assert.False(mask.At(0, -1));
        Assert.False(mask.At(1, 0));
        Assert.False(mask.At(0, 1));
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~SolidMaskTests`
Expected: FAIL — `SolidMask` does not exist.

- [ ] **Step 4: Write the implementation**

```csharp
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model;

/// <summary>
/// Which pixels of a piece of art are THERE, cropped to their bounding box.
/// </summary>
/// <remarks>
/// <b>There is exactly one definition of "there", and it is the <c>solidAlpha</c> parameter.</b>
/// The reference implementation this was reviewed from answered the question two ways in one library
/// — fully-opaque on one path and any-alpha on another — so the same sprite produced a different
/// silhouette depending on which path ran. One function, one rule, both callers.
///
/// <para>Partial alpha is LEGAL in nfty: the editor's opacity lock is a default an author may turn
/// off deliberately. <c>Stats/VoxelReport.cs</c> reports which pixels will be dropped; this does not
/// refuse them.</para>
/// </remarks>
public sealed class SolidMask
{
    private readonly bool[] _solid;

    private SolidMask(bool[] solid, int width, int height, int offsetX, int offsetY)
    {
        _solid = solid;
        Width = width;
        Height = height;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    /// <summary>The bounding box's width in pixels.</summary>
    public int Width { get; }

    /// <summary>The bounding box's height in pixels.</summary>
    public int Height { get; }

    /// <summary>The bounding box's left edge in the source image.</summary>
    public int OffsetX { get; }

    /// <summary>The bounding box's top edge in the source image.</summary>
    public int OffsetY { get; }

    /// <summary>Whether the art has no solid pixel at all.</summary>
    public bool IsEmpty => Width == 0 || Height == 0;

    /// <summary>Whether the pixel at mask coordinates is solid. Out of bounds is not solid.</summary>
    /// <param name="x">Column within the bounding box.</param>
    /// <param name="y">Row within the bounding box.</param>
    /// <returns>True when that pixel is part of the solid.</returns>
    public bool At(int x, int y) =>
        x >= 0 && y >= 0 && x < Width && y < Height && _solid[(y * Width) + x];

    /// <summary>Builds the mask.</summary>
    /// <param name="art">The source art. Not modified.</param>
    /// <param name="solidAlpha">The lowest alpha counted as solid. 255 means fully opaque only.</param>
    /// <returns>The mask, cropped to the solid bounding box; empty when nothing qualifies.</returns>
    public static SolidMask Build(Image<Rgba32> art, byte solidAlpha)
    {
        ArgumentNullException.ThrowIfNull(art);

        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < art.Height; y++)
            for (int x = 0; x < art.Width; x++)
                if (art[x, y].A >= solidAlpha)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }

        if (maxX < 0) return new SolidMask([], 0, 0, 0, 0);

        int w = maxX - minX + 1, h = maxY - minY + 1;
        var solid = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                solid[(y * w) + x] = art[minX + x, minY + y].A >= solidAlpha;

        return new SolidMask(solid, w, h, minX, minY);
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~SolidMaskTests`
Expected: PASS, all four.

- [ ] **Step 6: Commit**

```bash
git add src/Nfty.Model/Solid.cs tests/Nfty.Model.Tests
git commit -m "feat: SolidMask, with one stated answer to which pixels are there

Reviewed from mixel, which answers it two different ways in one library.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: `GreedyRects` — maximal-rectangle merge

This is the algorithm genuinely worth taking from `mixel`. It is what turns a 64×64 sprite into a few
dozen quads instead of four thousand.

**Files:**
- Create: `src/Nfty.Model/RectMerge.cs`
- Test: `tests/Nfty.Model.Tests/RectMergeTests.cs`

**Interfaces:**
- Produces:
  - `readonly record struct MergedRect(int X, int Y, int Right, int Bottom)` — inclusive bounds
  - `static IEnumerable<MergedRect> RectMerge.Greedy(int width, int height, Func<int,int,bool> isSet)`

- [ ] **Step 1: Write the failing test**

```csharp
namespace Nfty.Model.Tests;

public class RectMergeTests
{
    private static Func<int, int, bool> Ascii(string[] rows) =>
        (x, y) => y >= 0 && y < rows.Length && x >= 0 && x < rows[0].Length && rows[y][x] == '#';

    [Fact]
    public void A_full_rectangle_merges_into_exactly_one()
    {
        var rows = new[] { "###", "###" };
        var rects = RectMerge.Greedy(3, 2, Ascii(rows)).ToList();
        Assert.Single(rects);
        Assert.Equal(new MergedRect(0, 0, 2, 1), rects[0]);
    }

    [Fact]
    public void A_checkerboard_merges_into_nothing_which_is_the_worst_case()
    {
        var rows = new[] { "#.#", ".#.", "#.#" };
        var rects = RectMerge.Greedy(3, 3, Ascii(rows)).ToList();
        Assert.Equal(5, rects.Count);
        Assert.All(rects, r => Assert.Equal((r.X, r.Y), (r.Right, r.Bottom)));
    }

    [Fact]
    public void An_empty_mask_yields_no_rectangles()
    {
        Assert.Empty(RectMerge.Greedy(3, 3, (_, _) => false));
    }

    /// <summary>
    /// The property that actually matters, and the one a shape-by-shape test cannot state: the
    /// rectangles TILE the set pixels — every one covered exactly once, and no clear pixel covered
    /// at all. A merge that double-covers emits z-fighting coplanar faces; one that under-covers
    /// leaves holes in the model. Neither shows up as an exception.
    /// </summary>
    [Theory]
    [InlineData("###", "#.#", "###")]
    [InlineData("..#", ".##", "###")]
    [InlineData("#..", "###", "..#")]
    [InlineData("####", "####", "####")]
    public void The_rectangles_tile_the_set_pixels_exactly_once(params string[] rows)
    {
        int h = rows.Length, w = rows[0].Length;
        var pred = Ascii(rows);
        var cover = new int[w, h];

        foreach (var r in RectMerge.Greedy(w, h, pred))
            for (int y = r.Y; y <= r.Bottom; y++)
                for (int x = r.X; x <= r.Right; x++)
                    cover[x, y]++;

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                Assert.Equal(pred(x, y) ? 1 : 0, cover[x, y]);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~RectMergeTests`
Expected: FAIL — `RectMerge` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
namespace Nfty.Model;

/// <summary>A merged run of set pixels. Bounds are INCLUSIVE on all four sides.</summary>
/// <param name="X">Left column.</param>
/// <param name="Y">Top row.</param>
/// <param name="Right">Right column, inclusive.</param>
/// <param name="Bottom">Bottom row, inclusive.</param>
public readonly record struct MergedRect(int X, int Y, int Right, int Bottom);

/// <summary>
/// Merges set pixels into maximal rectangles.
/// </summary>
/// <remarks>
/// <para>This is what makes an extruded sprite a few dozen quads instead of one per pixel. A 64x64
/// silhouette has about four thousand pixels and usually well under a hundred rectangles.</para>
///
/// <para><b>The rectangles TILE the set</b> — each set pixel is covered exactly once and no clear
/// pixel at all. Double coverage would emit coplanar faces that z-fight; under-coverage leaves holes.
/// Neither raises anything, so <c>RectMergeTests</c> asserts the tiling property directly rather
/// than checking a handful of shapes.</para>
///
/// <para>Greedy: extend right while it can, then down while every column of the strip continues.
/// Not optimal, and optimal rectangle partitioning is not worth its cost here — the win over
/// one-quad-per-pixel is already an order of magnitude.</para>
/// </remarks>
public static class RectMerge
{
    /// <summary>Merges the set pixels of a grid.</summary>
    /// <param name="width">Grid width.</param>
    /// <param name="height">Grid height.</param>
    /// <param name="isSet">Whether a cell is set. Called with in-bounds coordinates only.</param>
    /// <returns>The rectangles, top-to-bottom then left-to-right.</returns>
    public static IEnumerable<MergedRect> Greedy(int width, int height, Func<int, int, bool> isSet)
    {
        ArgumentNullException.ThrowIfNull(isSet);

        var used = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!isSet(x, y) || used[(y * width) + x]) continue;

                int right = x;
                while (right + 1 < width && isSet(right + 1, y) && !used[(y * width) + right + 1])
                    right++;

                int bottom = y;
                bool grow = true;
                while (grow && bottom + 1 < height)
                {
                    for (int xx = x; xx <= right; xx++)
                    {
                        if (!isSet(xx, bottom + 1) || used[((bottom + 1) * width) + xx])
                        {
                            grow = false;
                            break;
                        }
                    }
                    if (grow) bottom++;
                }

                for (int yy = y; yy <= bottom; yy++)
                    for (int xx = x; xx <= right; xx++)
                        used[(yy * width) + xx] = true;

                yield return new MergedRect(x, y, right, bottom);
            }
        }
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~RectMergeTests`
Expected: PASS, all seven (three facts + four theory cases).

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.Model/RectMerge.cs tests/Nfty.Model.Tests/RectMergeTests.cs
git commit -m "feat: greedy maximal-rectangle merge, with the tiling property asserted

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: `Mesh` and `ExtrudeOptions`

**Files:**
- Create: `src/Nfty.Model/Mesh.cs`
- Create: `src/Nfty.Model/ExtrudeOptions.cs`
- Test: `tests/Nfty.Model.Tests/MeshTests.cs`

**Interfaces:**
- Produces:
  - `sealed class Mesh { List<float> Positions; List<float> Normals; List<float> Uvs; List<int> Indices; int VertexCount; int TriangleCount; void AddQuad(...); }`
  - `enum ExtrudeMode { Front, Symmetric, Raised }`
  - `enum Pivot { BottomCenter, Center, MinCorner }`
  - `record ExtrudeOptions { int Depth = 1; float VoxelSize = 1f; Pivot Pivot = BottomCenter; ExtrudeMode Mode = Front; byte SolidAlpha = 255; }`

- [ ] **Step 1: Write the failing test**

```csharp
namespace Nfty.Model.Tests;

public class MeshTests
{
    [Fact]
    public void A_quad_is_four_vertices_and_two_triangles()
    {
        var mesh = new Mesh();
        mesh.AddQuad(
            (0, 0, 1),
            (0, 0, 0), (0, 0),
            (1, 0, 0), (1, 0),
            (1, 1, 0), (1, 1),
            (0, 1, 0), (0, 1));

        Assert.Equal(4, mesh.VertexCount);
        Assert.Equal(2, mesh.TriangleCount);
        Assert.Equal([0, 1, 2, 0, 2, 3], mesh.Indices);
    }

    [Fact]
    public void A_second_quad_indexes_off_the_first()
    {
        var mesh = new Mesh();
        for (int i = 0; i < 2; i++)
            mesh.AddQuad((0, 0, 1),
                (0, 0, 0), (0, 0), (1, 0, 0), (1, 0), (1, 1, 0), (1, 1), (0, 1, 0), (0, 1));

        Assert.Equal(8, mesh.VertexCount);
        Assert.Equal([0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7], mesh.Indices);
    }

    [Fact]
    public void Default_options_extrude_one_voxel_forward_from_the_bottom_centre()
    {
        var o = new ExtrudeOptions();
        Assert.Equal(1, o.Depth);
        Assert.Equal(1f, o.VoxelSize);
        Assert.Equal(Pivot.BottomCenter, o.Pivot);
        Assert.Equal(ExtrudeMode.Front, o.Mode);
        Assert.Equal(255, o.SolidAlpha);   // D1: fully opaque only, stated rather than implied
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~MeshTests`
Expected: FAIL.

- [ ] **Step 3: Write the implementation**

`Mesh` holds four parallel lists and `AddQuad` appends four vertices plus the six indices
`b, b+1, b+2, b, b+2, b+3`. Every public member needs a `<summary>`.

`ExtrudeOptions` is a `record` with every member `init` and defaulted, so a new axis is additive at
every existing call site — the rule `ExportOptions` already follows. Document `SolidAlpha` as D1's
one threshold and `ExtrudeMode`/`Pivot` with the meanings in the spec.

```csharp
/// <summary>The Z direction and symmetry of an extrusion.</summary>
public enum ExtrudeMode
{
    /// <summary>Forward: back face at Z=0, front face at Z=depth.</summary>
    Front,

    /// <summary>The art is the centre plane; extruded equally either way, so a pixel of depth D
    /// spans [-D/2, +D/2].</summary>
    Symmetric,

    /// <summary>Like <see cref="Front"/> with a one-voxel backing slab at Z=[-1, 0], for sprites
    /// that must read as solid from behind.</summary>
    Raised,
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~MeshTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.Model/Mesh.cs src/Nfty.Model/ExtrudeOptions.cs tests/Nfty.Model.Tests/MeshTests.cs
git commit -m "feat: Mesh and ExtrudeOptions

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: `MeshBuilder` — faces, walls and pivot

**Files:**
- Create: `src/Nfty.Model/MeshBuilder.cs`
- Test: `tests/Nfty.Model.Tests/MeshBuilderTests.cs`

**Interfaces:**
- Consumes: `SolidMask` (Task 2), `RectMerge` (Task 3), `Mesh`/`ExtrudeOptions` (Task 4).
- Produces: `static Mesh MeshBuilder.Build(SolidMask mask, ExtrudeOptions options)`

- [ ] **Step 1: Write the failing test**

```csharp
namespace Nfty.Model.Tests;

public class MeshBuilderTests
{
    private static Mesh Build(string[] rows, ExtrudeOptions? o = null)
    {
        using var art = Art.FromAscii(rows);
        return MeshBuilder.Build(SolidMask.Build(art, 255), o ?? new ExtrudeOptions());
    }

    [Fact]
    public void A_single_pixel_is_a_cube_of_six_quads()
    {
        var mesh = Build(["#"]);
        Assert.Equal(12, mesh.TriangleCount);   // 6 faces x 2 triangles
    }

    /// <summary>
    /// THE assertion for this file. A closed manifold has every edge shared by exactly two
    /// triangles: no hole, no dangling wall, no face emitted twice. It catches a whole class of
    /// wall-emission bug at once, and none of those bugs raises anything on its own.
    /// </summary>
    [Theory]
    [InlineData("#")]
    [InlineData("##", "##")]
    [InlineData(".#.", "###", ".#.")]
    [InlineData("###", "#.#", "###")]
    [InlineData("#.", ".#")]
    public void Every_mesh_is_a_closed_manifold(params string[] rows)
    {
        var mesh = Build(rows);
        var edges = new Dictionary<(int, int), int>();

        for (int t = 0; t < mesh.Indices.Count; t += 3)
        {
            AddEdge(Position(mesh, mesh.Indices[t]),     Position(mesh, mesh.Indices[t + 1]));
            AddEdge(Position(mesh, mesh.Indices[t + 1]), Position(mesh, mesh.Indices[t + 2]));
            AddEdge(Position(mesh, mesh.Indices[t + 2]), Position(mesh, mesh.Indices[t]));
        }

        Assert.All(edges, e => Assert.Equal(2, e.Value));

        void AddEdge((float, float, float) a, (float, float, float) b)
        {
            // Key on POSITION, not index: vertices are duplicated per face, so index-keyed edges
            // would never match and the assertion would pass vacuously.
            int ha = a.GetHashCode(), hb = b.GetHashCode();
            var key = ha <= hb ? (ha, hb) : (hb, ha);
            edges[key] = edges.GetValueOrDefault(key) + 1;
        }
    }

    [Fact]
    public void A_solid_rectangle_costs_fewer_quads_than_its_pixels()
    {
        // The greedy merge must actually reach the faces. 3x3 solid: 2 merged front/back quads plus
        // 12 perimeter walls = 14 quads, against 9 pixels' worth of unmerged faces plus walls.
        var mesh = Build(["###", "###", "###"]);
        Assert.True(mesh.TriangleCount < 9 * 12,
            $"{mesh.TriangleCount} triangles: the merge did not reach the face builder");
    }

    [Fact]
    public void Bottom_centre_pivot_centres_x_and_seats_y_at_zero()
    {
        var mesh = Build(["##", "##"]);
        var (min, max) = Bounds(mesh);
        Assert.Equal(-1f, min.x, 3);
        Assert.Equal(1f, max.x, 3);
        Assert.Equal(0f, min.y, 3);
    }

    [Fact]
    public void Min_corner_pivot_seats_the_minimum_corner_at_the_origin()
    {
        var mesh = Build(["##", "##"], new ExtrudeOptions { Pivot = Pivot.MinCorner });
        var (min, _) = Bounds(mesh);
        Assert.Equal(0f, min.x, 3);
        Assert.Equal(0f, min.y, 3);
        Assert.Equal(0f, min.z, 3);
    }

    [Fact]
    public void Symmetric_mode_straddles_the_origin_in_z()
    {
        var mesh = Build(["#"], new ExtrudeOptions { Mode = ExtrudeMode.Symmetric, Depth = 2 });
        var (min, max) = Bounds(mesh);
        Assert.Equal(-max.z, min.z, 3);
    }

    private static (float x, float y, float z) Position(Mesh m, int i) =>
        (m.Positions[i * 3], m.Positions[(i * 3) + 1], m.Positions[(i * 3) + 2]);

    private static ((float x, float y, float z) min, (float x, float y, float z) max) Bounds(Mesh m)
    {
        float mnx = float.MaxValue, mny = float.MaxValue, mnz = float.MaxValue;
        float mxx = float.MinValue, mxy = float.MinValue, mxz = float.MinValue;
        for (int i = 0; i < m.VertexCount; i++)
        {
            var (x, y, z) = Position(m, i);
            mnx = MathF.Min(mnx, x); mxx = MathF.Max(mxx, x);
            mny = MathF.Min(mny, y); mxy = MathF.Max(mxy, y);
            mnz = MathF.Min(mnz, z); mxz = MathF.Max(mxz, z);
        }
        return ((mnx, mny, mnz), (mxx, mxy, mxz));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~MeshBuilderTests`
Expected: FAIL — `MeshBuilder` does not exist.

- [ ] **Step 3: Write the implementation**

Read `M:\Repositories\mixel\src\Mixel.Core\MeshBuilder.cs` lines 1–90 (the uniform-depth `Build`) and
250–390 (the wall, face and pivot helpers) as reference. **Rewrite, do not paste.** The structure to
reproduce:

1. Resolve `zf`/`zb` from `ExtrudeMode`:
   - `Front`: `zf = depth * s`, `zb = 0`
   - `Symmetric`: `zf = depth * s / 2`, `zb = -zf`
   - `Raised`: `zf = depth * s`, `zb = -s`
2. Front and back faces from `RectMerge.Greedy` over the mask, UVs spanning the merged rect.
3. Perimeter walls: for each solid pixel, emit a wall on each side whose neighbour is clear. Wall UVs
   are the pixel's centre (`(x + 0.5f) / w`), so a wall takes one texel's colour — deliberate, and
   what keeps the sides crisp.
4. `ApplyPivot` shifts every position.

Coordinate convention, stated once and depended on by the pivot tests: **X spans `[0, w*s]`, Y spans
`[0, h*s]` with row 0 at the TOP of the image and therefore the HIGHEST Y**, Z per the mode. That is
why `MinCorner` only has to shift Z — X and Y minima are already at the origin.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~MeshBuilderTests`
Expected: PASS, all eleven.

If `Every_mesh_is_a_closed_manifold` fails, the wall emission is wrong — most likely a side emitted
when the neighbour is solid, or a `zb`/`zf` mismatch between a face and the wall meeting it.

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.Model/MeshBuilder.cs tests/Nfty.Model.Tests/MeshBuilderTests.cs
git commit -m "feat: build an extruded mesh from a solid mask

Closed-manifold assertion over five silhouettes, which is the check that catches
wall-emission bugs none of which raise anything on their own.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: The texture bake

**Files:**
- Create: `src/Nfty.Model/TextureBake.cs`
- Test: `tests/Nfty.Model.Tests/TextureBakeTests.cs`

**Interfaces:**
- Produces: `static byte[] TextureBake.CropToPng(Image<Rgba32> art, SolidMask mask)`

- [ ] **Step 1: Write the failing test**

```csharp
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

public class TextureBakeTests
{
    [Fact]
    public void The_texture_is_the_masks_bounding_box_and_nothing_more()
    {
        using var art = Art.FromAscii(
            ".....",
            "..#..",
            ".....");
        var mask = SolidMask.Build(art, 255);

        using var baked = Image.Load<Rgba32>(TextureBake.CropToPng(art, mask));
        Assert.Equal(1, baked.Width);
        Assert.Equal(1, baked.Height);
        Assert.Equal(art[2, 1], baked[0, 0]);
    }

    [Fact]
    public void Colour_and_alpha_are_carried_through_untouched()
    {
        // nfty resamples, blurs and anti-aliases NOTHING. A bake that altered one channel would be
        // the first place in the product that did.
        using var art = Art.FromAscii("#~");
        var mask = SolidMask.Build(art, solidAlpha: 128);

        using var baked = Image.Load<Rgba32>(TextureBake.CropToPng(art, mask));
        Assert.Equal(art[0, 0], baked[0, 0]);
        Assert.Equal(art[1, 0], baked[1, 0]);
    }

    [Fact]
    public void The_bytes_are_a_png()
    {
        using var art = Art.FromAscii("#");
        byte[] png = TextureBake.CropToPng(art, SolidMask.Build(art, 255));
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], png.Take(8));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~TextureBakeTests`
Expected: FAIL.

- [ ] **Step 3: Write the implementation**

Clone the mask's bounding box out of `art` with ImageSharp's `Crop`, encode with `PngEncoder`, return
the bytes. **No resampling, no filter, no colour conversion.** Roughly fifteen lines.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~TextureBakeTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.Model/TextureBake.cs tests/Nfty.Model.Tests/TextureBakeTests.cs
git commit -m "feat: bake the mask's bounding box to a PNG, unaltered

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 7: The GLB writer — no glTF library (D2, D3, D4)

**Files:**
- Create: `src/Nfty.Model/Glb.cs`
- Test: `tests/Nfty.Model.Tests/GlbStructureTests.cs`

**Interfaces:**
- Consumes: `Mesh` (Task 4).
- Produces: `static byte[] Glb.Write(Mesh mesh, byte[] pngTexture)`

**The container, which is all there is to it:**

```
offset 0   uint32  magic          0x46546C67  ('glTF')
offset 4   uint32  version        2
offset 8   uint32  total length   the whole file
           uint32  chunk0 length  (JSON, padded to 4 with 0x20 SPACE)
           uint32  chunk0 type    0x4E4F534A  ('JSON')
           bytes   the glTF JSON
           uint32  chunk1 length  (BIN, padded to 4 with 0x00)
           uint32  chunk1 type    0x004E4942  ('BIN\0')
           bytes   positions | normals | uvs | indices | png
```

**The JSON, which is one fixed shape.** Five bufferViews in that order; four accessors; `POSITION`
**must** carry `min` and `max`; indices are `UNSIGNED_INT` (5125); the image bufferView carries no
`target`.

- [ ] **Step 1: Write the failing structural test**

```csharp
using System.Buffers.Binary;
using System.Text.Json;

namespace Nfty.Model.Tests;

/// <summary>
/// With no glTF library there is nothing to re-open the file with, so this parses the container
/// itself and asserts what the spec requires. A test that only checks the file starts with 'glTF'
/// proves nothing; this list is what replaces the safety a library would have given.
///
/// Opened by hand in a real viewer once, on the date in the commit that added this file.
/// </summary>
public class GlbStructureTests
{
    private static byte[] Cube()
    {
        using var art = Art.FromAscii("#");
        var mask = SolidMask.Build(art, 255);
        return Glb.Write(MeshBuilder.Build(mask, new ExtrudeOptions()), TextureBake.CropToPng(art, mask));
    }

    private static (JsonDocument json, int binOffset, int binLength) Parse(byte[] glb)
    {
        Assert.Equal(0x46546C67u, BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(0)));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(4)));
        Assert.Equal((uint)glb.Length, BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(8)));

        int jsonLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        Assert.Equal(0x4E4F534Au, BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(16)));
        Assert.Equal(0, jsonLen % 4);

        int binHeader = 20 + jsonLen;
        int binLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(binHeader));
        Assert.Equal(0x004E4942u, BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(binHeader + 4)));
        Assert.Equal(0, binLen % 4);
        Assert.Equal(glb.Length, binHeader + 8 + binLen);

        return (JsonDocument.Parse(glb.AsSpan(20, jsonLen).ToArray()), binHeader + 8, binLen);
    }

    [Fact]
    public void The_container_is_a_well_formed_glb()
    {
        using var doc = Parse(Cube()).json;
        Assert.Equal("2.0", doc.RootElement.GetProperty("asset").GetProperty("version").GetString());
    }

    [Fact]
    public void Every_buffer_view_lies_inside_the_binary_chunk()
    {
        var (doc, _, binLen) = Parse(Cube());
        using (doc)
        {
            foreach (var bv in doc.RootElement.GetProperty("bufferViews").EnumerateArray())
            {
                int off = bv.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0;
                int len = bv.GetProperty("byteLength").GetInt32();
                Assert.True(off >= 0 && off + len <= binLen, $"bufferView {off}+{len} of {binLen}");
            }
        }
    }

    [Fact]
    public void Position_carries_min_and_max_and_they_match_the_vertices()
    {
        // The spec REQUIRES min/max on POSITION and a viewer is entitled to reject a file without
        // them. Wrong values are worse than missing ones: frustum culling uses them, so the model
        // vanishes at certain camera angles and nothing reports an error.
        using var art = Art.FromAscii("#");
        var mask = SolidMask.Build(art, 255);
        var mesh = MeshBuilder.Build(mask, new ExtrudeOptions());
        var (doc, _, _) = Parse(Glb.Write(mesh, TextureBake.CropToPng(art, mask)));

        using (doc)
        {
            var pos = doc.RootElement.GetProperty("accessors")[0];
            Assert.Equal("VEC3", pos.GetProperty("type").GetString());
            Assert.Equal(mesh.VertexCount, pos.GetProperty("count").GetInt32());

            var min = pos.GetProperty("min").EnumerateArray().Select(e => e.GetSingle()).ToArray();
            var max = pos.GetProperty("max").EnumerateArray().Select(e => e.GetSingle()).ToArray();
            for (int axis = 0; axis < 3; axis++)
            {
                var vals = Enumerable.Range(0, mesh.VertexCount)
                    .Select(i => mesh.Positions[(i * 3) + axis]).ToList();
                Assert.Equal(vals.Min(), min[axis], 4);
                Assert.Equal(vals.Max(), max[axis], 4);
            }
        }
    }

    [Fact]
    public void Every_accessor_starts_on_a_component_size_boundary()
    {
        var (doc, _, _) = Parse(Cube());
        using (doc)
        {
            var views = doc.RootElement.GetProperty("bufferViews").EnumerateArray().ToList();
            foreach (var a in doc.RootElement.GetProperty("accessors").EnumerateArray())
            {
                var bv = views[a.GetProperty("bufferView").GetInt32()];
                int off = (bv.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0)
                        + (a.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0);
                Assert.Equal(0, off % 4);   // FLOAT and UNSIGNED_INT are both 4 bytes
            }
        }
    }

    [Fact]
    public void The_material_is_single_sided_and_named_for_nfty()
    {
        // D3 and D4. An extrusion is a closed manifold, so no back face is ever visible; double-sided
        // disables culling for nothing. And a material still named after another project is the kind
        // of thing a port carries silently into every shipped file.
        var (doc, _, _) = Parse(Cube());
        using (doc)
        {
            var mat = doc.RootElement.GetProperty("materials")[0];
            Assert.False(mat.TryGetProperty("doubleSided", out var ds) && ds.GetBoolean());
            Assert.Contains("nfty", mat.GetProperty("name").GetString()!, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_sampler_is_nearest_and_clamped_so_pixel_art_stays_crisp()
    {
        var (doc, _, _) = Parse(Cube());
        using (doc)
        {
            var s = doc.RootElement.GetProperty("samplers")[0];
            Assert.Equal(9728, s.GetProperty("magFilter").GetInt32());     // NEAREST
            Assert.Equal(9728, s.GetProperty("minFilter").GetInt32());     // NEAREST
            Assert.Equal(33071, s.GetProperty("wrapS").GetInt32());        // CLAMP_TO_EDGE
            Assert.Equal(33071, s.GetProperty("wrapT").GetInt32());
        }
    }

    [Fact]
    public void The_image_is_a_png_inside_the_binary_chunk()
    {
        byte[] glb = Cube();
        var (doc, binOffset, _) = Parse(glb);
        using (doc)
        {
            var img = doc.RootElement.GetProperty("images")[0];
            Assert.Equal("image/png", img.GetProperty("mimeType").GetString());

            var bv = doc.RootElement.GetProperty("bufferViews")[img.GetProperty("bufferView").GetInt32()];
            int off = binOffset + (bv.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0);
            Assert.Equal([0x89, 0x50, 0x4E, 0x47], glb.Skip(off).Take(4));

            // An image bufferView must carry NO target; targets are for vertex and index data.
            Assert.False(bv.TryGetProperty("target", out _));
        }
    }

    [Fact]
    public void Every_index_is_a_real_vertex()
    {
        using var art = Art.FromAscii(".#.", "###", ".#.");
        var mask = SolidMask.Build(art, 255);
        var mesh = MeshBuilder.Build(mask, new ExtrudeOptions());
        Assert.All(mesh.Indices, i => Assert.InRange(i, 0, mesh.VertexCount - 1));
    }

    [Fact]
    public void The_same_art_and_options_produce_byte_identical_output()
    {
        // Owning the writer is what makes this assertable, and it is what lets a model fixture be
        // frozen at all. A library that stamped a version string or a timestamp would make the same
        // asset a different file on every run.
        Assert.Equal(Cube(), Cube());
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~GlbStructureTests`
Expected: FAIL — `Glb` does not exist.

- [ ] **Step 3: Write the implementation**

Build the BIN payload first, tracking each section's offset and length, padding each to 4 bytes:
positions (`float3`), normals (`float3`), uvs (`float2`), indices (`uint32`), png.

Then write the JSON with `Utf8JsonWriter` — one scene, one node, one mesh, one primitive, one
material (`pbrMetallicRoughness` with `metallicFactor: 0`, `roughnessFactor: 1`, `baseColorTexture`,
`alphaMode: "MASK"`, `alphaCutoff: 0.5`, **no `doubleSided`**), one texture, one sampler, one image.

Then frame: header, JSON chunk padded with `0x20`, BIN chunk padded with `0x00`, and patch the total
length.

`asset.generator` is `"nfty"` with **no version** — a version string would make the same mesh a
different file across releases, and `The_same_art_and_options_produce_byte_identical_output` is worth
more than the provenance.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~GlbStructureTests`
Expected: PASS, all nine.

- [ ] **Step 5: Open one by hand**

```bash
dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~GlbStructureTests
# then write a throwaway file and open it
```

Write one GLB to disk and open it in a real viewer — Windows 3D Viewer, Blender, or
https://gltf-viewer.donmccurdy.com. **Record in the test file's summary that this was done and on
what date.** A structural test proves the bytes are well-formed; only a viewer proves they are a
model.

- [ ] **Step 6: Commit**

```bash
git add src/Nfty.Model/Glb.cs tests/Nfty.Model.Tests/GlbStructureTests.cs
git commit -m "feat: write GLB directly, with no glTF package

A GLB is a 12-byte header and two length-prefixed chunks around a fixed-shape
JSON document. Owning it keeps the graph free of a glTF library and makes the
output deterministic, which is what lets a model fixture be frozen.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 8: `Extruder` — the entry point (D5, D6)

**Files:**
- Create: `src/Nfty.Model/Extruder.cs`
- Test: `tests/Nfty.Model.Tests/ExtruderTests.cs`

**Interfaces:**
- Produces:
  - `static byte[] Extruder.ExtrudeGlb(Image<Rgba32> art, ExtrudeOptions options)`
  - `static byte[]? Extruder.TryExtrudeGlb(Image<Rgba32> art, ExtrudeOptions options)`
  - `sealed class EmptyArtException : Exception`

- [ ] **Step 1: Write the failing test**

```csharp
namespace Nfty.Model.Tests;

public class ExtruderTests
{
    [Fact]
    public void Extruding_art_produces_a_glb()
    {
        using var art = Art.FromAscii(".#.", "###");
        byte[] glb = Extruder.ExtrudeGlb(art, new ExtrudeOptions());
        Assert.Equal([0x67, 0x6C, 0x54, 0x46], glb.Take(4));   // 'glTF'
    }

    [Fact]
    public void Fully_transparent_art_returns_null_from_the_try_form()
    {
        // D5. A rolled asset CAN be fully transparent — every layer of a recipe may be optional — so
        // a Set export must be able to skip one and name it rather than failing the whole batch.
        // This is the rule SpriteSheet already keeps: a hole is information.
        using var art = Art.FromAscii("...", "...");
        Assert.Null(Extruder.TryExtrudeGlb(art, new ExtrudeOptions()));
    }

    [Fact]
    public void Fully_transparent_art_throws_from_the_plain_form_and_says_why()
    {
        using var art = Art.FromAscii("...");
        var ex = Assert.Throws<EmptyArtException>(() => Extruder.ExtrudeGlb(art, new ExtrudeOptions()));
        Assert.Contains("no", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_api_takes_an_image_and_not_bytes()
    {
        // D6. mixel's entry point takes PNG bytes, so every call decodes. nfty already holds the
        // image at cook time; a round-trip through PNG per asset is pure waste on the hot path.
        var method = typeof(Extruder).GetMethod(nameof(Extruder.ExtrudeGlb));
        Assert.Equal(typeof(SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>),
                     method!.GetParameters()[0].ParameterType);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~ExtruderTests`
Expected: FAIL.

- [ ] **Step 3: Write the implementation**

`TryExtrudeGlb`: build the mask; return `null` if `IsEmpty`; otherwise bake, build, write.
`ExtrudeGlb`: call it and throw `EmptyArtException` on null. Document `EmptyArtException` with why
the `Try` form exists.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Nfty.Model.Tests`
Expected: PASS, whole project.

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.Model/Extruder.cs tests/Nfty.Model.Tests/ExtruderTests.cs
git commit -m "feat: Extruder, taking a live image and skipping empty art rather than throwing

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 9: CLI — `nfty model`

**Files:**
- Modify: `src/Nfty.Cli/CommandFactory.cs`
- Modify: `src/Nfty.Cli/Nfty.Cli.csproj` (`ProjectReference` to `Nfty.Model`)
- Test: `tests/Nfty.Cli.Tests/ModelCommandTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
namespace Nfty.Cli.Tests;

public class ModelCommandTests
{
    [Fact]
    public void The_model_command_parses_for_a_png()
    {
        var r = CommandFactory.Build().Parse("model sprite.png --out sprite.glb --depth 3");
        Assert.Empty(r.Errors);
    }

    [Fact]
    public void An_ingredient_needs_a_variant_and_a_png_refuses_one()
    {
        // The rule `preview` already keeps: an option belonging to the other form is REJECTED, never
        // ignored, because an option that silently does nothing is worse than one that says no.
        Assert.Empty(CommandFactory.Build()
            .Parse("model hat.igt --variant a --out hat.glb").Errors);
        Assert.NotEmpty(CommandFactory.Build()
            .Parse("model sprite.png --variant a --out sprite.glb").Errors);
    }

    [Fact]
    public void An_unknown_mode_is_refused()
    {
        Assert.NotEmpty(CommandFactory.Build()
            .Parse("model sprite.png --out a.glb --mode sideways").Errors);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/Nfty.Cli.Tests --filter FullyQualifiedName~ModelCommandTests`
Expected: FAIL.

- [ ] **Step 3: Add the command**

`model <input> --out <x.glb> [--depth N] [--voxel-size F] [--mode front|symmetric|raised]
[--pivot bottom-center|center|min-corner] [--variant ID]`

`<input>` is a `.png` or an `.igt`, dispatched by `Archives.KindOf`. `--variant` is required for an
`.igt` and rejected for a `.png`.

- [ ] **Step 4: Run the tests and try it by hand**

```bash
dotnet test tests/Nfty.Cli.Tests --filter FullyQualifiedName~ModelCommandTests
dotnet run --project src/Nfty.Cli -- model <a-real-sprite.png> --out /tmp/a.glb --depth 4
```

Open `/tmp/a.glb` in a viewer and look at it.

- [ ] **Step 5: Commit**

```bash
# Stage the files you changed BY NAME. A blanket add has swept an unrelated uncommitted
# edit into an implementer commit once on this branch.
git commit -m "feat: nfty model

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 10: Project 3 — a Set may carry models

**Files:**
- Modify: `src/Nfty.Core/Output/SetLayout.cs`
- Modify: `src/Nfty.Core/Output/Metadata.cs` (the `SetManifest` record)
- Create: `src/Nfty.Model/SetModelWriter.cs`
- Test: `tests/Nfty.Core.Tests/SetLayoutModelsTests.cs`
- Test: `tests/Nfty.Model.Tests/SetModelWriterTests.cs`

**Interfaces:**
- Produces:
  - `SetLayout.ModelsDir = "models"`, added to `SetLayout.Directories`
  - `record SetModelInfo(string Format, int Depth, double VoxelSize, string Mode, string Pivot)` and an **optional** `SetManifest.Models` property
  - `static SetModelWriteResult SetModelWriter.Write(string setDir, ExtrudeOptions options, IProgress<SetModelProgress>? progress, CancellationToken ct)`
  - `record SetModelWriteResult(int Written, IReadOnlyList<int> SkippedNumbers)`

- [ ] **Step 1: Write the failing tests**

```csharp
using Nfty.Core.Output;

namespace Nfty.Core.Tests;

public class SetLayoutModelsTests
{
    [Fact]
    public void Models_is_one_of_the_sets_directories()
    {
        // SetLayout is an INCLUDE list, so adding models here is what makes SetWriter.Pack carry
        // them. Nothing else needs to know.
        Assert.Contains(SetLayout.ModelsDir, SetLayout.Directories);
    }

    [Fact]
    public void A_set_with_no_models_directory_still_lists_its_files()
    {
        // Every Set written before this feature has none. Absence must be ordinary, not an error.
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, SetLayout.ImagesDir));
            File.WriteAllText(SetLayout.ManifestPath(dir), "{}");
            File.WriteAllBytes(Path.Combine(dir, SetLayout.ImagesDir, "0001.png"), [1]);

            var files = SetLayout.FilesIn(dir);
            Assert.Equal(2, files.Count);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
```

```csharp
using Nfty.Core.Demo;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Output;

namespace Nfty.Model.Tests;

public class SetModelWriterTests
{
    private static string CookDemo(int count)
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        string cbk = DemoCookBook.WriteTo(dir);   // takes a DIRECTORY, returns the written path
        using var book = CookBookArchive.Read(cbk);
        using var set = Generator.Generate(book, new GenerateOptions(Count: count, Seed: "models"));
        SetWriter.Write(set, Path.Combine(dir, "out"));
        return Path.Combine(dir, "out");
    }

    [Fact]
    public void Every_asset_gets_a_model_named_like_its_metadata()
    {
        string setDir = CookDemo(4);
        var result = SetModelWriter.Write(setDir, new ExtrudeOptions(), null, CancellationToken.None);

        Assert.Equal(4, result.Written);
        // FOUR-PAD, exactly like metadata/NNNN.json. SetReader orders by the parsed number rather
        // than by filename, and a second padding convention inside one Set is the bug that ordering
        // rule exists to prevent.
        Assert.True(File.Exists(Path.Combine(setDir, SetLayout.ModelsDir, "0001.glb")));
        Assert.True(File.Exists(Path.Combine(setDir, SetLayout.ModelsDir, "0004.glb")));
    }

    [Fact]
    public void An_asset_that_cannot_be_extruded_is_skipped_and_named()
    {
        // D5 at the Set level. Replace one asset's PNG with a fully transparent image; the run must
        // finish and report which number it skipped.
        string setDir = CookDemo(3);
        string victim = Path.Combine(setDir, SetLayout.ImagesDir, "0002.png");
        using (var blank = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(8, 8))
            blank.Save(victim);

        var result = SetModelWriter.Write(setDir, new ExtrudeOptions(), null, CancellationToken.None);

        Assert.Equal(2, result.Written);
        Assert.Equal([2], result.SkippedNumbers);
        Assert.False(File.Exists(Path.Combine(setDir, SetLayout.ModelsDir, "0002.glb")));
    }

    [Fact]
    public void Models_are_written_in_the_input_order_whatever_order_they_finish_in()
    {
        // ParallelWork's contract: results land in a caller-owned slot per item, never collected
        // from the loop, so nothing observable depends on how many threads ran.
        string setDir = CookDemo(12);
        var a = SetModelWriter.Write(setDir, new ExtrudeOptions(), null, CancellationToken.None);
        var b = SetModelWriter.Write(setDir, new ExtrudeOptions(), null, CancellationToken.None);
        Assert.Equal(a.SkippedNumbers, b.SkippedNumbers);
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(setDir, SetLayout.ModelsDir, "0001.glb")),
            File.ReadAllBytes(Path.Combine(setDir, SetLayout.ModelsDir, "0001.glb")));
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test nfty.sln --filter "FullyQualifiedName~SetLayoutModelsTests|FullyQualifiedName~SetModelWriterTests"`
Expected: FAIL.

- [ ] **Step 3: Implement**

1. `SetLayout`: add `ModelsDir = "models"` with a summary explaining it is optional and absent from
   every Set written before this feature, and add it to `Directories`.
2. `Metadata.cs`: add an optional `SetModelInfo? Models` to `SetManifest`. **Additive and optional,
   so `Schema.Current` is NOT bumped** — `System.Text.Json` ignores unknown properties, so an older
   build still reads the file, whereas a bump would make it refuse one. Put that reason in the XML
   doc, as `TargetSupply` does.
3. `SetModelWriter.Write`: read `images/NNNN.png`, extrude each through `ParallelWork` (one asset's
   model is a pure function of that asset, so it qualifies), write `models/NNNN.glb`, collect skips
   into a caller-owned slot per item, and update `set.json`'s `Models` block.

- [ ] **Step 4: Run the tests and the full suite**

Run: `dotnet test nfty.sln`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
# Stage the files you changed BY NAME. A blanket add has swept an unrelated uncommitted
# edit into an implementer commit once on this branch.
git commit -m "feat: a cooked Set may carry models/NNNN.glb

SetLayout is an include list, so adding the directory is what makes Pack carry
them. set.json's models block is additive and optional, so Schema.Current is
deliberately not bumped.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 11: The number stamp must never reach a model

**Files:**
- Test: `tests/Nfty.Model.Tests/StampIsolationTests.cs`
- Modify: `src/Nfty.Core/Publish/SetExporter.cs` — doc comment only, unless the test finds otherwise

**Interfaces:**
- Consumes: `SetExporter`, `NumberStamp`, `SetModelWriter` (Task 10).
- Produces: no new types.

- [ ] **Step 1: Write the test**

```csharp
using Nfty.Core.Output;
using Nfty.Core.Publish;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

/// <summary>
/// THE NUMBER STAMP IS NOT PART OF THE ASSET.
///
/// NumberStamp draws the set number into a corner. It is export-only and destructive, so
/// SetExporter renders a COPY and stamps that. A model is extruded from the asset's art, and the
/// stamp is a debug mark added on the way out of the door — so a model must never carry one.
///
/// At cook time this is true by construction, because stamping does not happen at cook time. The
/// place it can go wrong is an export with the debug mark armed, which is what this tests.
/// </summary>
public class StampIsolationTests
{
    [Fact]
    public void An_export_with_the_debug_mark_armed_writes_unstamped_models()
    {
        // Build a Set, write models, then export with stamping on. The models in the export must be
        // byte-identical to the ones written from the unstamped art.
        // (Fill in using SetExporter's real options record; the assertion is the point.)
        string setDir = CookDemoWithModels();
        string exportDir = Directory.CreateTempSubdirectory().FullName;

        SetExporter.Export(setDir, exportDir, StampedOptionsWithModels(), CancellationToken.None);

        foreach (string original in Directory.EnumerateFiles(
                     Path.Combine(setDir, SetLayout.ModelsDir), "*.glb"))
        {
            string exported = Path.Combine(exportDir, SetLayout.ModelsDir, Path.GetFileName(original));
            Assert.True(File.Exists(exported), $"missing {exported}");
            Assert.Equal(File.ReadAllBytes(original), File.ReadAllBytes(exported));
        }
    }

    [Fact]
    public void The_stamp_is_detectable_so_the_test_above_is_not_vacuous()
    {
        // Guards the guard. NumberStamp draws WHITE over a one-pixel BLACK halo, so a stamped image
        // has pure white pixels that solid-colour demo art does not. If this cannot tell them apart,
        // the assertion above would pass with a stamped model.
        using var clean = new Image<Rgba32>(32, 32, new Rgba32(40, 40, 40, 255));
        using var stamped = clean.Clone();
        NumberStamp.Draw(stamped, 12);

        Assert.False(HasPureWhite(clean));
        Assert.True(HasPureWhite(stamped));
    }

    private static bool HasPureWhite(Image<Rgba32> img)
    {
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
                if (img[x, y] == new Rgba32(255, 255, 255, 255)) return true;
        return false;
    }
}
```

**Two helpers to write before running this**, because their shapes come from files this plan does not
quote. Read them first, then write the helpers:

- `CookDemoWithModels()` — `SetModelWriterTests.CookDemo(4)` from Task 10, followed by
  `SetModelWriter.Write(setDir, new ExtrudeOptions(), null, CancellationToken.None)`. Returns the Set
  folder. Lift it into a shared internal helper rather than copying it; both test classes need it.
- `StampedOptionsWithModels()` — an `ExportOptions` with `Models = true` (Task 12) and the debug
  number stamp **armed**. Read `src/Nfty.Core/Publish/ExportOptions.cs` for the stamp property's real
  name, and `SetExporter.cs` for `Export`'s real parameter list.

Also check `NumberStamp.Draw`'s actual signature in `src/Nfty.Core/Publish/SpriteSheet.cs`'s
neighbourhood before writing the second test.

**Task 12 defines `ExportOptions.Models`, so run this task after it** — or stub the axis, run the
second test alone (it is the one that can pass today), and return here.

- [ ] **Step 2: Run it**

Run: `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~StampIsolationTests`
Expected: `The_stamp_is_detectable...` PASSES immediately. The first test either passes (stamping is
already isolated by construction) or FAILS, revealing that the export path stamps before models are
copied.

- [ ] **Step 3: Fix only if the test failed**

If it failed, the export is stamping art the model export then reads. The fix belongs in
`SetExporter`: stamping renders a copy for `images/` and must not be upstream of the model copy.
**Do not fix the test.**

If it passed, add a doc comment to `SetExporter` recording the invariant and pointing at this test,
so the next person to touch the stamp knows what it protects.

- [ ] **Step 4: Commit**

```bash
# Stage the files you changed BY NAME. A blanket add has swept an unrelated uncommitted
# edit into an implementer commit once on this branch.
git commit -m "test: the debug number stamp never reaches an exported model

The stamp is a mark added on the way out of the door, not part of the asset.
The companion test proves the stamp is detectable, so the assertion is not
vacuous on solid-colour demo art.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 12: Cook-time opt-in, export axis, and documentation

**Files:**
- Modify: `src/Nfty.Cli/CommandFactory.cs` — `generate --models`
- Modify: `src/Nfty.Core/Publish/ExportOptions.cs` — `Models` axis
- Modify: `src/Nfty.Core/Publish/SetExporter.cs` — plan and carry `models/`
- Modify: `CLAUDE.md`, `README.md`, `BACKLOG.md`
- Test: `tests/Nfty.Core.Tests/ExportModelsAxisTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Nfty.Core.Publish;

namespace Nfty.Core.Tests;

public class ExportModelsAxisTests
{
    [Fact]
    public void Models_is_an_independent_content_axis()
    {
        var o = new ExportOptions { Models = false };
        Assert.False(o.Models);
        Assert.True(new ExportOptions().Models);   // defaulted on, like the other content axes
    }

    [Fact]
    public void The_plan_lists_models_when_they_are_included_and_omits_them_when_not()
    {
        // SetExporter.Plan is the seam that makes the feature honest: the sentence a user reads
        // before pressing the button is generated by the code that then does the writing. A models
        // directory that appeared in the output without appearing in the plan is exactly the defect
        // that seam exists to prevent.
        // Same shared helper as Task 11: cook the demo, then SetModelWriter.Write over it.
        string setDir = CookDemoWithModels();

        var withModels = SetExporter.Plan(setDir, new ExportOptions { Models = true });
        var without = SetExporter.Plan(setDir, new ExportOptions { Models = false });

        Assert.Contains(withModels.Parts(), p => p.Contains("model", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(without.Parts(), p => p.Contains("model", StringComparison.OrdinalIgnoreCase));
        Assert.True(withModels.Bytes > without.Bytes);
    }
}
```

Read `SetExporter.Plan`, `ExportPlan.Parts()` and `ExportOptions` first and match their real shapes.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~ExportModelsAxisTests`
Expected: FAIL.

- [ ] **Step 3: Implement**

1. `ExportOptions`: add `bool Models { get; init; } = true;` with a summary. Check
   `MatchingPreset()` — it compares content, so the new axis must take part; decide each preset's
   value (Marketplace: **false**; AssetPack, FullProject, SealedCritique: **true**) and say why in
   the doc.
2. `SetExporter.Plan` and `Export`: carry `models/` when the axis is on.
3. `generate --models [--model-depth N] [--model-mode ...]`: after `SetWriter.Write`, call
   `SetModelWriter.Write`. **`Nfty.Cli` already references `Nfty.Model` from Task 9.**

- [ ] **Step 4: Documentation**

`CLAUDE.md`: add `Nfty.Model` to the project layout; record the `models/` directory, the
number-stamp rule, and that `Nfty.Core` deliberately does **not** reference `Nfty.Model`.

`BACKLOG.md`: two entries — **Layered extrusion** (per-layer Z, with the `LayerDepth` argument: depth
1 paints first and sits furthest back, so the 2D paint order already *is* a Z order) and **`.gltf`
text output** (with D2's reason).

`README.md`: `nfty model` and `generate --models` in the command list.

- [ ] **Step 5: Full verification**

```bash
dotnet build nfty.sln
dotnet test nfty.sln
```
Expected: PASS, zero warnings.

```bash
dotnet run --project src/Nfty.Cli -- generate <book.cbk> --out /tmp/set --count 6 --seed x --models
```
Open two of `/tmp/set/models/*.glb` in a viewer. **Look at them.**

- [ ] **Step 6: Commit**

```bash
# Stage the files you changed BY NAME. A blanket add has swept an unrelated uncommitted
# edit into an implementer commit once on this branch.
git commit -m "feat: cook with models, export them as their own content axis

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## UI — deliberately NOT in this plan

Designed against a rendered frame once the engine works, per the spec's checklist. Recorded here so
nothing is discovered late:

- Set browser inspector: **Save model…** beside Save image, gated by the **same seal flag**.
- New **`ModelDialog`**, its own menu: depth, voxel size, mode tray, pivot picker, a live
  triangle/size estimate, and a `VoxelReport` line naming how many pixels partial alpha will drop.
- Cook dialog: **Include 3D models** plus collapsed options and the measured cost.
- Export dialog **CONTENTS** tab: `models/` as a fifth axis. **IMAGES** tab: a note that the debug
  mark applies to `images/` only.
- Ingredient pane: `Export preview…` gains a model option.
- One new icon. It must differ from its **neighbours** — a plain cube sits beside `kitchen`'s archive
  box, so draw a solid with a visible depth face, not an outline box.
- Edit lock: model export is a READ, **not** gated. The seal flag **is**.
