# Rise step 5 — the `Nfty.Model` engine — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A new `Nfty.Model` assembly turns one finished pixel-art image plus its two-sided per-pixel depth into one closed solid, writes it as GLB, glTF + `.bin` or glTF with an embedded buffer, and `nfty model` exposes it on the command line.

**Architecture:** `Riser` builds a `Mesh` (positions, normals, UVs, `uint` indices) from an `Image<Rgba32>` and a `DepthMap`: faces are greedy-merged rectangles of equal `front` (and, separately, equal `back`), walls are each column's z-interval minus its neighbor's. `StackDepth.Combine` is the one stack rule (composite + topmost-solid-layer depth). `ModelWriter` builds ONE internal glTF record document and serializes it three ways, the three differing only in `buffers[0].uri`, set in the object model. No glTF package; the container is written with `System.Text.Json` and `BinaryPrimitives`.

**Tech Stack:** .NET 10, C#, ImageSharp 3.1.11 (already pinned, reached transitively through `Nfty.Core`), `System.Text.Json` (in-box), System.CommandLine 2.0.10, xunit.v3.

**Spec:** `docs/superpowers/specs/2026-09-25-rise-design.md` — binding. Read "The depth model", "Where depth lives", "`Nfty.Model` — the engine" (Surviving / Changed / Surface / Geometry), "CLI" and "Testing" before any task. Secondary: `docs/superpowers/specs/2026-09-22-nfty-model-design.md`, for the parts the new spec says survive (glTF compliance points, defects D1 and D3–D7, `GreedyRects`, vertex dedup, the structure validator, byte-identical output). Where the two conflict, the 2026-09-25 spec wins. `docs/superpowers/plans/2026-09-22-nfty-model.md` is **superseded** by this plan and was mined, not reused.

## Global Constraints

- **Zero new NuGet packages.** `Directory.Packages.props` does not change. No `SharpGLTF`, no `StbImageSharp`. `Nfty.Model.csproj` has a `ProjectReference` to `Nfty.Core` and **no** `PackageReference`.
- **`Nfty.Core` and `Nfty.Ticket` do not reference `Nfty.Model`.** The dependency runs `Nfty.Model → Nfty.Core → Nfty.Ticket` only. Nothing in `src/Nfty.Ticket` changes in this plan.
- `TreatWarningsAsErrors` + `GenerateDocumentationFile`: every public member carries an XML `<summary>` (and `<param>`/`<returns>` where it has them). A warning fails the build — test projects included (the xUnit analyzers run there too: never `Assert.Equal(0, x.Count)` / `Assert.Equal(1, x.Count)`, use `Assert.Empty` / `Assert.Single`).
- US English everywhere the product speaks — code, comments, messages, docs (`neighbor`, `color`, `center`).
- Never `git add -A` / `git add .` — add files **by path**, exactly the ones the task lists.
- Tests: xunit.v3, named `Snake_case_sentences`, fixtures built **in memory** from tiny synthetic images; no golden files; filesystem tests use `Directory.CreateTempSubdirectory()` and delete it in `finally`.
- Every sort that can reach output uses `StringComparer.Ordinal`; every number that reaches a file or a message a machine compares is formatted with `CultureInfo.InvariantCulture`.
- **Byte-identical output for identical input.** Same art + same depth + same options ⇒ same `Mesh` arrays and same file bytes, every run. No timestamps, no version strings, no hash-ordered collections anywhere on the output path.
- A fix is shown **RED** before it is made **GREEN**; state the red run in the task report.
- One solid test: `Opacity.IsSolid` / `Opacity.IsPartial` (`src/Nfty.Core/Imaging/Opacity.cs`). Nothing in `Nfty.Model` compares `A` to a number itself.
- Full suite: `dotnet test nfty.sln` — baseline before this plan **2418 passing**.
- Commit trailer, on every commit: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Rulings (interpretations of the spec, stated once)

1. **`DepthMap` is reused, not re-declared.** It already exists as `Nfty.Core.Model.DepthMap` in the `Nfty.Ticket` assembly (step 4) with `Width`, `Height`, `IsFlat`, `Flat`, `FromBytes`, `Front`, `Back`, `ToBytes`, `With`. The spec's Surface lists `DepthMap` under `Nfty.Model`; that line is satisfied by the existing type (a `sealed class`, not a record — nothing needs value equality). A second type would be two answers to one layout.
2. **Namespace `Nfty.Model` for the new assembly, no clash.** `DepthMap` and the manifests live in namespace `Nfty.Core.Model`, not `Nfty.Model`, so the two never collide. Checked: no file in `src/Nfty.Cli` or `tests/Nfty.Cli.Tests` (the only assemblies that gain a reference) uses a qualified `Model.X`, and no type name in `Nfty.Model` repeats one in `Nfty.Core.*`. Internal glTF records live in `Nfty.Model.Gltf`.
3. **`Riser.TryBuild` takes an optional fourth parameter, `string source = "the art"`.** The spec requires the partial-alpha refusal to name the source, and its three-parameter signature has no name to give. The parameter is additive; the three-argument call in the spec compiles unchanged.
4. **The refusal is `PartialAlphaException : InvalidOperationException`** carrying `ItemName`, `X`, `Y`, `Alpha`, `Count` (`Source` is taken by `System.Exception`). It is thrown **before** the empty check, so an image of only partial pixels is refused, not skipped.
5. **"Closed manifold" is asserted as the property that is actually true of the geometry.** Greedy-merged faces meet per-pixel walls at **T-junctions**, so "every edge shared by exactly two triangles" is false even for a 2×1 paper strip (worked example in Task 3). And pixels touching only at a corner — the checkerboard, or any two diagonal columns whose shared z-range both side-neighbors miss — **pinch**: four faces share one edge line. The anchor test therefore (a) refines every axis-aligned triangle edge into unit lattice segments and requires every directed segment to be walked exactly as often as its reverse, (b) requires the enclosed volume to equal `Σ (1 + front + back)` exactly, and (c) requires every stored normal to equal its triangle's winding normal. The strict "each directed unit segment exactly once, each way" is asserted additionally wherever no pinch exists (every shape but the checkerboard, under uniform depth).
6. **Pivot = a named point of the mesh's bounding box moved to the origin**, computed in voxel units, then everything is scaled by `VoxelSize`. `MinCorner` = the box's minimum corner; `Center` = its center; `BottomCenter` = center in X and Z, minimum in Y. With two-sided depth this centers the *solid*, not the paper plane.
7. **The texture is the whole art**, not cropped: UVs map the full image, re-encoded as an 8-bit RGBA PNG with metadata skipped, so a source decoded from a palette PNG or one carrying text/resolution chunks gives the same model bytes as the same pixels built in memory. `Mesh` records `TextureWidth`/`TextureHeight`, and `ModelWriter` refuses a texture of another size.
8. **Walls are not merged:** one quad per pixel edge per exposed segment. **Vertex rule:** a vertex is shared only by the two triangles of its own quad (four vertices, six indices per quad, so `VertexCount == 2 × TriangleCount`); no cross-quad dedup, because normals and UVs differ per face. Whether a `(position, normal, uv)`-keyed dedup is worth adding is decided by the `Perf` table `RisePerfTests` prints (Task 6), not assumed.
9. **`GreedyRects` merges a keyed grid in one pass** (cells merge only with equal keys; negative = unset), instead of one pass per depth value — the same maximal-rectangle algorithm, generalized, so faces cost one scan however many depth levels a sprite uses.
10. **Fill-all is `DepthFill.All(art, front, back)` in `Nfty.Model`**; `Nfty.Ticket` stays untouched. It takes no stored map because fill-all replaces the value on **every** solid pixel — "applied on top of the stored map" and "replaces the stored map" are the same result. The CLI requires `--front` and `--back` **together** (either alone is refused, never defaulted).
11. **`nfty model` dispatch:** a `.png` is recognized by extension first (a picture is not an archive kind, so `Archives.KindOf` would refuse it); everything else goes through `Archives.TryKindOf`, and only `ArchiveKind.Ingredient` is accepted. The action's backstop mirrors `preview`'s.
12. **The `.igt` form rises the variant's stored pixels as they are.** For a Dynamic/Static layer that is its grayscale value-map. The spec's CLI has no `--color`, and "Rise paints no color"; colorized models are what a cooked asset gives (`generate --models`, step 8).
13. **`--out`'s extension must match `--format`** (`.glb` for `glb`; `.gltf` for `gltf` and `gltf-embedded`), refused otherwise. The `.bin` is written beside the `.gltf` with the same base name; its `uri` is percent-escaped (`Uri.EscapeDataString`), the file name is not.
14. **All three formats serialize with one `JsonSerializerOptions`** (compact, nulls omitted, relaxed escaping so a base64 `+` stays `+`); `asset.generator` is `"nfty"` with no version, so a release bump cannot change a model's bytes.
15. **Emission order is fixed** so bytes are reproducible: front faces in `GreedyRects` discovery order, then back faces, then walls in row-major pixel order, sides Left, Right, Top, Bottom, the back segment before the front one.
16. **CI's cross-platform job gains `tests/Nfty.Model.Tests`**: byte-identical models are promised across machines exactly as generation is.
17. **Out of scope here, by the spec's build sequence:** `generate --models`, `models/` in a Set, the number-stamp test, the `nfty-grid` chunk, Make voxel-ready (all step 8); the rasterizer (step 6); the Rise page (step 7).

## File Structure

| File | Responsibility |
|------|----------------|
| `src/Nfty.Model/Nfty.Model.csproj` | the assembly: `ProjectReference` Nfty.Core, no packages, `InternalsVisibleTo` the tests |
| `src/Nfty.Model/RiseOptions.cs` | `RiseOptions`, `ModelFormat`, `Pivot` |
| `src/Nfty.Model/ModelFile.cs` | `ModelFile(FileName, Bytes)` |
| `src/Nfty.Model/Mesh.cs` | the immutable mesh the writers and (step 6) the preview consume |
| `src/Nfty.Model/GreedyRects.cs` | internal: maximal-rectangle merge over a keyed grid |
| `src/Nfty.Model/MeshBuilder.cs` | internal: quad emission, winding, UVs, pivot |
| `src/Nfty.Model/Riser.cs` | public entry: image + depth → `Mesh?` |
| `src/Nfty.Model/PartialAlphaException.cs` | the refusal, naming the item |
| `src/Nfty.Model/StackDepth.cs` | the stack rule |
| `src/Nfty.Model/DepthFill.cs` | fill-all |
| `src/Nfty.Model/Gltf/GltfDocument.cs` | internal: the glTF JSON object model (records) |
| `src/Nfty.Model/Gltf/GltfBuilder.cs` | internal: mesh + PNG → document + BIN bytes |
| `src/Nfty.Model/Gltf/GlbContainer.cs` | internal: GLB framing |
| `src/Nfty.Model/Gltf/GltfJson.cs` | internal: the one serializer configuration |
| `src/Nfty.Model/ModelWriter.cs` | public entry: mesh + texture → files |
| `src/Nfty.Cli/CommandFactory.Model.cs` | `nfty model` |
| `tests/Nfty.Model.Tests/*` | new test project (files listed per task) |
| `tests/Nfty.Cli.Tests/ModelCommandTests.cs` | the command surface |

---

### Task 1: The `Nfty.Model` project, `RiseOptions`, `ModelFile`, `Mesh`

**Files:**
- Create: `src/Nfty.Model/Nfty.Model.csproj`
- Create: `src/Nfty.Model/RiseOptions.cs`
- Create: `src/Nfty.Model/ModelFile.cs`
- Create: `src/Nfty.Model/Mesh.cs`
- Create: `tests/Nfty.Model.Tests/Nfty.Model.Tests.csproj`
- Create: `tests/Nfty.Model.Tests/MeshTests.cs`
- Create: `tests/Nfty.Model.Tests/RiseOptionsTests.cs`
- Create: `tests/Nfty.Model.Tests/AssemblyRulesTests.cs`
- Modify: `nfty.sln` (via `dotnet sln add`)
- Modify: `.github/workflows/ci.yml` (cross-platform job)

**Interfaces:**
- Consumes: nothing.
- Produces (namespace `Nfty.Model`):
  ```csharp
  public enum ModelFormat { Glb, GltfWithBin, GltfEmbedded }
  public enum Pivot { BottomCenter, Center, MinCorner }
  public sealed record RiseOptions { float VoxelSize = 1f; Pivot Pivot = Pivot.BottomCenter; ModelFormat Format = ModelFormat.Glb; }  // init-only
  public sealed record ModelFile(string FileName, byte[] Bytes);
  public sealed class Mesh
  {
      public Mesh(float[] positions, float[] normals, float[] texCoords, uint[] indices, int textureWidth, int textureHeight);
      public IReadOnlyList<float> Positions { get; }   // xyz per vertex
      public IReadOnlyList<float> Normals { get; }     // xyz per vertex
      public IReadOnlyList<float> TexCoords { get; }   // uv per vertex, glTF convention (0,0) = top-left
      public IReadOnlyList<uint> Indices { get; }      // three per triangle
      public int VertexCount { get; }
      public int TriangleCount { get; }
      public System.Numerics.Vector3 Min { get; }      // eager
      public System.Numerics.Vector3 Max { get; }      // eager
      public int TextureWidth { get; }
      public int TextureHeight { get; }
  }
  ```

- [ ] **Step 1: Write the project files**

`src/Nfty.Model/Nfty.Model.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!--
    RISE: PIXEL ART TO A 3D MODEL, AND NOTHING CORE DEPENDS ON. The arrow runs one way on purpose:
    Nfty.Core does NOT reference this, so a consumer that only wants to generate a collection never
    pulls mesh code into its graph.

    It takes NO package of its own. ImageSharp arrives through Nfty.Core, which already pins it, and
    there is no glTF library: a GLB is a 12-byte header and two length-prefixed chunks around a JSON
    document of fixed shape, so the container is written here - which is also what makes the output
    byte-identical for identical input, and that is what lets a model be frozen by a test at all.
  -->
  <!-- No PropertyGroup: Directory.Build.props already sets TargetFramework, Nullable,
       ImplicitUsings, LangVersion, the version stamp and the documentation settings. -->

  <ItemGroup>
    <ProjectReference Include="..\Nfty.Core\Nfty.Core.csproj" />
  </ItemGroup>

  <!-- GreedyRects is internal: it is how faces are built, not something a caller should choose. -->
  <ItemGroup>
    <InternalsVisibleTo Include="Nfty.Model.Tests" />
  </ItemGroup>

</Project>
```

`tests/Nfty.Model.Tests/Nfty.Model.Tests.csproj` (the shape of `tests/Nfty.Cli.Tests/Nfty.Cli.Tests.csproj`; ImageSharp arrives transitively, as it does there):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <!-- xUnit v3 runs each test project as its own process, so it must build as an executable. -->
    <OutputType>Exe</OutputType>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Nfty.Model\Nfty.Model.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Add both projects to the solution**

```bash
dotnet sln nfty.sln add src/Nfty.Model/Nfty.Model.csproj --solution-folder src
dotnet sln nfty.sln add tests/Nfty.Model.Tests/Nfty.Model.Tests.csproj --solution-folder tests
dotnet sln nfty.sln list
```

Expected: the list shows `src\Nfty.Model\Nfty.Model.csproj` and `tests\Nfty.Model.Tests\Nfty.Model.Tests.csproj`, and `git diff nfty.sln` shows both nested under the existing `src` / `tests` folders (not new folders with the same name).

- [ ] **Step 3: Write the failing tests**

`tests/Nfty.Model.Tests/MeshTests.cs`:

```csharp
using System.Numerics;

namespace Nfty.Model.Tests;

public class MeshTests
{
    // One unit quad facing +Z: the smallest mesh there is.
    private static float[] QuadPositions() => [0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0];
    private static float[] QuadNormals() => [0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1];
    private static float[] QuadUvs() => [0, 1, 1, 1, 1, 0, 0, 0];
    private static uint[] QuadIndices() => [0, 1, 2, 0, 2, 3];

    private static Mesh Quad() =>
        new(QuadPositions(), QuadNormals(), QuadUvs(), QuadIndices(), textureWidth: 1, textureHeight: 1);

    [Fact]
    public void A_quad_is_four_vertices_and_two_triangles()
    {
        var mesh = Quad();
        Assert.Equal(4, mesh.VertexCount);
        Assert.Equal(2, mesh.TriangleCount);
        Assert.Equal(new uint[] { 0, 1, 2, 0, 2, 3 }, mesh.Indices);
    }

    [Fact]
    public void Bounds_are_computed_when_the_mesh_is_built()
    {
        // D7: computed once, eagerly. A lazily cached bound is a race the first time two threads
        // touch the same mesh, and the preview and the writer both read these.
        var mesh = Quad();
        Assert.Equal(new Vector3(0, 0, 0), mesh.Min);
        Assert.Equal(new Vector3(1, 1, 0), mesh.Max);
    }

    [Fact]
    public void The_mesh_copies_its_inputs_so_a_caller_cannot_change_it_afterwards()
    {
        float[] positions = QuadPositions();
        var mesh = new Mesh(positions, QuadNormals(), QuadUvs(), QuadIndices(), 1, 1);

        positions[0] = 99;

        Assert.Equal(0f, mesh.Positions[0]);
        Assert.Equal(0f, mesh.Min.X);
    }

    [Fact]
    public void An_index_past_the_last_vertex_is_refused()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new Mesh(QuadPositions(), QuadNormals(), QuadUvs(), [0, 1, 4], 1, 1));
        Assert.Contains("4", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_vertex_needs_exactly_one_normal_and_one_texture_coordinate()
    {
        Assert.Throws<ArgumentException>(() =>
            new Mesh(QuadPositions(), [0, 0, 1], QuadUvs(), QuadIndices(), 1, 1));
        Assert.Throws<ArgumentException>(() =>
            new Mesh(QuadPositions(), QuadNormals(), [0, 0], QuadIndices(), 1, 1));
    }

    [Fact]
    public void Indices_come_in_whole_triangles()
    {
        Assert.Throws<ArgumentException>(() =>
            new Mesh(QuadPositions(), QuadNormals(), QuadUvs(), [0, 1], 1, 1));
    }

    [Fact]
    public void A_position_that_is_not_a_finite_number_is_refused()
    {
        float[] positions = QuadPositions();
        positions[4] = float.NaN;
        Assert.Throws<ArgumentException>(() =>
            new Mesh(positions, QuadNormals(), QuadUvs(), QuadIndices(), 1, 1));
    }

    [Fact]
    public void The_texture_size_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Mesh(QuadPositions(), QuadNormals(), QuadUvs(), QuadIndices(), 0, 1));
    }
}
```

`tests/Nfty.Model.Tests/RiseOptionsTests.cs`:

```csharp
namespace Nfty.Model.Tests;

public class RiseOptionsTests
{
    [Fact]
    public void The_defaults_are_one_unit_voxels_seated_bottom_center_written_as_glb()
    {
        var o = new RiseOptions();
        Assert.Equal(1f, o.VoxelSize);
        Assert.Equal(Pivot.BottomCenter, o.Pivot);
        Assert.Equal(ModelFormat.Glb, o.Format);
    }

    [Fact]
    public void Every_option_is_set_by_init_so_a_new_axis_is_additive()
    {
        var o = new RiseOptions { VoxelSize = 0.5f, Pivot = Pivot.MinCorner, Format = ModelFormat.GltfEmbedded };
        Assert.Equal(new RiseOptions { VoxelSize = 0.5f, Pivot = Pivot.MinCorner, Format = ModelFormat.GltfEmbedded }, o);
    }
}
```

`tests/Nfty.Model.Tests/AssemblyRulesTests.cs`:

```csharp
using System.Reflection;
using System.Runtime.CompilerServices;
using Nfty.Core.Imaging;
using Nfty.Core.Model;

namespace Nfty.Model.Tests;

/// <summary>
/// Rules about the assembly as a whole, checked by reflection so a later file cannot quietly break
/// them.
/// </summary>
public class AssemblyRulesTests
{
    /// <summary>
    /// D7. The reference implementation cached a value lazily into a nullable field — a benign race
    /// while it ran on one thread, and not benign once a Set's models are built in parallel. The
    /// cheapest way to make "nothing is cached lazily" a property of the code rather than a habit:
    /// no field anywhere in this assembly can be written after construction. Compiler-generated
    /// closure types are exempt (their fields are the captured locals of one call); enums are exempt
    /// (their one instance field is the value).
    /// </summary>
    [Fact]
    public void No_field_in_the_assembly_can_be_written_after_construction()
    {
        var writable = typeof(Mesh).Assembly.GetTypes()
            .Where(t => !t.IsEnum && !IsCompilerGenerated(t))
            .SelectMany(t => t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
                                         | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(f => !f.IsInitOnly && !f.IsLiteral)
                .Select(f => $"{t.FullName}.{f.Name}"))
            .ToList();

        Assert.Empty(writable);
    }

    /// <summary>
    /// The arrow runs one way. A consumer that only generates a collection must never pull mesh code
    /// into its graph, which is the whole reason this is its own assembly.
    /// </summary>
    [Fact]
    public void Neither_the_engine_nor_the_roll_references_nfty_model()
    {
        foreach (var assembly in new[] { typeof(Opacity).Assembly, typeof(DepthMap).Assembly })
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), a => a.Name == "Nfty.Model");
    }

    private static bool IsCompilerGenerated(Type t) =>
        t.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
        || (t.DeclaringType is { } outer && IsCompilerGenerated(outer));
}
```

- [ ] **Step 4: Run them to verify they fail**

Run: `dotnet test tests/Nfty.Model.Tests`
Expected: build FAILS — `CS0246: The type or namespace name 'Mesh' could not be found` (and `RiseOptions`, `Pivot`, `ModelFormat`).

- [ ] **Step 5: Write the implementation**

`src/Nfty.Model/RiseOptions.cs`:

```csharp
namespace Nfty.Model;

/// <summary>The file shape a model is written in. All three are serialized from one glTF document
/// and differ only in where the binary buffer lives.</summary>
public enum ModelFormat
{
    /// <summary>One binary <c>.glb</c>: the JSON and the buffer in one self-contained file. The
    /// default — the form every viewer opens with nothing beside it.</summary>
    Glb,

    /// <summary>A text <c>.gltf</c> plus a sibling <c>.bin</c> holding the buffer; the JSON names
    /// the <c>.bin</c> by a relative URI.</summary>
    GltfWithBin,

    /// <summary>One text <c>.gltf</c> carrying its buffer inline as a base64 <c>data:</c> URI.</summary>
    GltfEmbedded,
}

/// <summary>Which point of the model's bounding box is moved to the origin.</summary>
public enum Pivot
{
    /// <summary>The center of the box's bottom face: X and Z centered, the lowest Y at zero — what
    /// a model standing on a floor wants.</summary>
    BottomCenter,

    /// <summary>The center of the box.</summary>
    Center,

    /// <summary>The box's minimum corner: every coordinate is zero or more.</summary>
    MinCorner,
}

/// <summary>How a picture is risen and written. Every member is <c>init</c> with a default, so a new
/// axis is additive at every existing call site — the rule <c>ExportOptions</c> already follows.</summary>
public sealed record RiseOptions
{
    /// <summary>World units per pixel (and per voxel of depth). Must be positive and finite.</summary>
    public float VoxelSize { get; init; } = 1f;

    /// <summary>Which point of the bounding box lands on the origin.</summary>
    public Pivot Pivot { get; init; } = Pivot.BottomCenter;

    /// <summary>The file shape <c>ModelWriter</c> produces.</summary>
    public ModelFormat Format { get; init; } = ModelFormat.Glb;
}
```

(`<c>`, not `<see cref>`: `ModelWriter` does not exist until Task 5, and an unresolved `cref` is warning CS1574, which fails the build. Task 5 Step 4 switches it.)

`src/Nfty.Model/ModelFile.cs`:

```csharp
namespace Nfty.Model;

/// <summary>One file of a written model: its name (no folder — the caller chooses where it goes) and
/// its complete bytes.</summary>
/// <param name="FileName">The file's name, e.g. <c>sprite.glb</c> or <c>sprite.bin</c>.</param>
/// <param name="Bytes">The file's contents.</param>
public sealed record ModelFile(string FileName, byte[] Bytes);
```

`src/Nfty.Model/Mesh.cs`:

```csharp
using System.Numerics;

namespace Nfty.Model;

/// <summary>
/// A triangle mesh: what <c>Riser</c> builds, what the writers serialize, and what the step-6
/// preview rasterizes. Immutable — every array is copied in and exposed read-only — and every
/// derived value is computed in the constructor, never lazily (mixel's D7), so one mesh may be read
/// from many threads.
/// </summary>
/// <remarks>
/// <para><b>Vertex rule.</b> A vertex is shared only by the two triangles of its own quad: four
/// vertices and six indices per quad, so <see cref="VertexCount"/> is always twice
/// <see cref="TriangleCount"/> for a risen mesh. Vertices that share a position across quads are NOT
/// merged — their normals and UVs differ per face, and a merge keyed on position alone would be
/// wrong. Whether a merge keyed on (position, normal, uv) is worth its complexity is a size question
/// answered by <c>RisePerfTests</c>' measurement, not assumed.</para>
///
/// <para>Coordinates follow glTF: +Y up, +Z toward the viewer. Texture coordinates put (0,0) at the
/// image's top-left, so v grows downward with the image row.</para>
/// </remarks>
public sealed class Mesh
{
    /// <summary>Builds a mesh, copying every array.</summary>
    /// <param name="positions">xyz per vertex.</param>
    /// <param name="normals">One xyz unit normal per vertex.</param>
    /// <param name="texCoords">One uv per vertex.</param>
    /// <param name="indices">Three vertex indices per triangle, counter-clockwise seen from outside.</param>
    /// <param name="textureWidth">Width of the image the UVs address.</param>
    /// <param name="textureHeight">Height of the image the UVs address.</param>
    /// <exception cref="ArgumentException">The arrays disagree about the vertex count, an index names
    /// no vertex, or a position is not finite.</exception>
    public Mesh(float[] positions, float[] normals, float[] texCoords, uint[] indices,
        int textureWidth, int textureHeight)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(normals);
        ArgumentNullException.ThrowIfNull(texCoords);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textureWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textureHeight);

        if (positions.Length == 0 || positions.Length % 3 != 0)
            throw new ArgumentException(
                $"Positions are xyz triples; {positions.Length} floats is not a whole, non-zero number of them.",
                nameof(positions));
        int vertexCount = positions.Length / 3;
        if (normals.Length != positions.Length)
            throw new ArgumentException(
                $"Every vertex needs one xyz normal: expected {positions.Length} floats, got {normals.Length}.",
                nameof(normals));
        if (texCoords.Length != vertexCount * 2)
            throw new ArgumentException(
                $"Every vertex needs one uv: expected {vertexCount * 2} floats, got {texCoords.Length}.",
                nameof(texCoords));
        if (indices.Length == 0 || indices.Length % 3 != 0)
            throw new ArgumentException(
                $"Indices come three per triangle; {indices.Length} is not a whole, non-zero number of triangles.",
                nameof(indices));
        foreach (uint i in indices)
            if (i >= (uint)vertexCount)
                throw new ArgumentException($"Index {i} names no vertex; the mesh has {vertexCount}.", nameof(indices));
        foreach (float f in positions)
            if (!float.IsFinite(f))
                throw new ArgumentException("Every position must be a finite number.", nameof(positions));

        float[] p = (float[])positions.Clone();
        Positions = Array.AsReadOnly(p);
        Normals = Array.AsReadOnly((float[])normals.Clone());
        TexCoords = Array.AsReadOnly((float[])texCoords.Clone());
        Indices = Array.AsReadOnly((uint[])indices.Clone());
        VertexCount = vertexCount;
        TriangleCount = indices.Length / 3;
        TextureWidth = textureWidth;
        TextureHeight = textureHeight;

        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        for (int v = 0; v < p.Length; v += 3)
        {
            var q = new Vector3(p[v], p[v + 1], p[v + 2]);
            min = Vector3.Min(min, q);
            max = Vector3.Max(max, q);
        }
        Min = min;
        Max = max;
    }

    /// <summary>xyz per vertex, in world units.</summary>
    public IReadOnlyList<float> Positions { get; }

    /// <summary>One xyz unit normal per vertex, pointing out of the solid.</summary>
    public IReadOnlyList<float> Normals { get; }

    /// <summary>One uv per vertex; (0,0) is the texture's top-left.</summary>
    public IReadOnlyList<float> TexCoords { get; }

    /// <summary>Three vertex indices per triangle, counter-clockwise seen from outside.</summary>
    public IReadOnlyList<uint> Indices { get; }

    /// <summary>How many vertices the mesh has.</summary>
    public int VertexCount { get; }

    /// <summary>How many triangles the mesh has.</summary>
    public int TriangleCount { get; }

    /// <summary>The smallest x, y and z over every vertex — exactly the values glTF's
    /// <c>POSITION</c> accessor must carry as <c>min</c>.</summary>
    public Vector3 Min { get; }

    /// <summary>The largest x, y and z over every vertex — glTF's <c>POSITION</c> <c>max</c>.</summary>
    public Vector3 Max { get; }

    /// <summary>Width of the image the UVs address; a writer refuses a texture of another size.</summary>
    public int TextureWidth { get; }

    /// <summary>Height of the image the UVs address.</summary>
    public int TextureHeight { get; }
}
```

- [ ] **Step 6: Add the project to CI's cross-platform job**

In `.github/workflows/ci.yml`, job `core-cross-platform`, replace:

```yaml
      - run: dotnet test tests/Nfty.Core.Tests -c Release
      - run: dotnet test tests/Nfty.Cli.Tests -c Release
```

with:

```yaml
      - run: dotnet test tests/Nfty.Core.Tests -c Release
      - run: dotnet test tests/Nfty.Cli.Tests -c Release
      # A model's bytes are promised identical for identical input on any machine, exactly as a
      # cooked Set's are, so the engine that writes them is checked on all three systems too.
      - run: dotnet test tests/Nfty.Model.Tests -c Release
```

- [ ] **Step 7: Run the tests and the build**

Run: `dotnet test tests/Nfty.Model.Tests`
Expected: PASS — 12 tests (8 `MeshTests`, 2 `RiseOptionsTests`, 2 `AssemblyRulesTests`).

Run: `dotnet build nfty.sln`
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 8: Commit**

```bash
git add src/Nfty.Model/Nfty.Model.csproj src/Nfty.Model/RiseOptions.cs src/Nfty.Model/ModelFile.cs src/Nfty.Model/Mesh.cs tests/Nfty.Model.Tests/Nfty.Model.Tests.csproj tests/Nfty.Model.Tests/MeshTests.cs tests/Nfty.Model.Tests/RiseOptionsTests.cs tests/Nfty.Model.Tests/AssemblyRulesTests.cs nfty.sln .github/workflows/ci.yml
git commit -m "build: the Nfty.Model assembly, its options and the Mesh

Nfty.Model references Nfty.Core and nothing else; no package of its own.
Mesh is immutable and computes its bounds eagerly (mixel's D7), and a
reflection sweep keeps every field in the assembly readonly.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: `GreedyRects` — maximal rectangles over a keyed grid

**Files:**
- Create: `src/Nfty.Model/GreedyRects.cs`
- Test: `tests/Nfty.Model.Tests/GreedyRectsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces (internal, namespace `Nfty.Model`):
  ```csharp
  internal readonly record struct PixelRect(int X, int Y, int Width, int Height, int Key); // Width/Height in cells, >= 1
  internal static class GreedyRects
  {
      public const int Unset = -1;
      public static List<PixelRect> Merge(int width, int height, ReadOnlySpan<int> keys); // keys row-major, negative = unset
  }
  ```

- [ ] **Step 1: Write the failing test**

`tests/Nfty.Model.Tests/GreedyRectsTests.cs`:

```csharp
namespace Nfty.Model.Tests;

public class GreedyRectsTests
{
    /// <summary>One char per cell: a digit is that cell's key, '.' is unset.</summary>
    private static (int Width, int Height, int[] Keys) Grid(params string[] rows)
    {
        int h = rows.Length, w = rows[0].Length;
        var keys = new int[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                keys[(y * w) + x] = rows[y][x] == '.' ? GreedyRects.Unset : rows[y][x] - '0';
        return (w, h, keys);
    }

    private static List<PixelRect> Merge(params string[] rows)
    {
        var (w, h, keys) = Grid(rows);
        return GreedyRects.Merge(w, h, keys);
    }

    [Fact]
    public void A_full_rectangle_of_one_key_merges_into_exactly_one()
    {
        Assert.Equal(new[] { new PixelRect(0, 0, 3, 2, 0) }, Merge("000", "000"));
    }

    [Fact]
    public void Cells_of_different_keys_never_merge()
    {
        // The whole reason the grid is keyed: two columns of different depth must not share a face.
        Assert.Equal(
            new[] { new PixelRect(0, 0, 1, 1, 2), new PixelRect(1, 0, 1, 1, 0) },
            Merge("20"));
    }

    [Fact]
    public void A_checkerboard_merges_into_one_rectangle_per_cell_which_is_the_worst_case()
    {
        var rects = Merge("0.0", ".0.", "0.0");
        Assert.Equal(5, rects.Count);
        Assert.All(rects, r => Assert.Equal((1, 1), (r.Width, r.Height)));
    }

    [Fact]
    public void An_empty_grid_yields_nothing()
    {
        Assert.Empty(Merge("...", "..."));
    }

    [Fact]
    public void Rectangles_come_in_row_major_discovery_order_growing_right_then_down()
    {
        // Pinned exactly: the order reaches the model file, and the file must be byte-identical.
        Assert.Equal(
            new[]
            {
                new PixelRect(1, 0, 1, 3, 0),
                new PixelRect(0, 1, 1, 1, 0),
                new PixelRect(2, 1, 1, 1, 0),
            },
            Merge(".0.", "000", ".0."));
    }

    /// <summary>
    /// The property that matters and that a shape-by-shape test cannot state: the rectangles TILE
    /// the set cells — each covered exactly once, by a rectangle of its own key, and no unset cell
    /// covered at all. Double coverage emits coplanar faces that z-fight; under-coverage leaves a
    /// hole. Neither raises anything.
    /// </summary>
    [Theory]
    [InlineData("000", "0.0", "000")]
    [InlineData("..0", ".00", "000")]
    [InlineData("0..", "000", "..0")]
    [InlineData("0011", "0011", "2222")]
    [InlineData("0101", "1010", "0101")]
    [InlineData("3332", "3.12", "3312")]
    public void The_rectangles_tile_every_set_cell_exactly_once_with_its_own_key(params string[] rows)
    {
        var (w, h, keys) = Grid(rows);
        var cover = new int[w * h];

        foreach (var r in GreedyRects.Merge(w, h, keys))
        {
            Assert.True(r.Width >= 1 && r.Height >= 1, $"empty rectangle {r}");
            for (int y = r.Y; y < r.Y + r.Height; y++)
                for (int x = r.X; x < r.X + r.Width; x++)
                {
                    Assert.Equal(r.Key, keys[(y * w) + x]);
                    cover[(y * w) + x]++;
                }
        }

        for (int i = 0; i < keys.Length; i++)
            Assert.Equal(keys[i] < 0 ? 0 : 1, cover[i]);
    }

    [Fact]
    public void A_key_array_of_the_wrong_length_is_refused()
    {
        Assert.Throws<ArgumentException>(() => GreedyRects.Merge(2, 2, new int[3]));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~GreedyRectsTests`
Expected: build FAILS — `GreedyRects` / `PixelRect` not found.

- [ ] **Step 3: Write the implementation**

`src/Nfty.Model/GreedyRects.cs`:

```csharp
namespace Nfty.Model;

/// <summary>A merged run of cells that share one key. <see cref="Width"/> and
/// <see cref="Height"/> are in cells and at least 1.</summary>
/// <param name="X">Left column.</param>
/// <param name="Y">Top row (image rows: 0 is the top).</param>
/// <param name="Width">Columns covered.</param>
/// <param name="Height">Rows covered.</param>
/// <param name="Key">The key every covered cell carries.</param>
internal readonly record struct PixelRect(int X, int Y, int Width, int Height, int Key);

/// <summary>
/// Merges the set cells of a keyed grid into maximal rectangles of equal key — what makes a risen
/// sprite a few dozen quads instead of one per pixel.
/// </summary>
/// <remarks>
/// <para>Taken, as an idea, from mixel's <c>GreedyRects</c> and generalized from a boolean mask to a
/// keyed grid, so the front faces of every depth level come out of ONE scan: a cell only joins a
/// rectangle of its own key.</para>
///
/// <para><b>The rectangles TILE the set cells</b> — each exactly once, none unset. Greedy: from the
/// first free cell in row-major order, extend right while the key continues, then down while every
/// cell of the strip continues. Not optimal, and optimal partitioning is not worth its cost here.
/// The discovery order is part of the contract: it reaches the model file, which must be
/// byte-identical for identical input.</para>
/// </remarks>
internal static class GreedyRects
{
    /// <summary>The key of a cell that belongs to no rectangle. Any negative key means the same.</summary>
    public const int Unset = -1;

    /// <summary>Merges a grid.</summary>
    /// <param name="width">Columns.</param>
    /// <param name="height">Rows.</param>
    /// <param name="keys">One key per cell, row-major, top row first; negative means unset.</param>
    /// <returns>The rectangles, in row-major discovery order.</returns>
    public static List<PixelRect> Merge(int width, int height, ReadOnlySpan<int> keys)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (keys.Length != width * height)
            throw new ArgumentException(
                $"A {width}x{height} grid has {width * height} cells; got {keys.Length} keys.", nameof(keys));

        var rects = new List<PixelRect>();
        var used = new bool[keys.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int key = keys[(y * width) + x];
                if (key < 0 || used[(y * width) + x]) continue;

                int right = x + 1;
                while (right < width && Free(keys, used, (y * width) + right, key)) right++;

                int bottom = y + 1;
                while (bottom < height && RowFree(keys, used, width, bottom, x, right, key)) bottom++;

                for (int yy = y; yy < bottom; yy++)
                    for (int xx = x; xx < right; xx++)
                        used[(yy * width) + xx] = true;

                rects.Add(new PixelRect(x, y, right - x, bottom - y, key));
            }
        }
        return rects;
    }

    private static bool Free(ReadOnlySpan<int> keys, bool[] used, int i, int key) =>
        keys[i] == key && !used[i];

    private static bool RowFree(ReadOnlySpan<int> keys, bool[] used, int width, int y, int x0, int x1, int key)
    {
        for (int x = x0; x < x1; x++)
            if (!Free(keys, used, (y * width) + x, key)) return false;
        return true;
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~GreedyRectsTests`
Expected: PASS — 12 (6 facts + 6 theory cases).

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.Model/GreedyRects.cs tests/Nfty.Model.Tests/GreedyRectsTests.cs
git commit -m "feat: greedy maximal rectangles over a keyed grid

One scan merges the faces of every depth level; cells only join a
rectangle of their own key. The tiling property is asserted directly.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: `Riser` — faces, two-segment walls, pivot, and the closed-solid anchor test

**Files:**
- Create: `src/Nfty.Model/MeshBuilder.cs`
- Create: `src/Nfty.Model/Riser.cs`
- Create: `src/Nfty.Model/PartialAlphaException.cs`
- Create: `tests/Nfty.Model.Tests/Art.cs`
- Create: `tests/Nfty.Model.Tests/Shapes.cs`
- Create: `tests/Nfty.Model.Tests/MeshChecks.cs`
- Test: `tests/Nfty.Model.Tests/RiserTests.cs`
- Test: `tests/Nfty.Model.Tests/RiserSolidityTests.cs`

**Interfaces:**
- Consumes: `Mesh`, `RiseOptions`, `Pivot` (Task 1); `GreedyRects.Merge`, `PixelRect`, `GreedyRects.Unset` (Task 2); `Nfty.Core.Imaging.Opacity.IsSolid/IsPartial`; `Nfty.Core.Model.DepthMap`; `Nfty.Core.Diagnostics.Perf.Measure`.
- Produces:
  ```csharp
  public static class Riser
  {
      public static Mesh? TryBuild(Image<Rgba32> art, DepthMap? depth, RiseOptions options, string source = "the art");
  }
  public sealed class PartialAlphaException : InvalidOperationException
  {
      public PartialAlphaException(string itemName, int x, int y, byte alpha, int count);
      public string ItemName { get; } public int X { get; } public int Y { get; } public byte Alpha { get; } public int Count { get; }
  }
  internal enum WallFace { Left, Right, Top, Bottom }
  internal sealed class MeshBuilder { MeshBuilder(int width, int height); void FrontFace(PixelRect); void BackFace(PixelRect); void Wall(WallFace, int x, int y, float z0, float z1); Mesh ToMesh(RiseOptions); }
  ```
- Test helpers other tasks reuse: `Art.FromAscii(params string[])`, `Art.Opaque(x, y)`, `Art.Depth(Image<Rgba32>, Func<int,int,(int Front,int Back)>)`, `Shapes.All`, `Shapes.Patterns`, `Shapes.Uniform`, `MeshChecks.*`.

**The geometry, exactly.** Everything below is in voxel units before the pivot; `W × H` is the image.

| Thing | Rule |
|-------|------|
| Axes | +X right, +Y up, +Z toward the viewer (glTF's default camera looks down −Z). Image row 0 is the TOP, so pixel `(x, y)` covers world `X ∈ [x, x+1]`, `Y ∈ [H−1−y, H−y]`. |
| Column | a solid pixel with depth `(front, back)` spans `Z ∈ [lo, hi] = [−(0.5 + back), 0.5 + front]`; thickness `1 + front + back`. |
| Solid | `Opacity.IsSolid` (`A == 255`). Depth on any other pixel is ignored. |
| Front faces | `GreedyRects` over keys = `front` on solid pixels (`Unset` elsewhere); rect `(x, y, w, h)` → quad at `z = 0.5 + key`, `X ∈ [x, x+w]`, `Y ∈ [H−(y+h), H−y]`, normal `(0,0,1)`, corners `(X0,Ylo) (X1,Ylo) (X1,Yhi) (X0,Yhi)`. |
| Back faces | same over keys = `back`, at `z = −(0.5 + key)`, normal `(0,0,−1)`, corners `(X0,Ylo) (X0,Yhi) (X1,Yhi) (X1,Ylo)`. |
| Face UVs | sample the art directly: `u = X / W`, `v = (H − Y) / H` (glTF's (0,0) is the image's top-left). |
| Walls | per solid pixel, sides Left `(x−1)`, Right `(x+1)`, Top `(y−1)`, Bottom `(y+1)`. Neighbor outside the image or not solid ⇒ one wall `[lo, hi]`. Neighbor solid with `[nlo, nhi]` ⇒ `[lo, nlo]` if `lo < nlo`, then `[nhi, hi]` if `nhi < hi` — up to **two** segments. Both intervals contain the paper `[−0.5, 0.5]`, so the difference is never more than two pieces. |
| Wall corners (`z0 < z1`, `y0 = H−1−y`, `y1 = H−y`) | Left, normal `(−1,0,0)`: `(x,y0,z0) (x,y0,z1) (x,y1,z1) (x,y1,z0)`. Right, `(1,0,0)`: `(x+1,y0,z0) (x+1,y1,z0) (x+1,y1,z1) (x+1,y0,z1)`. Top, `(0,1,0)`: `(x,y1,z0) (x,y1,z1) (x+1,y1,z1) (x+1,y1,z0)`. Bottom, `(0,−1,0)`: `(x,y0,z0) (x+1,y0,z0) (x+1,y0,z1) (x,y0,z1)`. |
| Wall UVs | all four at the pixel center: `((x + 0.5) / W, (y + 0.5) / H)`. |
| Winding | every quad's corners `a b c d` are counter-clockwise seen from outside; triangles `(a,b,c)` and `(a,c,d)`. Checked: `(b−a) × (c−a)` equals the listed normal times the quad's area for all six kinds. Single-sided (D3). |
| Order | front rects, back rects, then walls row-major, sides L R T B, back segment before front. |
| Pivot | bounding box of every vertex; `MinCorner` → `min`; `Center` → `(min+max)/2`; `BottomCenter` → `((minX+maxX)/2, minY, (minZ+maxZ)/2)`. Final position = `(p − pivot) × VoxelSize`. |
| Empty | no solid pixel ⇒ `null` (D5). Partial pixel anywhere ⇒ `PartialAlphaException`, checked first. |

**Exact triangle counts** (paper; every quad is 2 triangles; count = 2 × (front rects + back rects + wall quads)):

| Shape | Rows | Rects (front = back) | Wall quads | Triangles |
|-------|------|------|-----------|-----------|
| single | `#` | 1 | 4 | **12** |
| strip | `##` | 1 | 6 | **16** |
| square | `###` ×3 | 1 | 12 | **28** |
| plus | `.#.` `###` `.#.` | 3 | 12 | **36** |
| L | `#..` `#..` `###` | 2 | 12 | **32** |
| hole | `###` `#.#` `###` | 4 | 16 | **48** |
| checkerboard | `#.#` `.#.` `#.#` | 5 | 20 | **60** |

Uniform depth (every pixel the same `(front, back)`) keeps these counts — every wall is still one segment. `strip` with `(x+y)` even ⇒ `(2,2)`, odd ⇒ `(0,0)` (the "alternate" pattern): fronts 2 rects, backs 2 rects, walls 3 + **2** (the split seam) + 3 = 8 ⇒ **24**. `###` under alternate ⇒ **36**.

**Why the anchor test refines edges — the worked example.** `strip` (`##`), paper, `Pivot.MinCorner`, `VoxelSize 1`, so every vertex is an integer lattice point (`z` shifted by `0.5 + maxBack`):

- The one front quad is `a(0,0,1) b(2,0,1) c(2,1,1) d(0,1,1)`. Its top edge is the directed edge `c→d = (2,1,1)→(0,1,1)`, length 2.
- The top walls are per pixel. Pixel `x=1`: `a(1,1,0) b(1,1,1) c(2,1,1) d(2,1,0)`, so it walks `b→c = (1,1,1)→(2,1,1)`, length 1. Pixel `x=0` walks `(0,1,1)→(1,1,1)`.
- Raw edges: `(2,1,1)→(0,1,1)` appears once and its reverse never. **"Every edge shared by exactly two triangles" fails** on the simplest merged mesh there is — a T-junction at `(1,1,1)`.
- Refined to unit segments, the front edge becomes `(2,1,1)→(1,1,1)` and `(1,1,1)→(0,1,1)`; each is exactly the reverse of one wall's segment. Balanced.

Every axis-aligned edge refines cleanly because every vertex is a lattice point; a quad's diagonal is never axis-aligned and is walked once each way by its own two triangles, so it is keyed unrefined. No vertex lies inside a face rectangle (rectangles tile disjointly, and a wall's edge at a face's `z` lies only on that face's region boundary), so no diagonal passes through one.

**Why balanced, not "exactly once".** `#.` / `.#`, paper, MinCorner: the line `X=1, Y=1` is walked upward by A's bottom wall and B's top wall and downward by A's right wall and B's left wall — **two each way**, a pinch. The surface is closed and consistently oriented; it is not a 2-manifold, and no correct geometry makes it one (the two columns really do touch along that edge). The same pinch appears under the alternate pattern wherever two raised diagonal columns rise above both of their paper side-neighbors.

- [ ] **Step 1: Write the test helpers**

`tests/Nfty.Model.Tests/Art.cs`:

```csharp
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

/// <summary>
/// Tiny synthetic art, built in memory. '#' is fully opaque, '.' is fully clear, '~' is half-alpha —
/// the one thing a voxel model cannot represent. Every opaque pixel gets a color of its own, so a
/// texture test can tell which pixel a texel came from.
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
                    '#' => Opaque(x, y),
                    '~' => new Rgba32(200, 40, 40, 128),
                    _ => new Rgba32(0, 0, 0, 0),
                };
        return img;
    }

    public static Rgba32 Opaque(int x, int y) => new((byte)(10 + (x * 20)), (byte)(10 + (y * 20)), 200, 255);

    /// <summary>A depth map over the whole image, one (front, back) per pixel, 0..255 each.</summary>
    public static DepthMap Depth(Image<Rgba32> art, Func<int, int, (int Front, int Back)> at)
    {
        var bytes = new byte[art.Width * art.Height * 2];
        for (int y = 0; y < art.Height; y++)
            for (int x = 0; x < art.Width; x++)
            {
                var (front, back) = at(x, y);
                int i = ((y * art.Width) + x) * 2;
                bytes[i] = checked((byte)front);
                bytes[i + 1] = checked((byte)back);
            }
        return DepthMap.FromBytes(art.Width, art.Height, bytes);
    }
}
```

`tests/Nfty.Model.Tests/Shapes.cs`:

```csharp
namespace Nfty.Model.Tests;

/// <summary>The fixture shapes and depth patterns the spec's Testing section names.</summary>
internal static class Shapes
{
    public static readonly IReadOnlyDictionary<string, string[]> All = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["single"] = ["#"],
        ["strip"] = ["##"],
        ["square"] = ["###", "###", "###"],
        ["plus"] = [".#.", "###", ".#."],
        ["L"] = ["#..", "#..", "###"],
        ["hole"] = ["###", "#.#", "###"],
        ["checkerboard"] = ["#.#", ".#.", "#.#"],
    };

    public static readonly IReadOnlyDictionary<string, Func<int, int, (int Front, int Back)>> Patterns =
        new Dictionary<string, Func<int, int, (int Front, int Back)>>(StringComparer.Ordinal)
        {
            ["paper"] = (_, _) => (0, 0),
            ["front"] = (_, _) => (2, 0),
            ["back"] = (_, _) => (0, 3),
            ["both"] = (_, _) => (1, 2),
            // Neighbors differing on BOTH sides: a raised column beside a paper one exposes a wall
            // toward the viewer AND one away from them, so its seam wall splits in two.
            ["alternate"] = (x, y) => (x + y) % 2 == 0 ? (2, 2) : (0, 0),
            ["staircase"] = (x, y) => (x, y),
        };

    /// <summary>Patterns under which every column has the same interval.</summary>
    public static readonly string[] Uniform = ["paper", "front", "back", "both"];

    public static TheoryData<string, string> EveryShapeAndPattern()
    {
        var data = new TheoryData<string, string>();
        foreach (string shape in All.Keys)
            foreach (string pattern in Patterns.Keys)
                data.Add(shape, pattern);
        return data;
    }

    public static TheoryData<string, string> UnpinchedShapesUnderUniformDepth()
    {
        var data = new TheoryData<string, string>();
        foreach (string shape in All.Keys.Where(s => s != "checkerboard"))
            foreach (string pattern in Uniform)
                data.Add(shape, pattern);
        return data;
    }

    /// <summary>The solid's exact volume in voxels: one column of 1 + front + back per solid pixel.</summary>
    public static int Voxels(string shape, string pattern)
    {
        var rows = All[shape];
        int total = 0;
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                if (rows[y][x] == '#')
                {
                    var (front, back) = Patterns[pattern](x, y);
                    total += 1 + front + back;
                }
        return total;
    }
}
```

`tests/Nfty.Model.Tests/MeshChecks.cs`:

```csharp
using System.Numerics;

namespace Nfty.Model.Tests;

internal readonly record struct Point3(int X, int Y, int Z);

/// <summary>One quad of a risen mesh: every four consecutive vertices are one quad.</summary>
internal sealed record QuadView(Vector3 Normal, Vector3[] Corners, Vector2[] Uvs);

/// <summary>
/// The solidity checks. See Task 3 of the Rise step-5 plan for why "every edge is shared by exactly
/// two triangles" is not the property to assert: greedy faces meet per-pixel walls at T-junctions,
/// and pixels that touch only at a corner pinch four faces onto one edge. What IS true, and what
/// these assert, is that the surface is closed and consistently oriented at the level of unit
/// lattice segments, and that it encloses exactly the voxels it should.
/// </summary>
internal static class MeshChecks
{
    public static Vector3 Position(Mesh m, int v) =>
        new(m.Positions[v * 3], m.Positions[(v * 3) + 1], m.Positions[(v * 3) + 2]);

    public static Vector3 Normal(Mesh m, int v) =>
        new(m.Normals[v * 3], m.Normals[(v * 3) + 1], m.Normals[(v * 3) + 2]);

    public static Vector2 Uv(Mesh m, int v) => new(m.TexCoords[v * 2], m.TexCoords[(v * 2) + 1]);

    public static IReadOnlyList<QuadView> Quads(Mesh m)
    {
        var quads = new List<QuadView>();
        for (int q = 0; q < m.VertexCount / 4; q++)
        {
            int v = q * 4;
            quads.Add(new QuadView(
                Normal(m, v),
                [Position(m, v), Position(m, v + 1), Position(m, v + 2), Position(m, v + 3)],
                [Uv(m, v), Uv(m, v + 1), Uv(m, v + 2), Uv(m, v + 3)]));
        }
        return quads;
    }

    /// <summary>A vertex as an integer lattice point. Only meaningful for a mesh built with
    /// <see cref="Pivot.MinCorner"/> and a voxel size of 1, where every coordinate is an integer.</summary>
    public static Point3 Lattice(Mesh m, int vertex)
    {
        int Axis(int k)
        {
            float f = m.Positions[(vertex * 3) + k];
            int n = (int)MathF.Round(f);
            Assert.True(f == n,
                $"vertex {vertex} axis {k} is {f}, not a lattice value; build with Pivot.MinCorner and VoxelSize 1");
            return n;
        }
        return new Point3(Axis(0), Axis(1), Axis(2));
    }

    /// <summary>Every directed triangle edge, unrefined.</summary>
    public static Dictionary<(Point3 From, Point3 To), int> RawEdges(Mesh m)
    {
        var counts = new Dictionary<(Point3 From, Point3 To), int>();
        for (int t = 0; t < m.Indices.Count; t += 3)
            for (int e = 0; e < 3; e++)
            {
                var key = (Lattice(m, (int)m.Indices[t + e]), Lattice(m, (int)m.Indices[t + ((e + 1) % 3)]));
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        return counts;
    }

    /// <summary>
    /// Every directed triangle edge, with each axis-aligned edge split into unit lattice segments —
    /// which is what makes a T-junction countable. A diagonal (a quad's own split) is kept whole: it
    /// is walked once each way by the quad's two triangles and never touches another quad.
    /// </summary>
    public static Dictionary<(Point3 From, Point3 To), int> UnitSegments(Mesh m)
    {
        var counts = new Dictionary<(Point3 From, Point3 To), int>();
        void Add(Point3 a, Point3 b) => counts[(a, b)] = counts.GetValueOrDefault((a, b)) + 1;

        for (int t = 0; t < m.Indices.Count; t += 3)
            for (int e = 0; e < 3; e++)
            {
                var a = Lattice(m, (int)m.Indices[t + e]);
                var b = Lattice(m, (int)m.Indices[t + ((e + 1) % 3)]);
                int dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
                int moving = (dx != 0 ? 1 : 0) + (dy != 0 ? 1 : 0) + (dz != 0 ? 1 : 0);
                Assert.True(moving > 0, $"triangle {t / 3} has a zero-length edge at {a}");
                if (moving > 1)
                {
                    Add(a, b);
                    continue;
                }
                int steps = Math.Abs(dx + dy + dz);
                int sx = Math.Sign(dx), sy = Math.Sign(dy), sz = Math.Sign(dz);
                for (int k = 0; k < steps; k++)
                    Add(new Point3(a.X + (k * sx), a.Y + (k * sy), a.Z + (k * sz)),
                        new Point3(a.X + ((k + 1) * sx), a.Y + ((k + 1) * sy), a.Z + ((k + 1) * sz)));
            }
        return counts;
    }

    /// <summary>Closed and consistently oriented: every unit segment is walked exactly as often in
    /// one direction as in the other. A hole, a duplicated face or a flipped face all break it.</summary>
    public static void AssertClosed(Mesh m)
    {
        var segments = UnitSegments(m);
        foreach (var ((from, to), n) in segments)
        {
            int back = segments.GetValueOrDefault((to, from));
            Assert.True(back == n,
                $"{from}->{to} is walked {n} time(s) but {to}->{from} {back}: a hole, a duplicate face or a flipped one");
        }
    }

    /// <summary>The strict form, for shapes with no pinch: every unit segment exactly once each way.</summary>
    public static void AssertTwoManifold(Mesh m)
    {
        var segments = UnitSegments(m);
        foreach (var ((from, to), n) in segments)
        {
            Assert.True(n == 1, $"{from}->{to} is walked {n} times");
            Assert.True(segments.GetValueOrDefault((to, from)) == 1, $"{to}->{from} is not walked exactly once");
        }
    }

    /// <summary>Signed volume by the divergence theorem. Exact for lattice meshes; positive only
    /// when every face winds outward.</summary>
    public static double Volume(Mesh m)
    {
        double total = 0;
        for (int t = 0; t < m.Indices.Count; t += 3)
        {
            var a = Position(m, (int)m.Indices[t]);
            var b = Position(m, (int)m.Indices[t + 1]);
            var c = Position(m, (int)m.Indices[t + 2]);
            double cx = ((double)b.Y * c.Z) - ((double)b.Z * c.Y);
            double cy = ((double)b.Z * c.X) - ((double)b.X * c.Z);
            double cz = ((double)b.X * c.Y) - ((double)b.Y * c.X);
            total += (a.X * cx) + (a.Y * cy) + (a.Z * cz);
        }
        return total / 6;
    }

    /// <summary>Every triangle has area, and each of its vertices' stored normals is the unit normal
    /// its winding implies — so a face drawn inside-out is caught even where the volume is not.</summary>
    public static void AssertNormalsMatchWinding(Mesh m)
    {
        for (int t = 0; t < m.Indices.Count; t += 3)
        {
            int ia = (int)m.Indices[t], ib = (int)m.Indices[t + 1], ic = (int)m.Indices[t + 2];
            var a = Position(m, ia);
            var cross = Vector3.Cross(Position(m, ib) - a, Position(m, ic) - a);
            Assert.True(cross.LengthSquared() > 0, $"triangle {t / 3} is degenerate");
            var expected = Vector3.Normalize(cross);
            foreach (int v in new[] { ia, ib, ic })
                Assert.Equal(expected, Normal(m, v));
        }
    }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/Nfty.Model.Tests/RiserTests.cs`:

```csharp
using System.Numerics;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

public class RiserTests
{
    private static readonly RiseOptions LatticeOptions = new() { Pivot = Pivot.MinCorner };

    private static Mesh Rise(string[] rows, Func<int, int, (int Front, int Back)>? depth = null, RiseOptions? options = null)
    {
        using var art = Art.FromAscii(rows);
        DepthMap? map = depth is null ? null : Art.Depth(art, depth);
        return Riser.TryBuild(art, map, options ?? LatticeOptions)
            ?? throw new InvalidOperationException("the fixture has no solid pixel");
    }

    [Fact]
    public void A_single_paper_pixel_is_a_cube_of_twelve_triangles()
    {
        var mesh = Rise(["#"]);
        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal(24, mesh.VertexCount);
    }

    [Theory]
    [InlineData("single", 12)]
    [InlineData("strip", 16)]
    [InlineData("square", 28)]
    [InlineData("plus", 36)]
    [InlineData("L", 32)]
    [InlineData("hole", 48)]
    [InlineData("checkerboard", 60)]
    public void Uniform_depth_changes_the_height_and_never_the_triangle_count(string shape, int triangles)
    {
        foreach (string pattern in Shapes.Uniform)
            Assert.Equal(triangles, Rise(Shapes.All[shape], Shapes.Patterns[pattern]).TriangleCount);
    }

    [Fact]
    public void A_neighbor_inside_both_ends_of_a_column_splits_its_wall_in_two()
    {
        // Left column (2, 2) spans z [-2.5, 2.5]; its paper neighbor spans [-0.5, 0.5]. The seam wall
        // is the difference: one piece behind the paper and one in front of it. After MinCorner
        // shifts z by 2.5 those are [0, 2] and [3, 5].
        var mesh = Rise(["##"], (x, _) => x == 0 ? (2, 2) : (0, 0));

        var seam = MeshChecks.Quads(mesh)
            .Where(q => q.Normal == Vector3.UnitX && q.Corners.All(c => c.X == 1f))
            .Select(q => (Low: q.Corners.Min(c => c.Z), High: q.Corners.Max(c => c.Z)))
            .OrderBy(s => s.Low)
            .ToArray();

        Assert.Equal(new[] { (0f, 2f), (3f, 5f) }, seam);
        Assert.Equal(24, mesh.TriangleCount);
    }

    [Fact]
    public void Three_alternating_columns_are_thirty_six_triangles()
    {
        Assert.Equal(36, Rise(["###"], (x, y) => (x + y) % 2 == 0 ? (2, 2) : (0, 0)).TriangleCount);
    }

    [Fact]
    public void Front_rises_toward_the_viewer_and_back_away_from_them_around_one_paper_plane()
    {
        // Left paper (0,0): front at +0.5, back at -0.5. Right (2,1): front at 2.5, back at -1.5.
        // MinCorner shifts z by 1.5, so fronts land at 2 and 4, backs at 1 and 0.
        var mesh = Rise(["##"], (x, _) => x == 0 ? (0, 0) : (2, 1));
        var quads = MeshChecks.Quads(mesh);

        var fronts = quads.Where(q => q.Normal == Vector3.UnitZ).Select(q => q.Corners[0].Z).Order().ToArray();
        var backs = quads.Where(q => q.Normal == -Vector3.UnitZ).Select(q => q.Corners[0].Z).Order().ToArray();

        Assert.Equal(new[] { 2f, 4f }, fronts);
        Assert.Equal(new[] { 0f, 1f }, backs);
    }

    [Fact]
    public void Image_row_zero_is_the_top_of_the_model()
    {
        // "#." over "##": the tall rectangle is column 0; the lone rectangle is the BOTTOM-right
        // pixel, which must sit at world Y [0, 1], not [1, 2].
        var mesh = Rise(["#.", "##"]);
        var fronts = MeshChecks.Quads(mesh)
            .Where(q => q.Normal == Vector3.UnitZ)
            .Select(q => (q.Corners.Min(c => c.X), q.Corners.Min(c => c.Y), q.Corners.Max(c => c.X), q.Corners.Max(c => c.Y)))
            .ToArray();

        Assert.Equal(new[] { (0f, 0f, 1f, 2f), (1f, 0f, 2f, 1f) }, fronts);
    }

    [Theory]
    [InlineData(Pivot.BottomCenter, -1.5f, 0f, -0.5f, 1.5f, 3f, 0.5f)]
    [InlineData(Pivot.Center, -1.5f, -1.5f, -0.5f, 1.5f, 1.5f, 0.5f)]
    [InlineData(Pivot.MinCorner, 0f, 0f, 0f, 3f, 3f, 1f)]
    public void Each_pivot_puts_its_named_point_of_the_bounding_box_on_the_origin(
        Pivot pivot, float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
    {
        var mesh = Rise(Shapes.All["plus"], options: new RiseOptions { Pivot = pivot });
        Assert.Equal(new Vector3(minX, minY, minZ), mesh.Min);
        Assert.Equal(new Vector3(maxX, maxY, maxZ), mesh.Max);
    }

    [Fact]
    public void Bottom_center_centers_the_solid_in_depth_not_the_paper()
    {
        // front 2, back 1: z spans [-1.5, 2.5] raw, 4 deep, so centered it is [-2, 2].
        var mesh = Rise(["#"], (_, _) => (2, 1), new RiseOptions());
        Assert.Equal(new Vector3(-0.5f, 0f, -2f), mesh.Min);
        Assert.Equal(new Vector3(0.5f, 1f, 2f), mesh.Max);
    }

    [Fact]
    public void Voxel_size_scales_every_coordinate()
    {
        var mesh = Rise(Shapes.All["plus"], options: new RiseOptions { Pivot = Pivot.MinCorner, VoxelSize = 0.25f });
        Assert.Equal(Vector3.Zero, mesh.Min);
        Assert.Equal(new Vector3(0.75f, 0.75f, 0.25f), mesh.Max);
    }

    [Fact]
    public void Where_the_sprite_sits_on_its_canvas_does_not_move_the_model()
    {
        var alone = Rise(["#"], options: new RiseOptions());
        var inCanvas = Rise([".....", "...#.", "....."], options: new RiseOptions());
        Assert.Equal(alone.Positions, inCanvas.Positions);
        Assert.Equal(alone.Indices, inCanvas.Indices);
    }

    [Fact]
    public void Faces_sample_the_art_they_cover()
    {
        // ".##." : one merged rectangle over columns 1..2 of a 4-wide, 1-tall image.
        var mesh = Rise([".##."]);
        var expected = new[] { new Vector2(0.25f, 1f), new Vector2(0.75f, 1f), new Vector2(0.75f, 0f), new Vector2(0.25f, 0f) };

        var front = Assert.Single(MeshChecks.Quads(mesh), q => q.Normal == Vector3.UnitZ);
        Assert.Equal(expected, front.Uvs);

        var back = Assert.Single(MeshChecks.Quads(mesh), q => q.Normal == -Vector3.UnitZ);
        Assert.Equal(expected.ToHashSet(), back.Uvs.ToHashSet());
    }

    [Theory]
    [InlineData(".#", 0.75f)]
    [InlineData("#.", 0.25f)]
    public void Walls_take_their_pixels_center_color(string row, float u)
    {
        var walls = MeshChecks.Quads(Rise([row])).Where(q => q.Normal.Z == 0f).ToList();
        Assert.Equal(4, walls.Count);
        Assert.All(walls, w => Assert.All(w.Uvs, uv => Assert.Equal(new Vector2(u, 0.5f), uv)));
    }

    [Fact]
    public void Vertices_are_shared_within_a_quad_and_nowhere_else()
    {
        var mesh = Rise(Shapes.All["plus"], Shapes.Patterns["alternate"]);
        Assert.Equal(2 * mesh.TriangleCount, mesh.VertexCount);
        for (int q = 0; q < mesh.TriangleCount / 2; q++)
        {
            uint b = (uint)(q * 4);
            Assert.Equal(new[] { b, b + 1, b + 2, b, b + 2, b + 3 }, mesh.Indices.Skip(q * 6).Take(6));
        }
    }

    [Fact]
    public void Depth_on_a_transparent_pixel_is_ignored()
    {
        var plain = Rise(["#."]);
        var withStrayDepth = Rise(["#."], (x, _) => x == 1 ? (5, 5) : (0, 0));
        Assert.Equal(plain.Positions, withStrayDepth.Positions);
    }

    [Fact]
    public void Art_with_nothing_solid_returns_null_rather_than_throwing()
    {
        // D5. A rolled asset can legitimately be empty; a batch must be able to skip and name it.
        using var art = Art.FromAscii("..", "..");
        Assert.Null(Riser.TryBuild(art, null, new RiseOptions()));
    }

    [Fact]
    public void Partial_alpha_is_refused_by_name()
    {
        using var art = Art.FromAscii("#~");
        var ex = Assert.Throws<PartialAlphaException>(() =>
            Riser.TryBuild(art, null, new RiseOptions(), "variant 'red' of hat.igt"));

        Assert.Equal("variant 'red' of hat.igt", ex.ItemName);
        Assert.Equal((1, 0, (byte)128, 1), (ex.X, ex.Y, ex.Alpha, ex.Count));
        Assert.Contains("variant 'red' of hat.igt", ex.Message, StringComparison.Ordinal);
        Assert.Contains("(1, 0)", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Partial_alpha_is_checked_before_emptiness_so_it_is_refused_not_skipped()
    {
        using var art = Art.FromAscii("~~");
        var ex = Assert.Throws<PartialAlphaException>(() => Riser.TryBuild(art, null, new RiseOptions()));
        Assert.Equal(2, ex.Count);
    }

    [Fact]
    public void Alpha_254_is_partial_and_only_255_is_solid()
    {
        // D1: one threshold, Opacity's. Nothing rounds 254 up.
        var nearly = new Rgba32(1, 2, 3, 254);
        Assert.True(Opacity.IsPartial(nearly));
        using var art = new Image<Rgba32>(1, 1, nearly);
        Assert.Throws<PartialAlphaException>(() => Riser.TryBuild(art, null, new RiseOptions()));

        using var solid = new Image<Rgba32>(1, 1, new Rgba32(1, 2, 3, 255));
        Assert.NotNull(Riser.TryBuild(solid, null, new RiseOptions()));
    }

    [Fact]
    public void A_depth_map_of_another_size_is_refused()
    {
        using var art = Art.FromAscii("##");
        Assert.Throws<ArgumentException>(() => Riser.TryBuild(art, DepthMap.Flat(1, 1), new RiseOptions()));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void A_voxel_size_that_is_not_positive_and_finite_is_refused(float size)
    {
        using var art = Art.FromAscii("#");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Riser.TryBuild(art, null, new RiseOptions { VoxelSize = size }));
    }

    [Fact]
    public void Rising_the_same_input_twice_gives_identical_meshes()
    {
        var a = Rise(Shapes.All["hole"], Shapes.Patterns["staircase"]);
        var b = Rise(Shapes.All["hole"], Shapes.Patterns["staircase"]);
        Assert.Equal(a.Positions, b.Positions);
        Assert.Equal(a.Normals, b.Normals);
        Assert.Equal(a.TexCoords, b.TexCoords);
        Assert.Equal(a.Indices, b.Indices);
    }
}
```

`tests/Nfty.Model.Tests/RiserSolidityTests.cs`:

```csharp
namespace Nfty.Model.Tests;

/// <summary>
/// THE anchor test for the geometry: whatever the shape and whatever the depth, the risen mesh is a
/// closed, consistently oriented surface enclosing exactly its voxels. One assertion that catches a
/// whole class of wall bug, none of which raises anything on its own.
/// </summary>
public class RiserSolidityTests
{
    private static readonly RiseOptions LatticeOptions = new() { Pivot = Pivot.MinCorner };

    private static Mesh Rise(string shape, string pattern)
    {
        using var art = Art.FromAscii(Shapes.All[shape]);
        return Riser.TryBuild(art, Art.Depth(art, Shapes.Patterns[pattern]), LatticeOptions)!;
    }

    public static TheoryData<string, string> EveryShapeAndPattern() => Shapes.EveryShapeAndPattern();

    public static TheoryData<string, string> UnpinchedShapesUnderUniformDepth() => Shapes.UnpinchedShapesUnderUniformDepth();

    [Theory]
    [MemberData(nameof(EveryShapeAndPattern))]
    public void Every_rise_is_closed_consistently_oriented_and_exactly_as_big_as_its_voxels(string shape, string pattern)
    {
        var mesh = Rise(shape, pattern);
        MeshChecks.AssertClosed(mesh);
        MeshChecks.AssertNormalsMatchWinding(mesh);
        Assert.Equal((double)Shapes.Voxels(shape, pattern), MeshChecks.Volume(mesh));
    }

    [Theory]
    [MemberData(nameof(UnpinchedShapesUnderUniformDepth))]
    public void A_rise_with_no_corner_only_contact_is_a_two_manifold(string shape, string pattern)
    {
        MeshChecks.AssertTwoManifold(Rise(shape, pattern));
    }

    [Fact]
    public void Greedy_faces_meet_per_pixel_walls_at_t_junctions_which_is_why_edges_are_refined()
    {
        // The worked example in the plan. The merged front face walks (2,1,1)->(0,1,1) in ONE edge;
        // the two top walls walk (0,1,1)->(1,1,1) and (1,1,1)->(2,1,1). Unrefined, the long edge has
        // no partner; refined, every unit segment does.
        var mesh = Rise("strip", "paper");
        var raw = MeshChecks.RawEdges(mesh);
        var p = (From: new Point3(2, 1, 1), To: new Point3(0, 1, 1));

        Assert.Equal(1, raw.GetValueOrDefault(p));
        Assert.Equal(0, raw.GetValueOrDefault((p.To, p.From)));

        var unit = MeshChecks.UnitSegments(mesh);
        Assert.Equal(1, unit.GetValueOrDefault((new Point3(2, 1, 1), new Point3(1, 1, 1))));
        Assert.Equal(1, unit.GetValueOrDefault((new Point3(1, 1, 1), new Point3(2, 1, 1))));
        MeshChecks.AssertTwoManifold(mesh);
    }

    [Fact]
    public void Pixels_touching_only_at_a_corner_pinch_one_edge_between_four_faces()
    {
        // "#." over ".#": the two cubes share the line X=1, Y=1. It is walked up by A's bottom wall
        // and B's top wall, and down by A's right wall and B's left wall — twice each way. Closed and
        // oriented; genuinely not a 2-manifold, and no correct geometry would make it one.
        using var art = Art.FromAscii("#.", ".#");
        var mesh = Riser.TryBuild(art, null, LatticeOptions)!;
        var unit = MeshChecks.UnitSegments(mesh);

        var up = (new Point3(1, 1, 0), new Point3(1, 1, 1));
        var down = (new Point3(1, 1, 1), new Point3(1, 1, 0));
        Assert.Equal(2, unit.GetValueOrDefault(up));
        Assert.Equal(2, unit.GetValueOrDefault(down));
        MeshChecks.AssertClosed(mesh);
    }
}
```

- [ ] **Step 3: Run them to verify they fail**

Run: `dotnet test tests/Nfty.Model.Tests --filter "FullyQualifiedName~RiserTests|FullyQualifiedName~RiserSolidityTests"`
Expected: build FAILS — `Riser`, `PartialAlphaException` not found.

- [ ] **Step 4: Write the implementation**

`src/Nfty.Model/PartialAlphaException.cs`:

```csharp
using System.Globalization;

namespace Nfty.Model;

/// <summary>
/// A picture with a partly transparent pixel cannot be risen. It is refused by name rather than
/// thresholded: silently rounding would give a model whose silhouette nobody drew — at the scale of
/// one item, the "half the models are malformed" outcome the Rise spec forbids.
/// </summary>
public sealed class PartialAlphaException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="itemName">What was being risen, as a person would name it.</param>
    /// <param name="x">Column of the first partial pixel, row-major.</param>
    /// <param name="y">Row of the first partial pixel.</param>
    /// <param name="alpha">That pixel's alpha.</param>
    /// <param name="count">How many partial pixels the picture has.</param>
    public PartialAlphaException(string itemName, int x, int y, byte alpha, int count)
        : base(Describe(itemName, x, y, alpha, count))
    {
        ItemName = itemName;
        X = x;
        Y = y;
        Alpha = alpha;
        Count = count;
    }

    /// <summary>What was being risen.</summary>
    public string ItemName { get; }

    /// <summary>Column of the first partial pixel.</summary>
    public int X { get; }

    /// <summary>Row of the first partial pixel.</summary>
    public int Y { get; }

    /// <summary>That pixel's alpha, 1..254.</summary>
    public byte Alpha { get; }

    /// <summary>How many partial pixels the picture has.</summary>
    public int Count { get; }

    private static string Describe(string itemName, int x, int y, byte alpha, int count) =>
        (count == 1
            ? $"{itemName} cannot be risen: pixel ({x}, {y}) is partly transparent (alpha {alpha})."
            : $"{itemName} cannot be risen: {count.ToString("N0", CultureInfo.InvariantCulture)} pixels are "
              + $"partly transparent, the first at ({x}, {y}) with alpha {alpha}.")
        + " A voxel is either there or not, so every pixel must be fully opaque or fully clear;"
        + " nfty will not round one for you.";
}
```

`src/Nfty.Model/MeshBuilder.cs`:

```csharp
using System.Numerics;

namespace Nfty.Model;

/// <summary>The four side walls a column can expose.</summary>
internal enum WallFace
{
    /// <summary>Toward x − 1.</summary>
    Left,

    /// <summary>Toward x + 1.</summary>
    Right,

    /// <summary>Toward image row y − 1, which is world +Y.</summary>
    Top,

    /// <summary>Toward image row y + 1, which is world −Y.</summary>
    Bottom,
}

/// <summary>
/// Accumulates quads in voxel units and turns them into a <see cref="Mesh"/>. Every corner list here
/// is counter-clockwise seen from outside — checked once by hand in the plan, and by
/// <c>RiserSolidityTests</c> on every build.
/// </summary>
internal sealed class MeshBuilder
{
    private static readonly Vector3 PlusX = new(1, 0, 0), MinusX = new(-1, 0, 0);
    private static readonly Vector3 PlusY = new(0, 1, 0), MinusY = new(0, -1, 0);
    private static readonly Vector3 PlusZ = new(0, 0, 1), MinusZ = new(0, 0, -1);

    private readonly int _width;
    private readonly int _height;
    private readonly List<float> _positions = [];
    private readonly List<float> _normals = [];
    private readonly List<float> _texCoords = [];
    private readonly List<uint> _indices = [];

    /// <summary>Starts a mesh over a <paramref name="width"/> × <paramref name="height"/> image.</summary>
    public MeshBuilder(int width, int height)
    {
        _width = width;
        _height = height;
    }

    /// <summary>A front face over a merged rectangle, at z = 0.5 + its key.</summary>
    public void FrontFace(PixelRect r)
    {
        float x0 = r.X, x1 = r.X + r.Width, ylo = _height - (r.Y + r.Height), yhi = _height - r.Y;
        float z = 0.5f + r.Key;
        Quad(new(x0, ylo, z), new(x1, ylo, z), new(x1, yhi, z), new(x0, yhi, z), PlusZ,
            Uv(x0, ylo), Uv(x1, ylo), Uv(x1, yhi), Uv(x0, yhi));
    }

    /// <summary>A back face over a merged rectangle, at z = −(0.5 + its key).</summary>
    public void BackFace(PixelRect r)
    {
        float x0 = r.X, x1 = r.X + r.Width, ylo = _height - (r.Y + r.Height), yhi = _height - r.Y;
        float z = -(0.5f + r.Key);
        Quad(new(x0, ylo, z), new(x0, yhi, z), new(x1, yhi, z), new(x1, ylo, z), MinusZ,
            Uv(x0, ylo), Uv(x0, yhi), Uv(x1, yhi), Uv(x1, ylo));
    }

    /// <summary>One exposed wall segment of pixel (x, y), spanning z0 &lt; z1. Every corner samples
    /// the pixel's center, so a wall takes exactly that pixel's color.</summary>
    public void Wall(WallFace face, int x, int y, float z0, float z1)
    {
        float x0 = x, x1 = x + 1, y0 = _height - 1 - y, y1 = _height - y;
        var uv = new Vector2((x + 0.5f) / _width, (y + 0.5f) / _height);
        switch (face)
        {
            case WallFace.Left:
                Quad(new(x0, y0, z0), new(x0, y0, z1), new(x0, y1, z1), new(x0, y1, z0), MinusX, uv, uv, uv, uv);
                break;
            case WallFace.Right:
                Quad(new(x1, y0, z0), new(x1, y1, z0), new(x1, y1, z1), new(x1, y0, z1), PlusX, uv, uv, uv, uv);
                break;
            case WallFace.Top:
                Quad(new(x0, y1, z0), new(x0, y1, z1), new(x1, y1, z1), new(x1, y1, z0), PlusY, uv, uv, uv, uv);
                break;
            case WallFace.Bottom:
                Quad(new(x0, y0, z0), new(x1, y0, z0), new(x1, y0, z1), new(x0, y0, z1), MinusY, uv, uv, uv, uv);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(face), face, "Unknown wall face.");
        }
    }

    /// <summary>Applies the pivot and the voxel size, and freezes the result.</summary>
    public Mesh ToMesh(RiseOptions options)
    {
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        for (int i = 0; i < _positions.Count; i += 3)
        {
            var p = new Vector3(_positions[i], _positions[i + 1], _positions[i + 2]);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        Vector3 pivot = options.Pivot switch
        {
            Pivot.MinCorner => min,
            Pivot.Center => (min + max) / 2,
            Pivot.BottomCenter => new Vector3((min.X + max.X) / 2, min.Y, (min.Z + max.Z) / 2),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Pivot, "Unknown pivot."),
        };

        float s = options.VoxelSize;
        var positions = new float[_positions.Count];
        for (int i = 0; i < positions.Length; i += 3)
        {
            positions[i] = (_positions[i] - pivot.X) * s;
            positions[i + 1] = (_positions[i + 1] - pivot.Y) * s;
            positions[i + 2] = (_positions[i + 2] - pivot.Z) * s;
        }

        return new Mesh(positions, _normals.ToArray(), _texCoords.ToArray(), _indices.ToArray(), _width, _height);
    }

    private Vector2 Uv(float worldX, float worldY) => new(worldX / _width, (_height - worldY) / _height);

    private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal,
        Vector2 ta, Vector2 tb, Vector2 tc, Vector2 td)
    {
        uint first = checked((uint)(_positions.Count / 3));
        Vertex(a, normal, ta);
        Vertex(b, normal, tb);
        Vertex(c, normal, tc);
        Vertex(d, normal, td);
        _indices.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
    }

    private void Vertex(Vector3 p, Vector3 n, Vector2 t)
    {
        _positions.Add(p.X);
        _positions.Add(p.Y);
        _positions.Add(p.Z);
        _normals.Add(n.X);
        _normals.Add(n.Y);
        _normals.Add(n.Z);
        _texCoords.Add(t.X);
        _texCoords.Add(t.Y);
    }
}
```

`src/Nfty.Model/Riser.cs`:

```csharp
using Nfty.Core.Diagnostics;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model;

/// <summary>
/// Rises one finished picture into one solid. Every fully opaque pixel becomes a column of voxels
/// spanning z ∈ [−(0.5 + back), 0.5 + front] — the paper plus its two depths — colored with the
/// picture's own pixels. Geometry shared by the writers and the step-6 preview.
/// </summary>
/// <remarks>
/// <para><b>One solid test</b> (mixel's D1): <see cref="Opacity.IsSolid"/>. A partial pixel anywhere
/// is refused FIRST, by name; a picture with nothing solid returns <c>null</c> so a batch can skip
/// and name it (D5). The picture is taken as a live <see cref="Image{TPixel}"/>, never bytes (D6).</para>
///
/// <para><b>The surface is closed and consistently oriented, not a strict 2-manifold.</b> Greedy
/// faces meet per-pixel walls at T-junctions (every T-vertex lies exactly on the longer edge — all
/// coordinates are small integers and halves), and pixels touching only at a corner pinch four faces
/// onto one edge. <c>RiserSolidityTests</c> asserts the true property; do not "fix" it back to
/// "every edge shared by exactly two triangles", which a 2×1 strip already fails.</para>
/// </remarks>
public static class Riser
{
    private static readonly (WallFace Face, int Dx, int Dy)[] Sides =
    [
        (WallFace.Left, -1, 0),
        (WallFace.Right, 1, 0),
        (WallFace.Top, 0, -1),
        (WallFace.Bottom, 0, 1),
    ];

    private readonly record struct Scan(int Solid, int Partial, int FirstX, int FirstY, byte FirstAlpha);

    /// <summary>Builds the mesh for one picture.</summary>
    /// <param name="art">The finished picture. Not modified; the caller keeps ownership.</param>
    /// <param name="depth">Its depth map, the same size, or <c>null</c> for paper. Depth on a pixel
    /// that is not solid is ignored.</param>
    /// <param name="options">Voxel size and pivot. <see cref="RiseOptions.Format"/> is not used here.</param>
    /// <param name="source">How to name the picture in a refusal, e.g. <c>variant 'red' of hat.igt</c>.</param>
    /// <returns>The mesh, or <c>null</c> when no pixel is fully opaque.</returns>
    /// <exception cref="PartialAlphaException">Some pixel is partly transparent.</exception>
    /// <exception cref="ArgumentException">The depth map is another size.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The voxel size is not positive and finite, or the
    /// pivot is not one of <see cref="Pivot"/>'s values.</exception>
    public static Mesh? TryBuild(Image<Rgba32> art, DepthMap? depth, RiseOptions options, string source = "the art")
    {
        ArgumentNullException.ThrowIfNull(art);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (!float.IsFinite(options.VoxelSize) || options.VoxelSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(options), options.VoxelSize,
                "VoxelSize is world units per pixel and must be a positive, finite number.");
        if (!Enum.IsDefined(options.Pivot))
            throw new ArgumentOutOfRangeException(nameof(options), options.Pivot, "Unknown pivot.");
        if (depth is not null && (depth.Width != art.Width || depth.Height != art.Height))
            throw new ArgumentException(
                $"The depth map is {depth.Width}x{depth.Height}, but {source} is {art.Width}x{art.Height}.",
                nameof(depth));

        using var _ = Perf.Measure("Riser.TryBuild");

        int w = art.Width, h = art.Height;
        var front = new int[w * h];
        var back = new int[w * h];
        var scan = ScanPixels(art, depth, front, back);

        if (scan.Partial > 0)
            throw new PartialAlphaException(source, scan.FirstX, scan.FirstY, scan.FirstAlpha, scan.Partial);
        if (scan.Solid == 0)
            return null;

        var builder = new MeshBuilder(w, h);
        foreach (var r in GreedyRects.Merge(w, h, front)) builder.FrontFace(r);
        foreach (var r in GreedyRects.Merge(w, h, back)) builder.BackFace(r);
        EmitWalls(builder, w, h, front, back);
        return builder.ToMesh(options);
    }

    /// <summary>One pass: fills the front/back key grids (<see cref="GreedyRects.Unset"/> on every
    /// pixel that is not solid) and counts solid and partial pixels.</summary>
    private static Scan ScanPixels(Image<Rgba32> art, DepthMap? depth, int[] front, int[] back)
    {
        int solid = 0, partial = 0, firstX = -1, firstY = -1;
        byte firstAlpha = 0;
        int w = art.Width;
        art.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                Span<Rgba32> row = rows.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    Rgba32 p = row[x];
                    int i = (y * w) + x;
                    if (Opacity.IsPartial(p) && partial++ == 0)
                    {
                        firstX = x;
                        firstY = y;
                        firstAlpha = p.A;
                    }
                    if (Opacity.IsSolid(p))
                    {
                        solid++;
                        front[i] = depth?.Front(x, y) ?? 0;
                        back[i] = depth?.Back(x, y) ?? 0;
                    }
                    else
                    {
                        front[i] = GreedyRects.Unset;
                        back[i] = GreedyRects.Unset;
                    }
                }
            }
        });
        return new Scan(solid, partial, firstX, firstY, firstAlpha);
    }

    /// <summary>Each column's exposed side: its interval minus its neighbor's, up to two segments
    /// per side — the back one first, then the front one.</summary>
    private static void EmitWalls(MeshBuilder builder, int w, int h, int[] front, int[] back)
    {
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w) + x;
                if (front[i] == GreedyRects.Unset) continue;
                float lo = -(0.5f + back[i]), hi = 0.5f + front[i];

                foreach (var (face, dx, dy) in Sides)
                {
                    int nx = x + dx, ny = y + dy;
                    bool neighbor = nx >= 0 && ny >= 0 && nx < w && ny < h && front[(ny * w) + nx] != GreedyRects.Unset;
                    if (!neighbor)
                    {
                        builder.Wall(face, x, y, lo, hi);
                        continue;
                    }
                    int n = (ny * w) + nx;
                    float nlo = -(0.5f + back[n]), nhi = 0.5f + front[n];
                    if (lo < nlo) builder.Wall(face, x, y, lo, nlo);
                    if (nhi < hi) builder.Wall(face, x, y, nhi, hi);
                }
            }
        }
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Nfty.Model.Tests --filter "FullyQualifiedName~RiserTests|FullyQualifiedName~RiserSolidityTests"`
Expected: PASS. `RiserSolidityTests` runs 42 shape × pattern cases plus 24 two-manifold cases plus 2 facts.

If `Every_rise_is_closed...` fails, the message names the unbalanced segment: a segment walked once one way and zero times the other is a missing wall; a segment walked the same way twice is a duplicated or flipped face. Fix the geometry, never the check.

- [ ] **Step 6: Probe the anchor (mutation check, not committed)**

Temporarily delete the line `if (lo < nlo) builder.Wall(face, x, y, lo, nlo);` in `EmitWalls` (the back segment of a split wall). Run `RiserSolidityTests`: the `alternate` and `staircase` cases must FAIL on `AssertClosed`. Restore the line; rerun; PASS. Then temporarily swap `MinusX` for `PlusX` in the `Left` case of `MeshBuilder.Wall`: `AssertNormalsMatchWinding` must fail. Restore. Record both probes in the task report.

- [ ] **Step 7: Run the whole project**

Run: `dotnet test tests/Nfty.Model.Tests`
Expected: PASS, including Task 1's `No_field_in_the_assembly_can_be_written_after_construction` (the builder's lists are readonly fields; the scan's counters are closure locals).

- [ ] **Step 8: Commit**

```bash
git add src/Nfty.Model/MeshBuilder.cs src/Nfty.Model/Riser.cs src/Nfty.Model/PartialAlphaException.cs tests/Nfty.Model.Tests/Art.cs tests/Nfty.Model.Tests/Shapes.cs tests/Nfty.Model.Tests/MeshChecks.cs tests/Nfty.Model.Tests/RiserTests.cs tests/Nfty.Model.Tests/RiserSolidityTests.cs
git commit -m "feat: Riser, two-sided per-pixel depth into one closed solid

Faces are greedy rectangles of equal front and equal back; walls are the
column interval minus the neighbor's, so a column raised on both sides
past its neighbor exposes two segments. Partial alpha is refused by name
before anything else; nothing solid returns null.

The anchor asserts what is actually true: unit-lattice segments balanced
by their reverse, exact volume, normals matching winding. T-junctions and
corner pinches make the naive two-triangles-per-edge check false.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: `StackDepth.Combine` and `DepthFill.All`

**Files:**
- Create: `src/Nfty.Model/StackDepth.cs`
- Create: `src/Nfty.Model/DepthFill.cs`
- Test: `tests/Nfty.Model.Tests/StackDepthTests.cs`
- Test: `tests/Nfty.Model.Tests/DepthFillTests.cs`

**Interfaces:**
- Consumes: `Nfty.Core.Imaging.Compositor.Composite(Dimensions, IReadOnlyList<Image<Rgba32>>)`, `Opacity.IsSolid`, `DepthMap.FromBytes/Front/Back`, `Perf.Measure`.
- Produces:
  ```csharp
  public static class StackDepth
  {
      public static (Image<Rgba32> Art, DepthMap Depth) Combine(IReadOnlyList<(Image<Rgba32> Layer, DepthMap? Depth)> bottomFirst);
  }
  public static class DepthFill
  {
      public static DepthMap All(Image<Rgba32> art, byte front, byte back);
  }
  ```

- [ ] **Step 1: Write the failing tests**

`tests/Nfty.Model.Tests/StackDepthTests.cs`:

```csharp
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

public class StackDepthTests
{
    private static readonly Rgba32 Red = new(200, 0, 0, 255);
    private static readonly Rgba32 Blue = new(0, 0, 200, 255);
    private static readonly Rgba32 Clear = new(0, 0, 0, 0);

    private static Image<Rgba32> Row(params Rgba32[] pixels)
    {
        var img = new Image<Rgba32>(pixels.Length, 1);
        for (int x = 0; x < pixels.Length; x++) img[x, 0] = pixels[x];
        return img;
    }

    private static DepthMap Map(params byte[] frontBackPairs) =>
        DepthMap.FromBytes(frontBackPairs.Length / 2, 1, frontBackPairs);

    [Fact]
    public void The_topmost_layer_solid_at_a_pixel_owns_its_depth()
    {
        using var bottom = Row(Red, Red);
        using var top = Row(Blue, Clear);

        var (art, depth) = StackDepth.Combine([(bottom, Map(1, 0, 1, 0)), (top, Map(3, 2, 0, 0))]);
        using (art)
        {
            Assert.Equal((3, 2), (depth.Front(0, 0), depth.Back(0, 0)));   // top is solid there
            Assert.Equal((1, 0), (depth.Front(1, 0), depth.Back(1, 0)));   // top is clear: defers down
        }
    }

    [Fact]
    public void A_solid_top_pixel_with_no_map_is_paper_even_over_a_raised_layer()
    {
        // "Topmost solid layer wins" includes a layer nobody raised: it covers the pixel, so it owns
        // the pixel's depth, and its depth is paper.
        using var bottom = Row(Red, Red);
        using var top = Row(Blue, Clear);

        var (art, depth) = StackDepth.Combine([(bottom, Map(4, 4, 4, 4)), (top, null)]);
        using (art)
        {
            Assert.Equal((0, 0), (depth.Front(0, 0), depth.Back(0, 0)));
            Assert.Equal((4, 4), (depth.Front(1, 0), depth.Back(1, 0)));
        }
    }

    [Fact]
    public void A_pixel_no_layer_covers_carries_no_depth()
    {
        using var only = Row(Red, Clear);
        var (art, depth) = StackDepth.Combine([(only, Map(0, 0, 4, 4))]);
        using (art)
        {
            Assert.Equal((0, 0), (depth.Front(1, 0), depth.Back(1, 0)));
            Assert.Equal(Clear, art[1, 0]);
        }
    }

    [Fact]
    public void A_partial_pixel_is_not_solid_so_it_owns_no_depth()
    {
        // Riser refuses the partial pixel later, by name; the combine rule just never gives it depth.
        using var half = Row(new Rgba32(200, 0, 0, 128));
        var (art, depth) = StackDepth.Combine([(half, Map(2, 2))]);
        using (art)
            Assert.True(depth.IsFlat);
    }

    [Fact]
    public void The_art_is_exactly_what_the_compositor_draws()
    {
        using var bottom = Row(Red, Red, Clear);
        using var top = Row(Blue, Clear, Clear);
        using var expected = Compositor.Composite(new Dimensions(3, 1), [bottom, top]);

        var (art, _) = StackDepth.Combine([(bottom, null), (top, null)]);
        using (art)
            for (int x = 0; x < 3; x++)
                Assert.Equal(expected[x, 0], art[x, 0]);
    }

    [Fact]
    public void An_empty_stack_is_refused()
    {
        Assert.Throws<ArgumentException>(() => StackDepth.Combine([]));
    }

    [Fact]
    public void Layers_of_different_sizes_are_refused()
    {
        using var two = Row(Red, Red);
        using var three = Row(Red, Red, Red);
        Assert.Throws<ArgumentException>(() => StackDepth.Combine([(two, null), (three, null)]));
    }

    [Fact]
    public void A_depth_map_of_another_size_is_refused()
    {
        using var two = Row(Red, Red);
        Assert.Throws<ArgumentException>(() => StackDepth.Combine([(two, Map(1, 1))]));
    }
}
```

`tests/Nfty.Model.Tests/DepthFillTests.cs`:

```csharp
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

public class DepthFillTests
{
    [Fact]
    public void Every_solid_pixel_gets_the_fill_and_nothing_else_does()
    {
        using var art = new Image<Rgba32>(3, 1);
        art[0, 0] = new Rgba32(1, 2, 3, 255);
        art[1, 0] = new Rgba32(0, 0, 0, 0);
        art[2, 0] = new Rgba32(1, 2, 3, 128);

        var map = DepthFill.All(art, front: 3, back: 1);

        Assert.Equal((3, 1), (map.Front(0, 0), map.Back(0, 0)));
        Assert.Equal((0, 0), (map.Front(1, 0), map.Back(1, 0)));
        Assert.Equal((0, 0), (map.Front(2, 0), map.Back(2, 0)));
    }

    [Fact]
    public void A_fill_of_zero_is_paper()
    {
        using var art = new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255));
        Assert.True(DepthFill.All(art, 0, 0).IsFlat);
    }

    [Fact]
    public void The_fill_is_sized_to_the_art()
    {
        using var art = new Image<Rgba32>(4, 3, new Rgba32(1, 2, 3, 255));
        var map = DepthFill.All(art, 1, 1);
        Assert.Equal((4, 3), (map.Width, map.Height));
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/Nfty.Model.Tests --filter "FullyQualifiedName~StackDepthTests|FullyQualifiedName~DepthFillTests"`
Expected: build FAILS — `StackDepth`, `DepthFill` not found.

- [ ] **Step 3: Write the implementation**

`src/Nfty.Model/StackDepth.cs`:

```csharp
using Nfty.Core.Diagnostics;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model;

/// <summary>
/// The stack rule, in one place: a layered picture is risen as ONE solid, never as stacked slabs
/// (slabs z-fight wherever two layers cover a pixel).
/// </summary>
/// <remarks>
/// The art is the compositor's own composite. A pixel of it is solid by <see cref="Opacity.IsSolid"/>,
/// and a solid pixel takes its depth from the TOPMOST layer whose own pixel is solid there, walking
/// the stack top-down; a layer with no map there is paper. In a book with no partial alpha every
/// layer pixel is 0 or 255, so the composite is solid exactly where some layer is and the two tests
/// cannot disagree. Where they could (partial layers), the composite is not solid and gets no depth —
/// <see cref="Riser"/> refuses such a picture by name anyway.
/// </remarks>
public static class StackDepth
{
    /// <summary>Composites a stack and combines its depth.</summary>
    /// <param name="bottomFirst">The layers in paint order (index 0 paints first, sits furthest
    /// back), each with its own depth map or <c>null</c> for paper. All the same size.</param>
    /// <returns>The composited art — the caller owns and disposes it — and its depth map.</returns>
    /// <exception cref="ArgumentException">The stack is empty, or a layer or map is another size.</exception>
    public static (Image<Rgba32> Art, DepthMap Depth) Combine(IReadOnlyList<(Image<Rgba32> Layer, DepthMap? Depth)> bottomFirst)
    {
        ArgumentNullException.ThrowIfNull(bottomFirst);
        if (bottomFirst.Count == 0)
            throw new ArgumentException("A stack needs at least one layer.", nameof(bottomFirst));

        int w = bottomFirst[0].Layer.Width, h = bottomFirst[0].Layer.Height;
        for (int i = 0; i < bottomFirst.Count; i++)
        {
            var (layer, map) = bottomFirst[i];
            ArgumentNullException.ThrowIfNull(layer);
            if (layer.Width != w || layer.Height != h)
                throw new ArgumentException(
                    $"Layer {i + 1} is {layer.Width}x{layer.Height}; the stack is {w}x{h}.", nameof(bottomFirst));
            if (map is not null && (map.Width != w || map.Height != h))
                throw new ArgumentException(
                    $"Layer {i + 1}'s depth map is {map.Width}x{map.Height}; the stack is {w}x{h}.", nameof(bottomFirst));
        }

        using var _ = Perf.Measure("StackDepth.Combine");

        var art = Compositor.Composite(new Dimensions(w, h), bottomFirst.Select(l => l.Layer).ToArray());
        try
        {
            var bytes = new byte[checked(w * h * 2)];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!Opacity.IsSolid(art[x, y])) continue;
                    for (int i = bottomFirst.Count - 1; i >= 0; i--)
                    {
                        var (layer, map) = bottomFirst[i];
                        if (!Opacity.IsSolid(layer[x, y])) continue;
                        if (map is not null)
                        {
                            int k = ((y * w) + x) * 2;
                            bytes[k] = map.Front(x, y);
                            bytes[k + 1] = map.Back(x, y);
                        }
                        break;
                    }
                }
            }
            return (art, DepthMap.FromBytes(w, h, bytes));
        }
        catch
        {
            art.Dispose();
            throw;
        }
    }
}
```

`src/Nfty.Model/DepthFill.cs`:

```csharp
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model;

/// <summary>
/// Fill-all: the one action that replaced the old Front / Symmetric / Raised modes. It sets every
/// solid pixel of a picture to the same (front, back) at once.
/// </summary>
public static class DepthFill
{
    /// <summary>A depth map giving every solid pixel of <paramref name="art"/> the same depth.</summary>
    /// <param name="art">The picture. Solid means <see cref="Opacity.IsSolid"/>.</param>
    /// <param name="front">Voxels toward the viewer, 0..255.</param>
    /// <param name="back">Voxels away from the viewer, 0..255.</param>
    /// <returns>A map the size of the art: (front, back) on every solid pixel, zero elsewhere. It
    /// REPLACES any stored map — fill-all touches every solid pixel, so nothing of the old one
    /// survives it — which is why this takes none.</returns>
    public static DepthMap All(Image<Rgba32> art, byte front, byte back)
    {
        ArgumentNullException.ThrowIfNull(art);
        var bytes = new byte[checked(art.Width * art.Height * 2)];
        for (int y = 0; y < art.Height; y++)
        {
            for (int x = 0; x < art.Width; x++)
            {
                if (!Opacity.IsSolid(art[x, y])) continue;
                int i = ((y * art.Width) + x) * 2;
                bytes[i] = front;
                bytes[i + 1] = back;
            }
        }
        return DepthMap.FromBytes(art.Width, art.Height, bytes);
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Nfty.Model.Tests --filter "FullyQualifiedName~StackDepthTests|FullyQualifiedName~DepthFillTests"`
Expected: PASS — 11.

- [ ] **Step 5: Probe (not committed)**

Temporarily reverse the layer walk in `Combine` (`for (int i = 0; i < bottomFirst.Count; i++)`). `The_topmost_layer_solid_at_a_pixel_owns_its_depth` and `A_solid_top_pixel_with_no_map_is_paper...` must FAIL. Restore; PASS. Record it.

- [ ] **Step 6: Commit**

```bash
git add src/Nfty.Model/StackDepth.cs src/Nfty.Model/DepthFill.cs tests/Nfty.Model.Tests/StackDepthTests.cs tests/Nfty.Model.Tests/DepthFillTests.cs
git commit -m "feat: the stack rule and fill-all

StackDepth.Combine composites through Core's own Compositor and gives
each solid pixel the depth of the topmost layer solid there; a clear top
pixel defers downward. DepthFill.All sets every solid pixel at once.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: The glTF object model, the GLB writer and the structure validator

**Files:**
- Create: `src/Nfty.Model/Gltf/GltfDocument.cs`
- Create: `src/Nfty.Model/Gltf/GltfJson.cs`
- Create: `src/Nfty.Model/Gltf/GltfBuilder.cs`
- Create: `src/Nfty.Model/Gltf/GlbContainer.cs`
- Create: `src/Nfty.Model/ModelWriter.cs`
- Modify: `src/Nfty.Model/RiseOptions.cs` (the `<c>ModelWriter</c>` note becomes `<see cref="ModelWriter"/>`)
- Create: `tests/Nfty.Model.Tests/GltfValidator.cs`
- Create: `tests/Nfty.Model.Tests/Models.cs`
- Test: `tests/Nfty.Model.Tests/GlbWriterTests.cs`

**Interfaces:**
- Consumes: `Mesh` (Task 1), `Riser.TryBuild` (Task 3, in tests), `RiseOptions`, `ModelFile`.
- Produces:
  ```csharp
  public static class ModelWriter
  {
      // One entry for Glb (and, from Task 6, GltfEmbedded); two (.gltf then .bin) for GltfWithBin.
      public static IReadOnlyList<ModelFile> Write(Mesh mesh, Image<Rgba32> texture, RiseOptions options, string baseName);
  }
  // internal, namespace Nfty.Model.Gltf:
  internal sealed record GltfRoot(...);  // see code; Buffers[0].Uri is the ONLY field that differs between formats
  internal static class GltfBuilder { public static (GltfRoot Root, byte[] Bin) Build(Mesh mesh, byte[] png); }
  internal static class GltfJson { public static byte[] Serialize(GltfRoot root); }
  internal static class GlbContainer { public static byte[] Frame(byte[] json, byte[] bin); public static int Pad4(int n); }
  ```
- Test helpers Task 6 reuses: `GltfValidator.ReadGlb(byte[])`, `GltfValidator.ReadGltf(IReadOnlyList<ModelFile>)`, `GltfValidator.AssertValid(JsonNode, byte[])`, `GltfValidator.ReadFloats`, `GltfValidator.Reframe`, `Models.Write`, `Models.Glb`.

**The document, one fixed shape.** BIN layout, each section starting on a 4-byte boundary by construction (every section before the PNG is a multiple of 4 bytes): positions (`12 × V`), normals (`12 × V`), texcoords (`8 × V`), indices (`4 × 3T`), PNG, then zero padding to 4.

| Index | bufferView | target | accessor | componentType | type |
|---|---|---|---|---|---|
| 0 | positions | 34962 `ARRAY_BUFFER` | `POSITION`, with `min`/`max` | 5126 `FLOAT` | VEC3 |
| 1 | normals | 34962 | `NORMAL` | 5126 | VEC3 |
| 2 | texcoords | 34962 | `TEXCOORD_0` | 5126 | VEC2 |
| 3 | indices | 34963 `ELEMENT_ARRAY_BUFFER` | indices | 5125 `UNSIGNED_INT` | SCALAR |
| 4 | PNG | **none** | — | — | — |

One scene, one node (`mesh 0`), one mesh, one primitive (`mode 4` TRIANGLES), one material named `nfty` (`pbrMetallicRoughness.baseColorTexture 0`, `metallicFactor 0`, `roughnessFactor 1`, `alphaMode "MASK"`, `alphaCutoff 0.5`, **no `doubleSided`**), one texture (`sampler 0`, `source 0`), one sampler (`magFilter`/`minFilter` 9728 `NEAREST`, `wrapS`/`wrapT` 33071 `CLAMP_TO_EDGE`), one image (`bufferView 4`, `mimeType "image/png"`), one buffer (`byteLength` = BIN length, no `uri` in the GLB). `asset` is `{"version":"2.0","generator":"nfty"}` — no version number, so a release cannot change a model's bytes.

GLB container: `magic 0x46546C67 | version 2 | total length`, then `JSON` chunk (`0x4E4F534A`) padded with `0x20`, then `BIN` chunk (`0x004E4942`) padded with `0x00`. All little-endian.

- [ ] **Step 1: Write the test helpers**

`tests/Nfty.Model.Tests/Models.cs`:

```csharp
using Nfty.Core.Model;

namespace Nfty.Model.Tests;

/// <summary>Rise-and-write in one call, for the writer tests.</summary>
internal static class Models
{
    public static IReadOnlyList<ModelFile> Write(string[] rows, Func<int, int, (int Front, int Back)>? depth = null,
        RiseOptions? options = null, string baseName = "sprite")
    {
        options ??= new RiseOptions();
        using var art = Art.FromAscii(rows);
        DepthMap? map = depth is null ? null : Art.Depth(art, depth);
        var mesh = Riser.TryBuild(art, map, options) ?? throw new InvalidOperationException("nothing solid");
        return ModelWriter.Write(mesh, art, options, baseName);
    }

    public static byte[] Glb(string[] rows, Func<int, int, (int Front, int Back)>? depth = null) =>
        Assert.Single(Write(rows, depth, new RiseOptions { Format = ModelFormat.Glb })).Bytes;
}
```

`tests/Nfty.Model.Tests/GltfValidator.cs`:

```csharp
using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;

namespace Nfty.Model.Tests;

/// <summary>
/// A validator, not a round-trip. With no glTF library there is nothing to re-open a model with, so
/// this parses the container itself and asserts what the glTF 2.0 spec requires of the one document
/// shape nfty writes. A test that only checks a file starts with "glTF" proves nothing; this list IS
/// the safety a library would have given, and <c>The_validator_is_not_vacuous</c> proves each check
/// can fail.
/// </summary>
internal static class GltfValidator
{
    public const uint Magic = 0x46546C67;
    public const uint JsonChunk = 0x4E4F534A;
    public const uint BinChunk = 0x004E4942;
    public const string DataUriPrefix = "data:application/octet-stream;base64,";
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static uint U32(byte[] b, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(offset));

    /// <summary>Checks the GLB container and returns its JSON and BIN chunk.</summary>
    public static (JsonNode Json, byte[] Bin) ReadGlb(byte[] glb)
    {
        Assert.True(glb.Length >= 28, "shorter than a header and two chunk headers");
        Assert.Equal(Magic, U32(glb, 0));
        Assert.Equal(2u, U32(glb, 4));
        Assert.Equal((uint)glb.Length, U32(glb, 8));

        int jsonLength = (int)U32(glb, 12);
        Assert.Equal(JsonChunk, U32(glb, 16));
        Assert.Equal(0, jsonLength % 4);
        byte[] jsonChunk = glb.AsSpan(20, jsonLength).ToArray();
        int end = Array.LastIndexOf(jsonChunk, (byte)'}') + 1;
        Assert.True(end > 0, "the JSON chunk holds no object");
        Assert.All(jsonChunk[end..], b => Assert.Equal((byte)0x20, b));   // padded with SPACES

        int binHeader = 20 + jsonLength;
        int binLength = (int)U32(glb, binHeader);
        Assert.Equal(BinChunk, U32(glb, binHeader + 4));
        Assert.Equal(0, binLength % 4);
        Assert.Equal(glb.Length, binHeader + 8 + binLength);

        var json = JsonNode.Parse(jsonChunk.AsSpan(0, end)) ?? throw new InvalidOperationException("null JSON");
        Assert.Null(json["buffers"]?[0]?["uri"]);   // a GLB's buffer IS the BIN chunk
        return (json, glb.AsSpan(binHeader + 8, binLength).ToArray());
    }

    /// <summary>Reads a text glTF and resolves its buffer — a sibling file or a data URI.</summary>
    public static (JsonNode Json, byte[] Bin) ReadGltf(IReadOnlyList<ModelFile> files)
    {
        var gltf = Assert.Single(files, f => f.FileName.EndsWith(".gltf", StringComparison.Ordinal));
        Assert.Equal((byte)'{', gltf.Bytes[0]);   // UTF-8, no BOM
        var json = JsonNode.Parse(gltf.Bytes) ?? throw new InvalidOperationException("null JSON");
        string uri = json["buffers"]![0]!["uri"]!.GetValue<string>();
        byte[] bin = uri.StartsWith(DataUriPrefix, StringComparison.Ordinal)
            ? Convert.FromBase64String(uri[DataUriPrefix.Length..])
            : Assert.Single(files, f => f.FileName == Uri.UnescapeDataString(uri)).Bytes;
        return (json, bin);
    }

    /// <summary>Every spec-compliance point the Rise spec lists, and the invariants behind them.</summary>
    public static void AssertValid(JsonNode root, byte[] bin)
    {
        Assert.Equal("2.0", root["asset"]!["version"]!.GetValue<string>());
        Assert.Equal("nfty", root["asset"]!["generator"]!.GetValue<string>());
        Assert.Equal(0, root["scene"]!.GetValue<int>());
        Assert.Equal(0, root["scenes"]![0]!["nodes"]![0]!.GetValue<int>());
        Assert.Equal(0, root["nodes"]![0]!["mesh"]!.GetValue<int>());

        var buffers = root["buffers"]!.AsArray();
        Assert.Single(buffers);
        int bufferLength = buffers[0]!["byteLength"]!.GetValue<int>();
        Assert.InRange(bin.Length - bufferLength, 0, 3);

        // Buffer views: inside the buffer, 4-aligned, disjoint; every byte no view claims is padding
        // and must be zero.
        var views = root["bufferViews"]!.AsArray();
        var covered = new bool[bin.Length];
        foreach (var view in views)
        {
            Assert.Equal(0, view!["buffer"]!.GetValue<int>());
            int offset = view["byteOffset"]?.GetValue<int>() ?? 0;
            int length = view["byteLength"]!.GetValue<int>();
            Assert.True(length > 0, "empty bufferView");
            Assert.True(offset % 4 == 0, $"bufferView at {offset} is not 4-byte aligned");
            Assert.True(offset + length <= bufferLength, $"bufferView {offset}+{length} overruns a {bufferLength}-byte buffer");
            for (int i = offset; i < offset + length; i++)
            {
                Assert.False(covered[i], $"two bufferViews claim byte {i}");
                covered[i] = true;
            }
        }
        for (int i = 0; i < bin.Length; i++)
            if (!covered[i])
                Assert.Equal(0, bin[i]);

        // Accessors: only FLOAT and UNSIGNED_INT, each starting on its component size, inside its view.
        var accessors = root["accessors"]!.AsArray();
        foreach (var a in accessors)
        {
            int componentType = a!["componentType"]!.GetValue<int>();
            Assert.Contains(componentType, new[] { 5126, 5125 });
            var view = views[a["bufferView"]!.GetValue<int>()]!;
            int accessorOffset = a["byteOffset"]?.GetValue<int>() ?? 0;
            int start = (view["byteOffset"]?.GetValue<int>() ?? 0) + accessorOffset;
            Assert.True(start % 4 == 0, $"accessor starts at {start}, not on its 4-byte component boundary");
            int count = a["count"]!.GetValue<int>();
            Assert.True(count > 0, "empty accessor");
            Assert.True(accessorOffset + (count * Components(a["type"]!.GetValue<string>()) * 4) <= view["byteLength"]!.GetValue<int>(),
                "accessor overruns its bufferView");
        }

        var primitive = root["meshes"]![0]!["primitives"]![0]!;
        Assert.Equal(4, primitive["mode"]!.GetValue<int>());
        var attributes = primitive["attributes"]!;
        int position = attributes["POSITION"]!.GetValue<int>();
        int normal = attributes["NORMAL"]!.GetValue<int>();
        int uv = attributes["TEXCOORD_0"]!.GetValue<int>();
        int vertexCount = accessors[position]!["count"]!.GetValue<int>();
        Assert.Equal("VEC3", accessors[position]!["type"]!.GetValue<string>());
        Assert.Equal("VEC3", accessors[normal]!["type"]!.GetValue<string>());
        Assert.Equal("VEC2", accessors[uv]!["type"]!.GetValue<string>());
        Assert.Equal(vertexCount, accessors[normal]!["count"]!.GetValue<int>());
        Assert.Equal(vertexCount, accessors[uv]!["count"]!.GetValue<int>());
        foreach (int attribute in new[] { position, normal, uv })
            Assert.Equal(34962, views[accessors[attribute]!["bufferView"]!.GetValue<int>()]!["target"]!.GetValue<int>());

        // POSITION MUST carry min and max, and they must be the real extents: a viewer culls by them.
        var min = accessors[position]!["min"]?.AsArray();
        var max = accessors[position]!["max"]?.AsArray();
        Assert.NotNull(min);
        Assert.NotNull(max);
        float[] p = ReadFloats(root, bin, position);
        for (int axis = 0; axis < 3; axis++)
        {
            var values = p.Where((_, i) => i % 3 == axis).ToArray();
            Assert.Equal(values.Min(), min[axis]!.GetValue<float>());
            Assert.Equal(values.Max(), max[axis]!.GetValue<float>());
        }

        // Indices: UNSIGNED_INT, whole triangles, every one a real vertex.
        var indices = accessors[primitive["indices"]!.GetValue<int>()]!;
        Assert.Equal(5125, indices["componentType"]!.GetValue<int>());
        Assert.Equal("SCALAR", indices["type"]!.GetValue<string>());
        Assert.Equal(0, indices["count"]!.GetValue<int>() % 3);
        Assert.Equal(34963, views[indices["bufferView"]!.GetValue<int>()]!["target"]!.GetValue<int>());
        Assert.All(ReadUInts(root, bin, primitive["indices"]!.GetValue<int>()), i => Assert.True(i < (uint)vertexCount));

        // Material: named for nfty (D4), single-sided (D3), alpha as a mask.
        var material = root["materials"]![primitive["material"]!.GetValue<int>()]!;
        Assert.Contains("nfty", material["name"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.False(material["doubleSided"]?.GetValue<bool>() ?? false, "D3: a closed solid is single-sided");
        Assert.Equal("MASK", material["alphaMode"]!.GetValue<string>());
        Assert.Equal(0.5f, material["alphaCutoff"]!.GetValue<float>());

        // Texture: NEAREST and CLAMP_TO_EDGE, so pixel art stays crisp; the PNG in a target-less view.
        int textureIndex = material["pbrMetallicRoughness"]!["baseColorTexture"]!["index"]!.GetValue<int>();
        var texture = root["textures"]![textureIndex]!;
        var sampler = root["samplers"]![texture["sampler"]!.GetValue<int>()]!;
        Assert.Equal(9728, sampler["magFilter"]!.GetValue<int>());
        Assert.Equal(9728, sampler["minFilter"]!.GetValue<int>());
        Assert.Equal(33071, sampler["wrapS"]!.GetValue<int>());
        Assert.Equal(33071, sampler["wrapT"]!.GetValue<int>());
        var image = root["images"]![texture["source"]!.GetValue<int>()]!;
        Assert.Equal("image/png", image["mimeType"]!.GetValue<string>());
        Assert.Null(image["uri"]);
        var imageView = views[image["bufferView"]!.GetValue<int>()]!;
        Assert.Null(imageView["target"]);
        int imageOffset = imageView["byteOffset"]?.GetValue<int>() ?? 0;
        Assert.Equal(PngSignature, bin.AsSpan(imageOffset, 8).ToArray());
    }

    public static float[] ReadFloats(JsonNode root, byte[] bin, int accessor)
    {
        var a = root["accessors"]![accessor]!;
        var view = root["bufferViews"]![a["bufferView"]!.GetValue<int>()]!;
        int start = (view["byteOffset"]?.GetValue<int>() ?? 0) + (a["byteOffset"]?.GetValue<int>() ?? 0);
        int n = a["count"]!.GetValue<int>() * Components(a["type"]!.GetValue<string>());
        var values = new float[n];
        for (int i = 0; i < n; i++)
            values[i] = BinaryPrimitives.ReadSingleLittleEndian(bin.AsSpan(start + (i * 4)));
        return values;
    }

    public static uint[] ReadUInts(JsonNode root, byte[] bin, int accessor)
    {
        var a = root["accessors"]![accessor]!;
        var view = root["bufferViews"]![a["bufferView"]!.GetValue<int>()]!;
        int start = (view["byteOffset"]?.GetValue<int>() ?? 0) + (a["byteOffset"]?.GetValue<int>() ?? 0);
        int n = a["count"]!.GetValue<int>();
        var values = new uint[n];
        for (int i = 0; i < n; i++)
            values[i] = BinaryPrimitives.ReadUInt32LittleEndian(bin.AsSpan(start + (i * 4)));
        return values;
    }

    public static byte[] ReadImage(JsonNode root, byte[] bin)
    {
        var view = root["bufferViews"]![root["images"]![0]!["bufferView"]!.GetValue<int>()]!;
        int offset = view["byteOffset"]?.GetValue<int>() ?? 0;
        return bin.AsSpan(offset, view["byteLength"]!.GetValue<int>()).ToArray();
    }

    /// <summary>Re-frames a GLB after editing its JSON — how the validator's own tests plant a defect.</summary>
    public static byte[] Reframe(byte[] glb, Action<JsonNode> edit)
    {
        var (json, bin) = ReadGlb(glb);
        edit(json);
        byte[] text = Encoding.UTF8.GetBytes(json.ToJsonString());
        int jsonLength = (text.Length + 3) & ~3;
        var result = new byte[28 + jsonLength + bin.Length];
        var span = result.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)result.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], (uint)jsonLength);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], JsonChunk);
        text.CopyTo(span[20..]);
        span.Slice(20 + text.Length, jsonLength - text.Length).Fill(0x20);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(20 + jsonLength)..], (uint)bin.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(24 + jsonLength)..], BinChunk);
        bin.CopyTo(span[(28 + jsonLength)..]);
        return result;
    }

    private static int Components(string type)
    {
        switch (type)
        {
            case "SCALAR": return 1;
            case "VEC2": return 2;
            case "VEC3": return 3;
            default:
                Assert.Fail($"unexpected accessor type '{type}'");
                return 0;
        }
    }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/Nfty.Model.Tests/GlbWriterTests.cs`:

```csharp
using System.Reflection;
using System.Text.Json.Nodes;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

public class GlbWriterTests
{
    private static readonly string[] Plus = [".#.", "###", ".#."];

    public static TheoryData<string, string> EveryShapeAndPattern() => Shapes.EveryShapeAndPattern();

    [Fact]
    public void A_glb_is_one_file_named_after_the_base_name()
    {
        var file = Assert.Single(Models.Write(Plus, baseName: "chest"));
        Assert.Equal("chest.glb", file.FileName);
    }

    [Theory]
    [MemberData(nameof(EveryShapeAndPattern))]
    public void Every_glb_passes_the_structure_validator(string shape, string pattern)
    {
        var (json, bin) = GltfValidator.ReadGlb(Models.Glb(Shapes.All[shape], Shapes.Patterns[pattern]));
        GltfValidator.AssertValid(json, bin);
    }

    [Fact]
    public void Position_min_and_max_are_the_meshs_own_bounds()
    {
        using var art = Art.FromAscii(Plus);
        var mesh = Riser.TryBuild(art, Art.Depth(art, (x, _) => (x, 1)), new RiseOptions())!;
        var (json, _) = GltfValidator.ReadGlb(Assert.Single(ModelWriter.Write(mesh, art, new RiseOptions(), "p")).Bytes);

        var position = json["accessors"]![0]!;
        Assert.Equal(new[] { mesh.Min.X, mesh.Min.Y, mesh.Min.Z }, position["min"]!.AsArray().Select(n => n!.GetValue<float>()));
        Assert.Equal(new[] { mesh.Max.X, mesh.Max.Y, mesh.Max.Z }, position["max"]!.AsArray().Select(n => n!.GetValue<float>()));
        Assert.Equal(mesh.VertexCount, position["count"]!.GetValue<int>());
    }

    [Fact]
    public void Indices_are_unsigned_int_whatever_the_mesh_size()
    {
        var (json, bin) = GltfValidator.ReadGlb(Models.Glb(["#"]));
        Assert.Equal(5125, json["accessors"]![3]!["componentType"]!.GetValue<int>());
        Assert.Equal(36, GltfValidator.ReadUInts(json, bin, 3).Length);
    }

    [Fact]
    public void The_material_is_single_sided_and_named_for_nfty()
    {
        // D3: a closed solid shows no back face; double-sided only disables culling. D4: the
        // reference implementation named its material after itself, baked into every file.
        var (json, _) = GltfValidator.ReadGlb(Models.Glb(Plus));
        var material = json["materials"]![0]!;
        Assert.Null(material["doubleSided"]);
        Assert.Equal("nfty", material["name"]!.GetValue<string>());
        Assert.Equal("nfty", json["meshes"]![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void The_sampler_is_nearest_and_clamped_so_pixel_art_stays_crisp()
    {
        var (json, _) = GltfValidator.ReadGlb(Models.Glb(Plus));
        var sampler = json["samplers"]![0]!;
        Assert.Equal((9728, 9728, 33071, 33071), (
            sampler["magFilter"]!.GetValue<int>(), sampler["minFilter"]!.GetValue<int>(),
            sampler["wrapS"]!.GetValue<int>(), sampler["wrapT"]!.GetValue<int>()));
    }

    [Fact]
    public void Alpha_is_a_mask_cut_at_one_half()
    {
        var (json, _) = GltfValidator.ReadGlb(Models.Glb(Plus));
        Assert.Equal("MASK", json["materials"]![0]!["alphaMode"]!.GetValue<string>());
        Assert.Equal(0.5f, json["materials"]![0]!["alphaCutoff"]!.GetValue<float>());
    }

    [Fact]
    public void The_png_sits_in_a_buffer_view_with_no_target()
    {
        var (json, _) = GltfValidator.ReadGlb(Models.Glb(Plus));
        var view = json["bufferViews"]![json["images"]![0]!["bufferView"]!.GetValue<int>()]!;
        Assert.Null(view["target"]);
        Assert.Equal("image/png", json["images"]![0]!["mimeType"]!.GetValue<string>());
    }

    [Fact]
    public void Both_chunks_are_padded_to_four_bytes()
    {
        // A 1x1 sprite makes an odd-sized PNG and a JSON of arbitrary length; both must be padded.
        byte[] glb = Models.Glb(["#"]);
        Assert.Equal(0, glb.Length % 4);
        GltfValidator.ReadGlb(glb);
    }

    [Fact]
    public void The_texture_is_the_art_unaltered()
    {
        // nfty resamples, blurs and recolors nothing; the texture is the whole picture, pixel for pixel.
        using var art = Art.FromAscii(".#.", "###");
        var (json, bin) = GltfValidator.ReadGlb(Models.Glb([".#.", "###"]));
        using var texture = Image.Load<Rgba32>(GltfValidator.ReadImage(json, bin));

        Assert.Equal((art.Width, art.Height), (texture.Width, texture.Height));
        for (int y = 0; y < art.Height; y++)
            for (int x = 0; x < art.Width; x++)
                Assert.Equal(art[x, y], texture[x, y]);
    }

    [Fact]
    public void The_same_pixels_give_the_same_file_whatever_metadata_the_picture_carried()
    {
        // A picture decoded from disk carries its PNG's metadata — color type, bit depth, text, a
        // resolution. None of it is the picture, so none of it may reach the model's bytes.
        var options = new RiseOptions();
        using var plain = Art.FromAscii(Plus);
        using var decorated = plain.Clone();
        decorated.Metadata.HorizontalResolution = 300;
        decorated.Metadata.VerticalResolution = 300;
        var png = decorated.Metadata.GetPngMetadata();
        png.ColorType = PngColorType.Palette;
        png.BitDepth = PngBitDepth.Bit4;
        png.TextData.Add(new PngTextData("Comment", "made elsewhere", "", ""));

        var mesh = Riser.TryBuild(plain, null, options)!;
        Assert.Equal(
            Assert.Single(ModelWriter.Write(mesh, plain, options, "a")).Bytes,
            Assert.Single(ModelWriter.Write(mesh, decorated, options, "a")).Bytes);
    }

    [Fact]
    public void The_same_input_gives_byte_identical_files()
    {
        // Owning the writer is what makes this assertable at all: no version stamp, no timestamp,
        // no hash-ordered collection anywhere on the path.
        Func<int, int, (int, int)> depth = (x, y) => (x, y);
        Assert.Equal(Models.Glb(Shapes.All["hole"], depth), Models.Glb(Shapes.All["hole"], depth));
    }

    [Fact]
    public void A_texture_of_another_size_is_refused()
    {
        using var art = Art.FromAscii("##");
        using var other = Art.FromAscii("###");
        var mesh = Riser.TryBuild(art, null, new RiseOptions())!;
        Assert.Throws<ArgumentException>(() => ModelWriter.Write(mesh, other, new RiseOptions(), "a"));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("")]
    [InlineData(" ")]
    public void A_base_name_that_is_not_a_plain_file_name_is_refused(string baseName)
    {
        using var art = Art.FromAscii("#");
        var mesh = Riser.TryBuild(art, null, new RiseOptions())!;
        Assert.ThrowsAny<ArgumentException>(() => ModelWriter.Write(mesh, art, new RiseOptions(), baseName));
    }

    [Fact]
    public void Every_entry_point_takes_live_images_never_bytes()
    {
        // D6: the reference implementation took PNG bytes and decoded per call. At cook time the image
        // is already in memory; a PNG round-trip per asset is pure waste on the hot path.
        var methods = new[] { typeof(Riser), typeof(ModelWriter), typeof(StackDepth), typeof(DepthFill) }
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.All(methods, m => Assert.DoesNotContain(m.GetParameters(), p =>
            p.ParameterType == typeof(byte[]) || p.ParameterType == typeof(ReadOnlySpan<byte>) || p.ParameterType == typeof(Stream)));
        Assert.Equal(typeof(Image<Rgba32>), typeof(Riser).GetMethod(nameof(Riser.TryBuild))!.GetParameters()[0].ParameterType);
    }

    /// <summary>Guards the guard: each planted defect must make the validator fail.</summary>
    [Theory]
    [InlineData("min-missing")]
    [InlineData("min-wrong")]
    [InlineData("sampler-linear")]
    [InlineData("image-target")]
    [InlineData("double-sided")]
    [InlineData("short-indices")]
    [InlineData("misaligned")]
    public void The_validator_is_not_vacuous(string defect)
    {
        byte[] good = Models.Glb(Plus);
        var (goodJson, goodBin) = GltfValidator.ReadGlb(good);
        GltfValidator.AssertValid(goodJson, goodBin);

        byte[] bad = GltfValidator.Reframe(good, json =>
        {
            switch (defect)
            {
                case "min-missing": json["accessors"]![0]!.AsObject().Remove("min"); break;
                case "min-wrong": json["accessors"]![0]!["min"]![0] = -100f; break;
                case "sampler-linear": json["samplers"]![0]!["magFilter"] = 9729; break;
                case "image-target": json["bufferViews"]![4]!["target"] = 34962; break;
                case "double-sided": json["materials"]![0]!["doubleSided"] = true; break;
                case "short-indices": json["accessors"]![3]!["componentType"] = 5123; break;
                case "misaligned":
                    json["bufferViews"]![1]!["byteOffset"] = json["bufferViews"]![1]!["byteOffset"]!.GetValue<int>() + 2;
                    break;
                default: Assert.Fail($"unknown defect {defect}"); break;
            }
        });

        var (json, bin) = GltfValidator.ReadGlb(bad);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => GltfValidator.AssertValid(json, bin));
    }
}
```

- [ ] **Step 3: Run them to verify they fail**

Run: `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~GlbWriterTests`
Expected: build FAILS — `ModelWriter` not found.

- [ ] **Step 4: Write the implementation**

`src/Nfty.Model/Gltf/GltfDocument.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Nfty.Model.Gltf;

// THE ONE glTF OBJECT MODEL. Every format is serialized from a GltfRoot; the three differ only in
// Buffers[0].Uri, which is SET HERE, in the object model, never patched into serialized text (the
// reference implementation's D2 string-replace matched nothing the day its serializer changed
// spacing, and wrote a .gltf pointing at a file that was never written). Property names are stated
// explicitly — glTF mixes camelCase with "POSITION" and "TEXCOORD_0" — and the declaration order
// here is the order in the file.

/// <summary>The glTF document.</summary>
internal sealed record GltfRoot(
    [property: JsonPropertyName("asset")] GltfAsset Asset,
    [property: JsonPropertyName("scene")] int Scene,
    [property: JsonPropertyName("scenes")] IReadOnlyList<GltfScene> Scenes,
    [property: JsonPropertyName("nodes")] IReadOnlyList<GltfNode> Nodes,
    [property: JsonPropertyName("meshes")] IReadOnlyList<GltfMesh> Meshes,
    [property: JsonPropertyName("materials")] IReadOnlyList<GltfMaterial> Materials,
    [property: JsonPropertyName("textures")] IReadOnlyList<GltfTexture> Textures,
    [property: JsonPropertyName("samplers")] IReadOnlyList<GltfSampler> Samplers,
    [property: JsonPropertyName("images")] IReadOnlyList<GltfImage> Images,
    [property: JsonPropertyName("accessors")] IReadOnlyList<GltfAccessor> Accessors,
    [property: JsonPropertyName("bufferViews")] IReadOnlyList<GltfBufferView> BufferViews,
    [property: JsonPropertyName("buffers")] IReadOnlyList<GltfBuffer> Buffers);

/// <summary><c>asset</c>: the spec version and who wrote it — no build version, so a release
/// cannot change a model's bytes.</summary>
internal sealed record GltfAsset(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("generator")] string Generator);

/// <summary>A scene: the root nodes.</summary>
internal sealed record GltfScene([property: JsonPropertyName("nodes")] IReadOnlyList<int> Nodes);

/// <summary>A node placing one mesh.</summary>
internal sealed record GltfNode(
    [property: JsonPropertyName("mesh")] int Mesh,
    [property: JsonPropertyName("name")] string Name);

/// <summary>A mesh.</summary>
internal sealed record GltfMesh(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("primitives")] IReadOnlyList<GltfPrimitive> Primitives);

/// <summary>One primitive: attributes, indices, material, mode.</summary>
internal sealed record GltfPrimitive(
    [property: JsonPropertyName("attributes")] GltfAttributes Attributes,
    [property: JsonPropertyName("indices")] int Indices,
    [property: JsonPropertyName("material")] int Material,
    [property: JsonPropertyName("mode")] int Mode);

/// <summary>The vertex attributes, as a record rather than a dictionary so the key order is fixed.</summary>
internal sealed record GltfAttributes(
    [property: JsonPropertyName("POSITION")] int Position,
    [property: JsonPropertyName("NORMAL")] int Normal,
    [property: JsonPropertyName("TEXCOORD_0")] int TexCoord0);

/// <summary>The one material. It has no <c>doubleSided</c> member at all (D3): a closed solid never
/// shows a back face.</summary>
internal sealed record GltfMaterial(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("pbrMetallicRoughness")] GltfPbr PbrMetallicRoughness,
    [property: JsonPropertyName("alphaMode")] string AlphaMode,
    [property: JsonPropertyName("alphaCutoff")] float AlphaCutoff);

/// <summary>PBR block: the art as base color, not metallic, fully rough.</summary>
internal sealed record GltfPbr(
    [property: JsonPropertyName("baseColorTexture")] GltfTextureRef BaseColorTexture,
    [property: JsonPropertyName("metallicFactor")] float MetallicFactor,
    [property: JsonPropertyName("roughnessFactor")] float RoughnessFactor);

/// <summary>A reference to a texture by index.</summary>
internal sealed record GltfTextureRef([property: JsonPropertyName("index")] int Index);

/// <summary>A texture: a sampler over an image.</summary>
internal sealed record GltfTexture(
    [property: JsonPropertyName("sampler")] int Sampler,
    [property: JsonPropertyName("source")] int Source);

/// <summary>Sampling: filters and wrapping.</summary>
internal sealed record GltfSampler(
    [property: JsonPropertyName("magFilter")] int MagFilter,
    [property: JsonPropertyName("minFilter")] int MinFilter,
    [property: JsonPropertyName("wrapS")] int WrapS,
    [property: JsonPropertyName("wrapT")] int WrapT);

/// <summary>An image held in a bufferView.</summary>
internal sealed record GltfImage(
    [property: JsonPropertyName("bufferView")] int BufferView,
    [property: JsonPropertyName("mimeType")] string MimeType);

/// <summary>A typed view of a bufferView. <c>min</c>/<c>max</c> are omitted when null.</summary>
internal sealed record GltfAccessor(
    [property: JsonPropertyName("bufferView")] int BufferView,
    [property: JsonPropertyName("componentType")] int ComponentType,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("min")] float[]? Min,
    [property: JsonPropertyName("max")] float[]? Max);

/// <summary>A byte range of the buffer. <c>target</c> is omitted when null — the image's view has none.</summary>
internal sealed record GltfBufferView(
    [property: JsonPropertyName("buffer")] int Buffer,
    [property: JsonPropertyName("byteOffset")] int ByteOffset,
    [property: JsonPropertyName("byteLength")] int ByteLength,
    [property: JsonPropertyName("target")] int? Target);

/// <summary>The one buffer. <c>uri</c> is null in a GLB (the BIN chunk is the buffer), a relative
/// file name for glTF + .bin, a data URI for embedded glTF.</summary>
internal sealed record GltfBuffer(
    [property: JsonPropertyName("byteLength")] int ByteLength,
    [property: JsonPropertyName("uri")] string? Uri);
```

`src/Nfty.Model/Gltf/GltfJson.cs`:

```csharp
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nfty.Model.Gltf;

/// <summary>The one serializer configuration every format uses.</summary>
internal static class GltfJson
{
    // Compact; nulls omitted (an absent target / min / uri is how glTF says "none"); relaxed escaping
    // so a base64 '+' in an embedded buffer stays '+' rather than becoming + — the file is not
    // HTML, and the relaxed encoder is deterministic just the same.
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>UTF-8 JSON, no BOM.</summary>
    public static byte[] Serialize(GltfRoot root) => JsonSerializer.SerializeToUtf8Bytes(root, Options);
}
```

`src/Nfty.Model/Gltf/GlbContainer.cs`:

```csharp
using System.Buffers.Binary;

namespace Nfty.Model.Gltf;

/// <summary>
/// The GLB container: a 12-byte header and two length-prefixed chunks, all little-endian. The JSON
/// chunk is padded with spaces and the BIN chunk with zeros, each to four bytes, as the spec requires.
/// </summary>
internal static class GlbContainer
{
    /// <summary>"glTF".</summary>
    public const uint Magic = 0x46546C67;

    /// <summary>The container version.</summary>
    public const uint Version = 2;

    /// <summary>"JSON".</summary>
    public const uint JsonChunk = 0x4E4F534A;

    /// <summary>"BIN\0".</summary>
    public const uint BinChunk = 0x004E4942;

    /// <summary>Frames a document and its buffer.</summary>
    public static byte[] Frame(byte[] json, byte[] bin)
    {
        int jsonLength = Pad4(json.Length);
        int binLength = Pad4(bin.Length);
        int total = checked(12 + 8 + jsonLength + 8 + binLength);
        var glb = new byte[total];
        var span = glb.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span, Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], Version);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)total);

        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], (uint)jsonLength);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], JsonChunk);
        json.CopyTo(span[20..]);
        span.Slice(20 + json.Length, jsonLength - json.Length).Fill(0x20);

        int binHeader = 20 + jsonLength;
        BinaryPrimitives.WriteUInt32LittleEndian(span[binHeader..], (uint)binLength);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(binHeader + 4)..], BinChunk);
        bin.CopyTo(span[(binHeader + 8)..]);   // the tail past bin.Length is already zero
        return glb;
    }

    /// <summary>Rounds up to a multiple of four.</summary>
    public static int Pad4(int n) => checked((n + 3) & ~3);
}
```

`src/Nfty.Model/Gltf/GltfBuilder.cs`:

```csharp
using System.Buffers.Binary;

namespace Nfty.Model.Gltf;

/// <summary>Builds the one document shape nfty writes, and the buffer it describes.</summary>
internal static class GltfBuilder
{
    private const int ArrayBuffer = 34962;
    private const int ElementArrayBuffer = 34963;
    private const int Float = 5126;
    private const int UnsignedInt = 5125;
    private const int Nearest = 9728;
    private const int ClampToEdge = 33071;
    private const int Triangles = 4;

    /// <summary>The document (buffer with no URI) and the buffer's bytes, padded to four with zeros.</summary>
    public static (GltfRoot Root, byte[] Bin) Build(Mesh mesh, byte[] png)
    {
        int vertices = mesh.VertexCount;
        int positionLength = checked(vertices * 12);
        int normalLength = positionLength;
        int uvLength = checked(vertices * 8);
        int indexLength = checked(mesh.Indices.Count * 4);

        int positionOffset = 0;
        int normalOffset = positionLength;
        int uvOffset = checked(normalOffset + normalLength);
        int indexOffset = checked(uvOffset + uvLength);
        int pngOffset = checked(indexOffset + indexLength);
        int binLength = GlbContainer.Pad4(checked(pngOffset + png.Length));

        var bin = new byte[binLength];
        WriteFloats(bin.AsSpan(positionOffset, positionLength), mesh.Positions);
        WriteFloats(bin.AsSpan(normalOffset, normalLength), mesh.Normals);
        WriteFloats(bin.AsSpan(uvOffset, uvLength), mesh.TexCoords);
        for (int i = 0; i < mesh.Indices.Count; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(bin.AsSpan(indexOffset + (i * 4)), mesh.Indices[i]);
        png.CopyTo(bin.AsSpan(pngOffset));

        var root = new GltfRoot(
            Asset: new GltfAsset("2.0", "nfty"),
            Scene: 0,
            Scenes: [new GltfScene([0])],
            Nodes: [new GltfNode(0, "nfty")],
            Meshes: [new GltfMesh("nfty", [new GltfPrimitive(new GltfAttributes(0, 1, 2), 3, 0, Triangles)])],
            Materials: [new GltfMaterial("nfty", new GltfPbr(new GltfTextureRef(0), 0f, 1f), "MASK", 0.5f)],
            Textures: [new GltfTexture(0, 0)],
            Samplers: [new GltfSampler(Nearest, Nearest, ClampToEdge, ClampToEdge)],
            Images: [new GltfImage(4, "image/png")],
            Accessors:
            [
                new GltfAccessor(0, Float, vertices, "VEC3",
                    [mesh.Min.X, mesh.Min.Y, mesh.Min.Z], [mesh.Max.X, mesh.Max.Y, mesh.Max.Z]),
                new GltfAccessor(1, Float, vertices, "VEC3", null, null),
                new GltfAccessor(2, Float, vertices, "VEC2", null, null),
                new GltfAccessor(3, UnsignedInt, mesh.Indices.Count, "SCALAR", null, null),
            ],
            BufferViews:
            [
                new GltfBufferView(0, positionOffset, positionLength, ArrayBuffer),
                new GltfBufferView(0, normalOffset, normalLength, ArrayBuffer),
                new GltfBufferView(0, uvOffset, uvLength, ArrayBuffer),
                new GltfBufferView(0, indexOffset, indexLength, ElementArrayBuffer),
                new GltfBufferView(0, pngOffset, png.Length, null),
            ],
            Buffers: [new GltfBuffer(binLength, null)]);

        return (root, bin);
    }

    private static void WriteFloats(Span<byte> target, IReadOnlyList<float> values)
    {
        for (int i = 0; i < values.Count; i++)
            BinaryPrimitives.WriteSingleLittleEndian(target[(i * 4)..], values[i]);
    }
}
```

`src/Nfty.Model/ModelWriter.cs`:

```csharp
using Nfty.Core.Diagnostics;
using Nfty.Model.Gltf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model;

/// <summary>
/// Writes a risen mesh and its texture as model files. Every format is serialized from ONE glTF
/// object model; they differ only in where the buffer lives, and that is set in the object model,
/// never by editing serialized text.
/// </summary>
public static class ModelWriter
{
    // Stated in full so the source image's own PNG metadata (palette color type, bit depth, text,
    // resolution) can never reach a model: the same pixels always encode to the same bytes.
    private static readonly PngEncoder TextureEncoder = new()
    {
        ColorType = PngColorType.RgbWithAlpha,
        BitDepth = PngBitDepth.Bit8,
        SkipMetadata = true,
    };

    /// <summary>Writes one model.</summary>
    /// <param name="mesh">The mesh, from <see cref="Riser.TryBuild"/>.</param>
    /// <param name="texture">The picture the mesh was risen from — the same size. Not modified.</param>
    /// <param name="options">Which format to write.</param>
    /// <param name="baseName">The file name without extension, and without any folder.</param>
    /// <returns>The files: one for <see cref="ModelFormat.Glb"/> and <see cref="ModelFormat.GltfEmbedded"/>,
    /// two (<c>.gltf</c> then <c>.bin</c>) for <see cref="ModelFormat.GltfWithBin"/>.</returns>
    /// <exception cref="ArgumentException">The texture is another size than the mesh was risen from,
    /// or the base name is not a plain file name.</exception>
    public static IReadOnlyList<ModelFile> Write(Mesh mesh, Image<Rgba32> texture, RiseOptions options, string baseName)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        if (baseName.AsSpan().IndexOfAny('/', '\\') >= 0)
            throw new ArgumentException(
                $"'{baseName}' is not a plain file name; the writer names files, the caller chooses the folder.",
                nameof(baseName));
        if (texture.Width != mesh.TextureWidth || texture.Height != mesh.TextureHeight)
            throw new ArgumentException(
                $"The mesh was risen from {mesh.TextureWidth}x{mesh.TextureHeight} art; this texture is "
                + $"{texture.Width}x{texture.Height}.", nameof(texture));

        using var _ = Perf.Measure("ModelWriter.Write");

        var (root, bin) = GltfBuilder.Build(mesh, EncodeTexture(texture));
        return options.Format switch
        {
            ModelFormat.Glb => new[] { new ModelFile(baseName + ".glb", GlbContainer.Frame(GltfJson.Serialize(root), bin)) },
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Format, "Unknown model format."),
        };
    }

    private static byte[] EncodeTexture(Image<Rgba32> texture)
    {
        using var stream = new MemoryStream();
        texture.Save(stream, TextureEncoder);
        return stream.ToArray();
    }
}
```

In `src/Nfty.Model/RiseOptions.cs`, change the `Format` summary from `The file shape <c>ModelWriter</c> produces.` to `The file shape <see cref="ModelWriter"/> produces.`

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~GlbWriterTests`
Expected: PASS — 42 validator cases, 7 defect cases, and the facts.

If `The_same_pixels_give_the_same_file_whatever_metadata_the_picture_carried` fails, the encoder is still reading the image's metadata: the fix is in `TextureEncoder`, never in the test.

- [ ] **Step 6: Run the whole project**

Run: `dotnet test tests/Nfty.Model.Tests`
Expected: PASS, including `No_field_in_the_assembly_can_be_written_after_construction` over the new records.

- [ ] **Step 7: Commit**

```bash
git add src/Nfty.Model/Gltf/GltfDocument.cs src/Nfty.Model/Gltf/GltfJson.cs src/Nfty.Model/Gltf/GltfBuilder.cs src/Nfty.Model/Gltf/GlbContainer.cs src/Nfty.Model/ModelWriter.cs src/Nfty.Model/RiseOptions.cs tests/Nfty.Model.Tests/GltfValidator.cs tests/Nfty.Model.Tests/Models.cs tests/Nfty.Model.Tests/GlbWriterTests.cs
git commit -m "feat: GLB from one glTF object model, with no glTF package

The document is a fixed shape of records; the GLB frames it with a 4-byte
padded JSON chunk (spaces) and BIN chunk (zeros). POSITION carries min/max,
indices are UNSIGNED_INT, the PNG sits in a target-less view, the sampler
is NEAREST/CLAMP_TO_EDGE, the material a single-sided MASK named nfty.

A test-side validator parses the container and checks every one of those;
planted defects prove each check can fail.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: glTF + `.bin` and embedded glTF from the same document; the cost, measured

**Files:**
- Modify: `src/Nfty.Model/ModelWriter.cs` (two more format arms)
- Test: `tests/Nfty.Model.Tests/GltfWriterTests.cs`
- Test: `tests/Nfty.Model.Tests/RisePerfTests.cs`

**Interfaces:**
- Consumes: everything from Task 5; `Nfty.Core.Diagnostics.Perf` (`Enable`, `Disable`, `Reset`, `Snapshot`, `Report`); `StackDepth.Combine` (Task 4).
- Produces: `ModelWriter.Write` handles all three `ModelFormat` values. `GltfWithBin` returns `[name.gltf, name.bin]` in that order, with `buffers[0].uri = Uri.EscapeDataString(name + ".bin")`; `GltfEmbedded` returns `[name.gltf]` with `buffers[0].uri = "data:application/octet-stream;base64," + base64(bin)`.

- [ ] **Step 1: Write the failing tests**

`tests/Nfty.Model.Tests/GltfWriterTests.cs`:

```csharp
using System.Text.Json.Nodes;

namespace Nfty.Model.Tests;

public class GltfWriterTests
{
    private static readonly string[] Plus = [".#.", "###", ".#."];

    private static IReadOnlyList<ModelFile> Write(ModelFormat format, string baseName = "sprite") =>
        Models.Write(Plus, (x, y) => (x, y), new RiseOptions { Format = format }, baseName);

    public static TheoryData<string, string, ModelFormat> EveryShapePatternAndTextFormat()
    {
        var data = new TheoryData<string, string, ModelFormat>();
        foreach (string shape in Shapes.All.Keys)
            foreach (string pattern in Shapes.Patterns.Keys)
                foreach (var format in new[] { ModelFormat.GltfWithBin, ModelFormat.GltfEmbedded })
                    data.Add(shape, pattern, format);
        return data;
    }

    [Fact]
    public void Gltf_with_bin_writes_the_text_document_and_its_buffer_beside_it()
    {
        var files = Write(ModelFormat.GltfWithBin);
        Assert.Equal(new[] { "sprite.gltf", "sprite.bin" }, files.Select(f => f.FileName));

        var (json, bin) = GltfValidator.ReadGltf(files);
        Assert.Equal("sprite.bin", json["buffers"]![0]!["uri"]!.GetValue<string>());
        GltfValidator.AssertValid(json, bin);
    }

    [Fact]
    public void Embedded_gltf_is_one_text_file_carrying_its_buffer_inline()
    {
        var file = Assert.Single(Write(ModelFormat.GltfEmbedded));
        Assert.Equal("sprite.gltf", file.FileName);

        var (json, bin) = GltfValidator.ReadGltf([file]);
        Assert.StartsWith(GltfValidator.DataUriPrefix, json["buffers"]![0]!["uri"]!.GetValue<string>(), StringComparison.Ordinal);
        GltfValidator.AssertValid(json, bin);
    }

    [Theory]
    [MemberData(nameof(EveryShapePatternAndTextFormat))]
    public void Every_text_form_passes_the_structure_validator(string shape, string pattern, ModelFormat format)
    {
        var files = Models.Write(Shapes.All[shape], Shapes.Patterns[pattern], new RiseOptions { Format = format });
        var (json, bin) = GltfValidator.ReadGltf(files);
        GltfValidator.AssertValid(json, bin);
    }

    [Fact]
    public void The_embedded_buffer_decodes_to_exactly_the_bin_file_and_the_glb_chunk()
    {
        byte[] sibling = Assert.Single(Write(ModelFormat.GltfWithBin), f => f.FileName == "sprite.bin").Bytes;
        var (_, embedded) = GltfValidator.ReadGltf(Write(ModelFormat.GltfEmbedded));
        var (_, chunk) = GltfValidator.ReadGlb(Assert.Single(Write(ModelFormat.Glb)).Bytes);

        Assert.Equal(sibling, embedded);
        Assert.Equal(sibling, chunk);
    }

    [Fact]
    public void All_three_formats_are_one_document_differing_only_in_the_buffer_uri()
    {
        // The rule D2's lesson became: one object model, the location of the buffer set IN it.
        JsonNode Document(IReadOnlyList<ModelFile> files, bool glb)
        {
            var json = glb ? GltfValidator.ReadGlb(Assert.Single(files).Bytes).Json : GltfValidator.ReadGltf(files).Json;
            json["buffers"]![0]!.AsObject().Remove("uri");
            return json;
        }

        var fromGlb = Document(Write(ModelFormat.Glb), glb: true);
        Assert.True(JsonNode.DeepEquals(fromGlb, Document(Write(ModelFormat.GltfWithBin), glb: false)));
        Assert.True(JsonNode.DeepEquals(fromGlb, Document(Write(ModelFormat.GltfEmbedded), glb: false)));
    }

    [Fact]
    public void A_base_name_with_a_space_is_escaped_in_the_uri_and_not_in_the_file_name()
    {
        var files = Write(ModelFormat.GltfWithBin, "my sprite");
        Assert.Equal(new[] { "my sprite.gltf", "my sprite.bin" }, files.Select(f => f.FileName));

        var (json, bin) = GltfValidator.ReadGltf(files);
        Assert.Equal("my%20sprite.bin", json["buffers"]![0]!["uri"]!.GetValue<string>());
        GltfValidator.AssertValid(json, bin);
    }

    [Theory]
    [InlineData(ModelFormat.Glb)]
    [InlineData(ModelFormat.GltfWithBin)]
    [InlineData(ModelFormat.GltfEmbedded)]
    public void Every_format_is_byte_identical_when_written_twice(ModelFormat format)
    {
        var a = Write(format);
        var b = Write(format);
        Assert.Equal(a.Select(f => f.FileName), b.Select(f => f.FileName));
        for (int i = 0; i < a.Count; i++)
            Assert.Equal(a[i].Bytes, b[i].Bytes);
    }
}
```

`tests/Nfty.Model.Tests/RisePerfTests.cs`:

```csharp
using System.Globalization;
using Nfty.Core.Diagnostics;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

/// <summary>
/// MEASURED, NEVER ASSERTED AS WALL-CLOCK. The spec keeps two questions open until measured: whether
/// a cross-quad vertex dedup is worth adding, and what <c>GreedyRects</c> costs on a large canvas.
/// This runs the real path on 64px and 512px art and prints <c>Perf</c>'s table; the numbers belong
/// in the task report, and a dedup pass is added only if they say the size matters. Run with
/// <c>dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~RisePerfTests -l "console;verbosity=detailed"</c>.
/// </summary>
public class RisePerfTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(64)]
    [InlineData(512)]
    public void Rising_writing_and_combining_are_measured_on_real_sized_art(int size)
    {
        using var art = Disc(size);
        var depth = Dome(art);

        Perf.Enable();
        try
        {
            Perf.Reset();
            var options = new RiseOptions();
            var mesh = Riser.TryBuild(art, depth, options, $"{size}px disc")!;
            var files = ModelWriter.Write(mesh, art, options, "disc");
            var (combined, _) = StackDepth.Combine([(art, depth), (art, null)]);
            combined.Dispose();

            var names = Perf.Snapshot().Select(e => e.Name).ToList();
            Assert.Contains("Riser.TryBuild", names);
            Assert.Contains("ModelWriter.Write", names);
            Assert.Contains("StackDepth.Combine", names);

            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{size}px: {mesh.TriangleCount:N0} triangles, {mesh.VertexCount:N0} vertices, {files[0].Bytes.Length:N0} GLB bytes"));
            output.WriteLine(Perf.Report());
        }
        finally
        {
            Perf.Disable();
        }
    }

    /// <summary>An opaque disc on a clear ground, every pixel its own color.</summary>
    private static Image<Rgba32> Disc(int size)
    {
        var img = new Image<Rgba32>(size, size);
        float c = (size - 1) / 2f, r = (size / 2f) - 1;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = MathF.Sqrt(((x - c) * (x - c)) + ((y - c) * (y - c)));
                img[x, y] = d <= r
                    ? new Rgba32((byte)(x * 255 / size), (byte)(y * 255 / size), 128, 255)
                    : new Rgba32(0, 0, 0, 0);
            }
        return img;
    }

    /// <summary>Eight rings rising toward the center, one voxel of back throughout.</summary>
    private static DepthMap Dome(Image<Rgba32> art)
    {
        float c = (art.Width - 1) / 2f, r = art.Width / 2f;
        return Art.Depth(art, (x, y) =>
        {
            float d = MathF.Sqrt(((x - c) * (x - c)) + ((y - c) * (y - c)));
            return (Math.Clamp(8 - (int)(d * 8 / r), 0, 8), 1);
        });
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/Nfty.Model.Tests --filter "FullyQualifiedName~GltfWriterTests|FullyQualifiedName~RisePerfTests"`
Expected: `GltfWriterTests` FAIL with `ArgumentOutOfRangeException: Unknown model format.` for every `GltfWithBin` / `GltfEmbedded` case; `RisePerfTests` PASS already (it uses only the GLB path) — say so in the report.

- [ ] **Step 3: Write the implementation**

In `src/Nfty.Model/ModelWriter.cs`, add the constant under `TextureEncoder`:

```csharp
    private const string DataUriPrefix = "data:application/octet-stream;base64,";
```

replace the `return options.Format switch { ... };` block with:

```csharp
        return options.Format switch
        {
            ModelFormat.Glb => new[]
            {
                new ModelFile(baseName + ".glb", GlbContainer.Frame(GltfJson.Serialize(root), bin)),
            },
            // The .bin is named by a RELATIVE URI, which glTF requires to be percent-encoded; the
            // file on disk keeps the name the caller gave.
            ModelFormat.GltfWithBin => new[]
            {
                new ModelFile(baseName + ".gltf", GltfJson.Serialize(WithBufferUri(root, Uri.EscapeDataString(baseName + ".bin")))),
                new ModelFile(baseName + ".bin", bin),
            },
            ModelFormat.GltfEmbedded => new[]
            {
                new ModelFile(baseName + ".gltf", GltfJson.Serialize(WithBufferUri(root, DataUriPrefix + Convert.ToBase64String(bin)))),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Format, "Unknown model format."),
        };
```

and add, below `EncodeTexture`:

```csharp
    /// <summary>The same document with the buffer's location set — in the object model, never by
    /// editing serialized text (mixel's D2).</summary>
    private static GltfRoot WithBufferUri(GltfRoot root, string uri) =>
        root with { Buffers = [root.Buffers[0] with { Uri = uri }] };
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Nfty.Model.Tests --filter "FullyQualifiedName~GltfWriterTests|FullyQualifiedName~RisePerfTests"`
Expected: PASS — 84 text-form validator cases plus the facts and theories.

- [ ] **Step 5: Measure, and record the numbers**

Run: `dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~RisePerfTests -l "console;verbosity=detailed"`

Paste both tables (triangles, vertices, GLB bytes, and each scope's ms/KB) into the task report. Then answer, in the report, the two questions the spec leaves open: (a) at 512px, what share of the GLB is vertex data a `(position, normal, uv)` dedup could shrink — every quad owns 4 vertices, so the ceiling is known; (b) whether `Riser.TryBuild` at 512px is dominated by `GreedyRects` or by wall emission. **Do not add a dedup pass in this plan**; if the numbers argue for one, add a `BACKLOG.md` entry with them instead.

- [ ] **Step 6: Commit**

```bash
git add src/Nfty.Model/ModelWriter.cs tests/Nfty.Model.Tests/GltfWriterTests.cs tests/Nfty.Model.Tests/RisePerfTests.cs
git commit -m "feat: glTF with a .bin, and embedded glTF, from the same document

The three formats differ only in buffers[0].uri, set in the object model:
none for GLB, the escaped sibling name, or a base64 data URI. The embedded
buffer decodes to exactly the .bin and the GLB's BIN chunk.

RisePerfTests prints Perf's table for 64px and 512px art, so vertex dedup
and GreedyRects cost are decided by measurement.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: `nfty model`

**Files:**
- Modify: `src/Nfty.Cli/Nfty.Cli.csproj` (`ProjectReference` to `Nfty.Model`)
- Create: `src/Nfty.Cli/CommandFactory.Model.cs`
- Modify: `src/Nfty.Cli/CommandFactory.cs:47` (register the command after `Preview()`)
- Modify: `tests/Nfty.Model.Tests/GltfValidator.cs` (one sentence in the summary, Step 7)
- Test: `tests/Nfty.Cli.Tests/ModelCommandTests.cs`

**Interfaces:**
- Consumes: `Riser.TryBuild`, `ModelWriter.Write`, `DepthFill.All`, `RiseOptions`, `ModelFormat`, `Pivot`, `PartialAlphaException`; `Archives.TryKindOf`, `ArchiveKind.Ingredient`, `IngredientArchive.Read(string)` → `LoadedIngredient` (`VariantImages`, `Depths`, `Manifest.Variants`, `Manifest.Id`).
- Produces: the command
  ```
  nfty model <input> --out <path> [--variant ID] [--front N --back M]
             [--format glb|gltf|gltf-embedded] [--pivot bottom-center|center|min-corner] [--voxel-size F]
  ```

- [ ] **Step 1: Reference `Nfty.Model` from the CLI**

In `src/Nfty.Cli/Nfty.Cli.csproj`, replace:

```xml
  <ItemGroup>
    <ProjectReference Include="..\Nfty.Core\Nfty.Core.csproj" />
  </ItemGroup>
```

with:

```xml
  <ItemGroup>
    <ProjectReference Include="..\Nfty.Core\Nfty.Core.csproj" />
    <!-- `nfty model`. The CLI may take it; Nfty.Core may not. -->
    <ProjectReference Include="..\Nfty.Model\Nfty.Model.csproj" />
  </ItemGroup>
```

- [ ] **Step 2: Write the failing tests**

`tests/Nfty.Cli.Tests/ModelCommandTests.cs`:

```csharp
using System.Buffers.Binary;
using System.CommandLine;
using System.Text.Json.Nodes;
using Nfty.Cli;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli.Tests;

/// <summary>
/// <c>nfty model</c>: a picture or one Ingredient variant, risen and written. Options belonging to
/// the other form are REJECTED at parse time, never ignored — the rule <c>preview</c> keeps.
/// </summary>
public class ModelCommandTests
{
    private static readonly InvocationConfiguration NonThrowing = new() { EnableDefaultExceptionHandler = false };

    private static int Run(params string[] args) => CommandFactory.Build().Parse(args).Invoke(NonThrowing);

    private static ParseResult Parse(string line) => CommandFactory.Build().Parse(line);

    private static string WritePicture(string dir, string name, int width, int height, Rgba32 color)
    {
        string path = Path.Combine(dir, name);
        using var img = new Image<Rgba32>(width, height, color);
        img.SaveAsPng(path);
        return path;
    }

    private static (float[] Min, float[] Max) PositionBounds(byte[] glb)
    {
        int jsonLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        var json = JsonNode.Parse(glb.AsSpan(20, jsonLength))!;
        int position = json["meshes"]![0]!["primitives"]![0]!["attributes"]!["POSITION"]!.GetValue<int>();
        var accessor = json["accessors"]![position]!;
        return (accessor["min"]!.AsArray().Select(n => n!.GetValue<float>()).ToArray(),
                accessor["max"]!.AsArray().Select(n => n!.GetValue<float>()).ToArray());
    }

    // --- parsing ---

    [Fact]
    public void A_picture_needs_nothing_but_an_out_path()
    {
        Assert.Empty(Parse("model sprite.png --out sprite.glb").Errors);
    }

    [Fact]
    public void An_ingredient_needs_a_variant_and_a_picture_refuses_one()
    {
        Assert.Empty(Parse("model hat.igt --variant red --out hat.glb").Errors);
        Assert.NotEmpty(Parse("model hat.igt --out hat.glb").Errors);
        Assert.NotEmpty(Parse("model sprite.png --variant red --out sprite.glb").Errors);
    }

    [Fact]
    public void Anything_else_is_refused_naming_both_forms()
    {
        var error = Assert.Single(Parse("model book.cbk --out a.glb").Errors);
        Assert.Contains(".png", error.Message, StringComparison.Ordinal);
        Assert.Contains(".igt", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Front_and_back_are_given_together_or_not_at_all()
    {
        Assert.Empty(Parse("model s.png --out s.glb --front 2 --back 0").Errors);
        Assert.NotEmpty(Parse("model s.png --out s.glb --front 2").Errors);
        Assert.NotEmpty(Parse("model s.png --out s.glb --back 2").Errors);
    }

    [Theory]
    [InlineData("--front 256 --back 0")]
    [InlineData("--front 0 --back 300")]
    public void A_depth_past_one_byte_is_refused(string fill)
    {
        Assert.NotEmpty(Parse($"model s.png --out s.glb {fill}").Errors);
    }

    [Fact]
    public void A_voxel_size_that_is_not_positive_is_refused()
    {
        Assert.NotEmpty(Parse("model s.png --out s.glb --voxel-size 0").Errors);
    }

    [Theory]
    [InlineData("glb", "a.gltf")]
    [InlineData("gltf", "a.glb")]
    [InlineData("gltf-embedded", "a.glb")]
    [InlineData("glb", "a")]
    public void The_out_extension_must_match_the_format(string format, string outPath)
    {
        Assert.NotEmpty(Parse($"model s.png --out {outPath} --format {format}").Errors);
    }

    [Fact]
    public void Unknown_formats_and_pivots_are_refused()
    {
        Assert.NotEmpty(Parse("model s.png --out s.glb --format obj").Errors);
        Assert.NotEmpty(Parse("model s.png --out s.glb --pivot top").Errors);
    }

    // --- running ---

    [Fact]
    public void A_picture_rises_into_a_glb_one_voxel_thick()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string png = WritePicture(dir, "sprite.png", 2, 1, new Rgba32(200, 40, 40, 255));
            string glb = Path.Combine(dir, "sprite.glb");

            Assert.Equal(0, Run("model", png, "--out", glb));

            byte[] bytes = File.ReadAllBytes(glb);
            Assert.Equal(0x46546C67u, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
            var (min, max) = PositionBounds(bytes);
            Assert.Equal(new[] { -1f, 0f, -0.5f }, min);   // bottom-center, two wide, paper
            Assert.Equal(new[] { 1f, 1f, 0.5f }, max);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Fill_all_raises_every_opaque_pixel_and_voxel_size_scales_it()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string png = WritePicture(dir, "sprite.png", 1, 1, new Rgba32(200, 40, 40, 255));
            string glb = Path.Combine(dir, "sprite.glb");

            Assert.Equal(0, Run("model", png, "--out", glb, "--front", "3", "--back", "1",
                "--pivot", "min-corner", "--voxel-size", "0.5"));

            var (min, max) = PositionBounds(File.ReadAllBytes(glb));
            Assert.Equal(new[] { 0f, 0f, 0f }, min);
            Assert.Equal(new[] { 0.5f, 0.5f, 2.5f }, max);   // 1 + 3 + 1 voxels deep, at half a unit
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void An_ingredient_variant_rises_with_its_stored_depth_and_fill_all_replaces_it()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string igt = Path.Combine(dir, "hat.igt");
            using (var red = new Image<Rgba32>(1, 1, new Rgba32(200, 40, 40, 255)))
                IngredientArchive.Write(igt,
                    new IngredientManifest("hat", "Hat", LayerKind.Custom, null, [new Variant("red", "Red", 1)]),
                    new Dictionary<string, Image<Rgba32>> { ["red"] = red },
                    new Dictionary<string, DepthMap> { ["red"] = DepthMap.FromBytes(1, 1, [2, 1]) });

            string stored = Path.Combine(dir, "stored.glb");
            Assert.Equal(0, Run("model", igt, "--variant", "red", "--out", stored));
            var (min, max) = PositionBounds(File.ReadAllBytes(stored));
            Assert.Equal(4f, max[2] - min[2]);   // 1 + front 2 + back 1

            string flat = Path.Combine(dir, "flat.glb");
            Assert.Equal(0, Run("model", igt, "--variant", "red", "--out", flat, "--front", "0", "--back", "0"));
            (min, max) = PositionBounds(File.ReadAllBytes(flat));
            Assert.Equal(1f, max[2] - min[2]);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void The_gltf_format_writes_its_bin_beside_it()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string png = WritePicture(dir, "sprite.png", 2, 2, new Rgba32(200, 40, 40, 255));
            string gltf = Path.Combine(dir, "sprite.gltf");

            Assert.Equal(0, Run("model", png, "--out", gltf, "--format", "gltf"));

            Assert.True(File.Exists(Path.Combine(dir, "sprite.bin")));
            var json = JsonNode.Parse(File.ReadAllBytes(gltf))!;
            Assert.Equal("sprite.bin", json["buffers"]![0]!["uri"]!.GetValue<string>());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_partly_transparent_picture_is_refused_by_name_and_nothing_is_written()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string png = WritePicture(dir, "ghost.png", 1, 1, new Rgba32(200, 40, 40, 128));
            string glb = Path.Combine(dir, "ghost.glb");

            var ex = Assert.Throws<PartialAlphaException>(() => Run("model", png, "--out", glb));
            Assert.Contains(png, ex.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(glb));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_fully_transparent_picture_has_nothing_to_rise()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string png = WritePicture(dir, "empty.png", 2, 2, new Rgba32(0, 0, 0, 0));
            var ex = Assert.Throws<InvalidOperationException>(() =>
                Run("model", png, "--out", Path.Combine(dir, "empty.glb")));
            Assert.Contains("nothing to rise", ex.Message, StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void An_unknown_variant_is_refused_naming_the_ones_there_are()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string igt = Path.Combine(dir, "hat.igt");
            using (var red = new Image<Rgba32>(1, 1, new Rgba32(200, 40, 40, 255)))
                IngredientArchive.Write(igt,
                    new IngredientManifest("hat", "Hat", LayerKind.Custom, null, [new Variant("red", "Red", 1)]),
                    new Dictionary<string, Image<Rgba32>> { ["red"] = red },
                    DepthMaps.None);

            var ex = Assert.Throws<ArgumentException>(() =>
                Run("model", igt, "--variant", "blue", "--out", Path.Combine(dir, "hat.glb")));
            Assert.Contains("'blue'", ex.Message, StringComparison.Ordinal);
            Assert.Contains("red", ex.Message, StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
```

- [ ] **Step 3: Run them to verify they fail**

Run: `dotnet test tests/Nfty.Cli.Tests --filter FullyQualifiedName~ModelCommandTests`
Expected: the parse tests that expect **no** errors FAIL (`'model' was not matched`), and every run test FAILS (unknown command → non-zero exit or parse error); the tests that expect errors pass vacuously — note that in the report; they turn meaningful once the command exists.

- [ ] **Step 4: Write the command**

`src/Nfty.Cli/CommandFactory.Model.cs`:

```csharp
using System.CommandLine;
using System.Globalization;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli;

public static partial class CommandFactory
{
    /// <summary>Which of <c>model</c>'s two forms an input selects.</summary>
    private enum ModelInput
    {
        /// <summary>A loose <c>.png</c>.</summary>
        Picture,

        /// <summary>An <c>.igt</c>, with <c>--variant</c>.</summary>
        Ingredient,
    }

    private static readonly string[] ModelFormatNames = ["glb", "gltf", "gltf-embedded"];
    private static readonly string[] PivotNames = ["bottom-center", "center", "min-corner"];

    private static Command ModelCommand()
    {
        var input = new Argument<string>("input")
        {
            Description = "A .png picture, or an .igt Ingredient together with --variant. Which one "
                + "you pass chooses the form.",
        };
        var outp = new Option<string>("--out")
        {
            Description = "The model file to write. Its extension must match --format: .glb for glb, "
                + ".gltf for gltf and gltf-embedded. The gltf format also writes a .bin beside it, "
                + "with the same name.",
            Required = true,
        };
        var variant = new Option<string>("--variant")
        {
            Description = "Variant id to rise — an id, not its display name. Required for an .igt, "
                + "refused for a .png. The variant's stored depth map is used; a variant with none "
                + "is paper. The variant's own pixels are risen as they are stored: a Dynamic or "
                + "Static layer's variant is its grayscale value-map.",
        };
        var front = new Option<int>("--front")
        {
            Description = "Fill-all: every fully opaque pixel's front depth, 0..255 voxels toward the "
                + "viewer. Given with --back, never alone. Replaces any stored depth.",
        };
        var back = new Option<int>("--back")
        {
            Description = "Fill-all: every fully opaque pixel's back depth, 0..255 voxels away from "
                + "the viewer. Given with --front, never alone.",
        };
        var format = new Option<string>("--format")
        {
            Description = "glb (one binary file), gltf (text JSON plus a .bin beside it) or "
                + "gltf-embedded (text JSON with the buffer inline).",
            DefaultValueFactory = _ => "glb",
        };
        format.AcceptOnlyFromAmong(ModelFormatNames);
        var pivot = new Option<string>("--pivot")
        {
            Description = "Which point of the model's bounding box sits at the origin: bottom-center "
                + "(standing on the floor), center, or min-corner.",
            DefaultValueFactory = _ => "bottom-center",
        };
        pivot.AcceptOnlyFromAmong(PivotNames);
        var voxelSize = new Option<float>("--voxel-size")
        {
            Description = "World units per pixel, and per voxel of depth. Must be positive.",
            DefaultValueFactory = _ => 1f,
        };

        var cmd = new Command("model",
            "Rise a picture into a 3D model: every fully opaque pixel becomes a column of voxels — one "
                + "for the paper plus its front and back depth — colored with the picture's own pixels. "
                + "A partly transparent pixel is refused by name, never rounded.")
        { input, outp, variant, front, back, format, pivot, voxelSize };

        // Which options are legal depends on the FORM the input selects, and an option that silently
        // does nothing is worse than one that says no — the rule `preview` keeps. Enforced at parse
        // time, where the error names the form.
        cmd.Validators.Add(result =>
        {
            string? file = result.GetValue(input);
            if (string.IsNullOrEmpty(file)) return;   // the missing-argument error already speaks

            bool Given(Option option) => result.GetResult(option) is { Implicit: false };

            switch (ModelInputOf(file))
            {
                case null:
                    result.AddError($"model reads a picture (.png) or an Ingredient "
                        + $"({Archives.IngredientExtension}) with --variant; '{file}' is neither.");
                    return;
                case ModelInput.Picture when Given(variant):
                    result.AddError("--variant names one variant of an .igt; a .png is one picture already.");
                    break;
                case ModelInput.Ingredient when !Given(variant):
                    result.AddError($"--variant is required to model '{file}': an Ingredient holds "
                        + "several variants and a model is made of one.");
                    break;
            }

            if (Given(front) != Given(back))
                result.AddError("--front and --back fill every opaque pixel together; pass both "
                    + "(0 leaves a side flat).");
            foreach (var (option, name) in new (Option<int> Option, string Name)[] { (front, "--front"), (back, "--back") })
            {
                int value = result.GetValue(option);
                if (Given(option) && value is < 0 or > 255)
                    result.AddError($"{name} must be 0..255 (one byte per side); got {value}.");
            }

            float size = result.GetValue(voxelSize);
            if (!float.IsFinite(size) || size <= 0f)
                result.AddError("--voxel-size must be a positive number of world units per pixel; got "
                    + size.ToString(CultureInfo.InvariantCulture) + ".");

            string? outPath = result.GetValue(outp);
            string? fmt = result.GetValue(format);
            if (outPath is not null && fmt is not null && ModelFormatNames.Contains(fmt, StringComparer.Ordinal))
            {
                string expected = fmt == "glb" ? ".glb" : ".gltf";
                if (!string.Equals(Path.GetExtension(outPath), expected, StringComparison.OrdinalIgnoreCase))
                    result.AddError($"--out '{outPath}' does not match --format {fmt}: that format "
                        + $"writes a {expected} file.");
            }
        });

        cmd.SetAction(parse =>
        {
            string file = parse.GetValue(input)!;
            string outPath = parse.GetValue(outp)!;
            var options = new RiseOptions
            {
                VoxelSize = parse.GetValue(voxelSize),
                Pivot = PivotOf(parse.GetValue(pivot)!),
                Format = ModelFormatOf(parse.GetValue(format)!),
            };
            (byte Front, byte Back)? fill = parse.GetResult(front) is { Implicit: false }
                ? ((byte)parse.GetValue(front), (byte)parse.GetValue(back))
                : null;

            return ModelInputOf(file) switch
            {
                ModelInput.Picture => ModelFromPicture(file, outPath, options, fill),
                ModelInput.Ingredient => ModelFromIngredient(file, parse.GetValue(variant)!, outPath, options, fill),
                // A backstop, not a reachable path: the validator has already refused anything that
                // is neither form, with a better-worded message.
                _ => throw new NotSupportedException(
                    $"model reads a picture (.png) or an Ingredient ({Archives.IngredientExtension}), not '{file}'."),
            };
        });
        return cmd;
    }

    /// <summary>
    /// Which of <c>model</c>'s forms a path selects, or null for neither. A picture is recognized by
    /// its extension FIRST: it is not an archive kind, so <see cref="Archives.KindOf"/> would refuse
    /// it. Every other path goes through <see cref="Archives.TryKindOf"/> — the one owner of the
    /// extension-to-kind table — and only an Ingredient is accepted.
    /// </summary>
    private static ModelInput? ModelInputOf(string path) =>
        string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase) ? ModelInput.Picture
        : Archives.TryKindOf(path, out var kind) && kind == ArchiveKind.Ingredient ? ModelInput.Ingredient
        : null;

    private static int ModelFromPicture(string file, string outPath, RiseOptions options, (byte Front, byte Back)? fill)
    {
        using var art = Image.Load<Rgba32>(file);
        DepthMap? depth = fill is { } f ? DepthFill.All(art, f.Front, f.Back) : null;
        return WriteModel(art, depth, options, outPath, $"'{file}'");
    }

    private static int ModelFromIngredient(string file, string variantId, string outPath, RiseOptions options,
        (byte Front, byte Back)? fill)
    {
        using var ing = IngredientArchive.Read(file);
        if (!ing.VariantImages.TryGetValue(variantId, out var art))
            throw new ArgumentException(
                $"Ingredient '{ing.Manifest.Id}' has no variant '{variantId}'; its variants are "
                + string.Join(", ", ing.Manifest.Variants.Select(v => v.Id).Order(StringComparer.Ordinal)) + ".");

        DepthMap? depth = fill is { } f ? DepthFill.All(art, f.Front, f.Back) : ing.Depths.GetValueOrDefault(variantId);
        return WriteModel(art, depth, options, outPath, $"variant '{variantId}' of '{file}'");
    }

    private static int WriteModel(Image<Rgba32> art, DepthMap? depth, RiseOptions options, string outPath, string source)
    {
        var mesh = Riser.TryBuild(art, depth, options, source)
            ?? throw new InvalidOperationException($"{source} has no fully opaque pixel, so there is nothing to rise.");

        string dir = Path.GetDirectoryName(Path.GetFullPath(outPath))!;
        var files = ModelWriter.Write(mesh, art, options, Path.GetFileNameWithoutExtension(outPath));
        Directory.CreateDirectory(dir);
        foreach (var f in files)
            File.WriteAllBytes(Path.Combine(dir, f.FileName), f.Bytes);

        Console.WriteLine($"Wrote {string.Join(" and ", files.Select(f => Path.Combine(dir, f.FileName)))} ("
            + mesh.TriangleCount.ToString("N0", CultureInfo.InvariantCulture) + " triangles)");
        return 0;
    }

    private static Pivot PivotOf(string name) => name switch
    {
        "bottom-center" => Pivot.BottomCenter,
        "center" => Pivot.Center,
        "min-corner" => Pivot.MinCorner,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown pivot."),
    };

    private static ModelFormat ModelFormatOf(string name) => name switch
    {
        "glb" => ModelFormat.Glb,
        "gltf" => ModelFormat.GltfWithBin,
        "gltf-embedded" => ModelFormat.GltfEmbedded,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown model format."),
    };
}
```

In `src/Nfty.Cli/CommandFactory.cs`, `Build()`, replace:

```csharp
        root.Subcommands.Add(Preview());
```

with:

```csharp
        root.Subcommands.Add(Preview());
        root.Subcommands.Add(ModelCommand());
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Nfty.Cli.Tests --filter FullyQualifiedName~ModelCommandTests`
Expected: PASS — 19.

If `parse.GetResult(front)` does not compile against System.CommandLine 2.0.10, the equivalent is `parse.CommandResult.GetResult(front)`; the validator already uses the `CommandResult` form.

- [ ] **Step 6: Try it by hand**

Write the demo book, cook one asset from it, and rise that asset's PNG in all three formats:

```bash
dotnet run --project src/Nfty.Cli -- demo "$TMPDIR/rise"
dotnet run --project src/Nfty.Cli -- generate "$TMPDIR/rise/ChestDemo.cbk" --count 1 --seed rise --out "$TMPDIR/rise/set"
dotnet run --project src/Nfty.Cli -- model "$TMPDIR/rise/set/images/0001.png" --out "$TMPDIR/rise/chest.glb" --front 2 --back 1
dotnet run --project src/Nfty.Cli -- model "$TMPDIR/rise/set/images/0001.png" --out "$TMPDIR/rise/chest.gltf" --format gltf --front 2 --back 1
dotnet run --project src/Nfty.Cli -- model "$TMPDIR/rise/set/images/0001.png" --out "$TMPDIR/rise/chest-inline.gltf" --format gltf-embedded --front 2 --back 1
```

(`$TMPDIR` = the scratchpad; on Windows PowerShell use `$env:TEMP`.) If the demo's assets carry partial alpha, the command refuses by name — that is correct behavior; record it and draw a 16×16 opaque test PNG instead.

- [ ] **Step 7: Open all three in a real viewer, and record it**

Open the three files in a real glTF viewer (Windows 3D Viewer, Blender's glTF importer, or https://gltf-viewer.donmccurdy.com). A structural test proves the bytes are well-formed; only a viewer proves they are a model. Check: the sprite reads correctly (not mirrored, not upside down), the sides take the edge pixels' colors, pixels are crisp at zoom, and nothing is see-through from behind.

Then add one sentence to the `<summary>` of `tests/Nfty.Model.Tests/GltfValidator.cs`, after "…proves each check can fail.":

```csharp
/// All three formats were opened by hand in <viewer name> on <YYYY-MM-DD> (Rise step 5, Task 7).
```

with the viewer and today's date filled in from what was actually done. If no viewer is available to the implementer, leave the file unchanged and report "viewer check pending" to the author — do not write a date for a check that did not happen.

- [ ] **Step 8: Run the full suite**

Run: `dotnet test nfty.sln`
Expected: PASS — 2418 baseline plus every test added in Tasks 1–7; `0 Warning(s)`.

- [ ] **Step 9: Commit**

```bash
git add src/Nfty.Cli/Nfty.Cli.csproj src/Nfty.Cli/CommandFactory.Model.cs src/Nfty.Cli/CommandFactory.cs tests/Nfty.Cli.Tests/ModelCommandTests.cs tests/Nfty.Model.Tests/GltfValidator.cs
git commit -m "feat: nfty model

A .png, or an .igt variant with its stored depth; --front/--back together
fill every opaque pixel. Options of the other form, a lone --front, and an
--out that disagrees with --format are refused at parse time. Partial alpha
is refused naming the file; an empty picture has nothing to rise.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

(If Step 7 left `GltfValidator.cs` unchanged, drop it from the `git add` line.)

---

### Task 8: Documentation

**Files:**
- Modify: `CLAUDE.md` (Commands, Project layout, `Nfty.Cli` bullet)
- Modify: `AGENTS.md`, `.clinerules/project.md`, `.github/copilot-instructions.md`, `.opencode/AGENTS.md` (identical mirrors)
- Modify: `README.md` (architecture tree, command table)
- Modify: `docs/manual/reference/cli.md` (new section)

**Interfaces:**
- Consumes: the finished surface of Tasks 1–7.
- Produces: no code.

- [ ] **Step 1: CLAUDE.md — Commands**

Replace:

```
dotnet test tests/Nfty.Core.Tests           # one test project (also: tests/Nfty.Cli.Tests, tests/Nfty.Ticket.Tests)
```

with:

```
dotnet test tests/Nfty.Core.Tests           # one test project (also: tests/Nfty.Cli.Tests, tests/Nfty.Ticket.Tests, tests/Nfty.Model.Tests)
```

and replace `tests **xunit.v3** across all four projects` with `tests **xunit.v3** across all five test projects`.

- [ ] **Step 2: CLAUDE.md — the `Nfty.Model` bullet**

In `### Project layout`, insert this bullet directly after the `- **`Nfty.Ticket`** — …` bullet and before `- **`Nfty.Cli`** — …`:

```markdown
- **`Nfty.Model`** — Rise: pixel art → 3D. `net10.0`, `ProjectReference` `Nfty.Core` ONLY, **zero** packages, no glTF library; `Nfty.Core`/`Nfty.Ticket` never reference it (arrow one way, `AssemblyRulesTests` checks). `DepthMap` NOT redeclared — step 4's `Nfty.Core.Model.DepthMap` (in `Nfty.Ticket`); namespace `Nfty.Model` doesn't clash. `Riser.TryBuild(art, depth, options, source)` = one solid from one FINISHED image: solid = `Opacity.IsSolid` (**one** test, mixel's D1), partial alpha → `PartialAlphaException` naming item, checked BEFORE emptiness; nothing solid → `null` (batch skips + names, never fails — D5); takes `Image<Rgba32>`, never bytes (D6). Column z `[-(0.5+back), 0.5+front]` voxel units, +Z = viewer, image row 0 = TOP (highest Y). Faces = `GreedyRects` over keyed grid (equal front; separately equal back), one scan for every depth level. Walls = column interval MINUS neighbor's → **two** segments when neighbor sits inside both ends; one quad per pixel edge per segment, never merged. **Mesh = closed + consistently oriented, NOT strict 2-manifold**: greedy faces meet per-pixel walls at T-junctions (2×1 strip already fails "every edge shared by two triangles"), corner-only contact pinches four faces on one edge. `MeshChecks` refines axis edges to unit lattice segments, asserts each balanced by reverse + volume exactly `Σ(1+front+back)` + normals match winding; strict form only where no pinch. Don't "fix" test back to naive check. Pivot = named point of bounding box → origin (`BottomCenter` = X/Z center, min Y — centers SOLID, not paper), then × `VoxelSize`. Vertex shared only within its quad (normals/UVs differ per face); cross-quad dedup waits on `RisePerfTests`' `Perf` table. `StackDepth.Combine` = THE stack rule: composite via `Compositor`, solid pixel takes depth of TOPMOST layer solid there (no map = paper), clear top defers. `DepthFill.All` = fill-all, replaces every solid pixel. `ModelWriter` = three formats from **one** internal glTF record model (`Gltf/`): GLB / `.gltf`+`.bin` / `.gltf` data URI differ ONLY in `buffers[0].uri`, set in object model, never string-replaced (D2). Texture = WHOLE art re-encoded 8-bit RGBA, `SkipMetadata` — palette/text/resolution in source PNG can't change model bytes. `generator` "nfty", no version → release can't change bytes. Test-side `GltfValidator` asserts every spec point (`POSITION` min/max = real extents, 4-byte chunk pads — JSON spaces / BIN zeros, 4-aligned accessors, `UNSIGNED_INT`, target-less PNG view, `NEAREST`/`CLAMP_TO_EDGE`, `MASK` 0.5, single-sided D3, named nfty D4); `The_validator_is_not_vacuous` plants each defect. Every field readonly (D7), reflection sweep. Out of this assembly's scope so far: `generate --models`, `models/` in Set, stamp isolation (step 8), rasterizer (step 6).
```

- [ ] **Step 3: CLAUDE.md — the `model` command**

In the `- **`Nfty.Cli`** — …` bullet, replace:

```
through same render `generate` uses), plus authoring
```

with:

```
through same render `generate` uses), `model` (Rise via `Nfty.Model`: `.png` = paper unless filled, or `.igt` + `--variant` rising variant's STORED pixels + stored depth — Dynamic/Static variant rises as its grayscale value-map, no `--color`; `--front N --back M` = fill-all, BOTH or neither, replaces stored depth on every solid pixel; `--out` extension must match `--format`; `.png` recognized before `Archives.TryKindOf` since picture isn't archive kind; other form's options rejected), plus authoring
```

- [ ] **Step 4: The four mirror files**

`AGENTS.md`, `.clinerules/project.md`, `.github/copilot-instructions.md` and `.opencode/AGENTS.md` are identical copies. In each, replace:

```
dotnet test nfty.sln      # all four projects
```

with:

```
dotnet test nfty.sln      # all five test projects
```

Then confirm they are still identical:

```bash
diff AGENTS.md .clinerules/project.md
diff AGENTS.md .github/copilot-instructions.md
diff AGENTS.md .opencode/AGENTS.md
```

Expected: no output.

- [ ] **Step 5: README.md**

In the architecture tree, replace:

```
│   ├── Nfty.Cli/           System.CommandLine wiring              (net10.0)
```

with:

```
│   ├── Nfty.Model/         Rise: pixel art to a 3D model, glTF    (net10.0)
│   ├── Nfty.Cli/           System.CommandLine wiring              (net10.0)
```

In the command table, after the `| \`serve <mnu>\` | … |` row, add:

```markdown
| `model <png\|igt>` | Rise a picture (or one Ingredient variant, with its stored depth) into a 3D model: GLB, glTF + `.bin`, or glTF with the buffer inline. `--front N --back M` raises every opaque pixel at once |
```

- [ ] **Step 6: The manual's CLI reference**

In `docs/manual/reference/cli.md`, insert this section between the `## Rendering` section and `## Cooking`:

````markdown
## Making a 3D model

```bash
nfty model sprite.png --out sprite.glb
nfty model sprite.png --out sprite.glb --front 2 --back 1
nfty model hat.igt --variant red --out hat.gltf --format gltf
```

Every fully opaque pixel becomes a column of voxels: one for the paper, plus its **front** depth
toward you and its **back** depth away from you. The column's sides take the pixel's own color. A
`.png` has no stored depth, so it is flat paper unless you fill it; an `.igt` variant uses the depth
stored with it.

| Option | Does |
|---|---|
| `--out` | The model to write. Required. Its extension must match the format: `.glb`, or `.gltf`. |
| `--variant <id>` | Which variant of an `.igt` to rise. Required for an `.igt`, refused for a `.png`. |
| `--front N --back M` | Fill-all: every opaque pixel gets front `N` and back `M` (0 to 255 each), replacing any stored depth. Pass both or neither. |
| `--format` | `glb` (one file, the default), `gltf` (text plus a `.bin` beside it) or `gltf-embedded` (one text file). |
| `--pivot` | Which point of the model sits at the origin: `bottom-center` (the default), `center` or `min-corner`. |
| `--voxel-size` | World units per pixel. Defaults to 1. |

A picture with a **partly transparent** pixel is refused, naming it: a voxel is either there or not,
and nfty does not round your art for you. `nfty inspect <file> --voxel` lists the variants that carry
partial transparency. Options belonging to the other form are **rejected**, not ignored.
````

- [ ] **Step 7: Build the manual's links and the solution**

```bash
dotnet build nfty.sln
dotnet test nfty.sln
```

Expected: `0 Warning(s)`; every test PASSES.

If `mkdocs` is installed, also run `mkdocs build --strict` and expect no warnings; if it is not, say so in the report.

- [ ] **Step 8: Commit**

```bash
git add CLAUDE.md AGENTS.md .clinerules/project.md .github/copilot-instructions.md .opencode/AGENTS.md README.md docs/manual/reference/cli.md
git commit -m "docs: Nfty.Model and nfty model

CLAUDE.md gains the assembly's rules, including why the solidity test is
balanced unit segments rather than two triangles per edge. The mirrors,
README and the manual's CLI reference name the fifth test project and the
new command.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Self-review

**Spec coverage** (2026-09-25 spec, step 5 scope):

| Requirement | Task |
|---|---|
| New `src/Nfty.Model` (net10.0, ProjectReference Nfty.Core, no package) + `tests/Nfty.Model.Tests` | 1 |
| `Nfty.Core` does not reference `Nfty.Model` | 1 (`Neither_the_engine_nor_the_roll_references_nfty_model`) |
| Surface: `ModelFormat`, `Pivot`, `ModelFile`, `Mesh`, `RiseOptions` | 1 |
| Surface: `DepthMap` | Ruling 1 — reused |
| Surface: `Riser.TryBuild` (null when nothing solid) | 3 |
| Surface: `ModelWriter.Write` (one entry for GLB/embedded, two for GltfWithBin) | 5, 6 |
| Surface: `StackDepth.Combine` | 4 |
| Partial alpha refused first, naming the source | 3 (`Partial_alpha_is_refused_by_name`, `..._checked_before_emptiness...`) |
| Two-sided depth; thickness 1 + front + back; column extent formula | 3 (`Front_rises_toward_the_viewer...`, volume check) |
| Faces greedy on equal front / equal back | 2, 3 |
| Walls: interval minus neighbor, two segments | 3 (`A_neighbor_inside_both_ends...`) |
| Walls sample pixel center; faces sample art | 3 |
| Closed manifold anchor for plus, L, single, square, hole, checkerboard × paper, front, back, both, differing-both-sides | 3 (Ruling 5 states the true property) |
| Triangle counts, bounding boxes, pivot placement | 3 |
| Stack combine: topmost solid wins; transparent top defers | 4 |
| Fill-all | 4 (`DepthFill`), 7 (CLI) |
| Three formats from one JSON object model, no string replacement | 5, 6 (`All_three_formats_are_one_document...`) |
| Validator for each format; `.gltf` forms parse JSON and check the same invariants | 5, 6 |
| Data URI decodes to exactly the `.bin` | 6 |
| Byte-identical twice | 5, 6, 3 (mesh) |
| `POSITION` min/max | 5 (validator + `Position_min_and_max...`) |
| 4-byte chunk padding (JSON spaces, BIN zeros) | 5 (`ReadGlb`, `Both_chunks_are_padded...`) |
| Component-aligned accessors | 5 (validator; `misaligned` defect) |
| `UNSIGNED_INT` indices | 5 (`Indices_are_unsigned_int...`; `short-indices` defect) |
| PNG in a target-less bufferView | 5 (`The_png_sits_in_a_buffer_view_with_no_target`; `image-target` defect) |
| `NEAREST`/`CLAMP_TO_EDGE` sampler | 5 (`The_sampler_is_nearest...`; `sampler-linear` defect) |
| `alphaMode: MASK` | 5 (`Alpha_is_a_mask_cut_at_one_half`) |
| D1 one solid test | 3 (`Alpha_254_is_partial...`), Global Constraints |
| D3 single-sided | 5 (`The_material_is_single_sided...`; `double-sided` defect) |
| D4 named for nfty | 5 |
| D5 skip-and-name mechanism (null, not throw) | 3 (`Art_with_nothing_solid_returns_null...`); batch use is step 8 |
| D6 image, never bytes | 5 (`Every_entry_point_takes_live_images_never_bytes`) |
| D7 nothing cached lazily | 1 (`No_field_in_the_assembly_can_be_written_after_construction`, `Bounds_are_computed_when_the_mesh_is_built`) |
| GreedyRects (tiling property) | 2 |
| Vertex dedup and GreedyRects cost measured with Perf before optimizing | 1 (rule in `Mesh` remarks), 6 (`RisePerfTests`, Step 5) |
| CLI `nfty model` with every listed option; dispatch; cross-form rejection; stored depth + fill-all on top | 7 |
| `generate --models` | Out of scope — step 8 (Ruling 17) |
| Docs, mirrors, test-project list, CI | 1 (CI), 8 |

**Placeholder scan:** the only deliberately open text is Task 7 Step 7's viewer name and date, which must be written from what actually happened and is explicitly skipped (and reported) if no viewer was used. No "TBD", no "similar to Task N"; every code step carries its code.

**Type consistency:** `PixelRect(X, Y, Width, Height, Key)` — Task 2 defines, Task 3 consumes `r.X/r.Y/r.Width/r.Height/r.Key`. `GreedyRects.Unset` / `Merge(int, int, ReadOnlySpan<int>)` — same in 2 and 3. `Mesh(float[], float[], float[], uint[], int, int)` with `Positions/Normals/TexCoords/Indices/VertexCount/TriangleCount/Min/Max/TextureWidth/TextureHeight` — Task 1 defines; Tasks 3, 5, 6 use exactly these. `Riser.TryBuild(Image<Rgba32>, DepthMap?, RiseOptions, string source = "the art")` — Tasks 3, 5, 6, 7. `PartialAlphaException(itemName, x, y, alpha, count)` with `ItemName/X/Y/Alpha/Count` — Tasks 3, 7. `StackDepth.Combine(IReadOnlyList<(Image<Rgba32> Layer, DepthMap? Depth)>)` → `(Image<Rgba32> Art, DepthMap Depth)` — Tasks 4, 6. `DepthFill.All(Image<Rgba32>, byte, byte)` — Tasks 4, 7. `ModelWriter.Write(Mesh, Image<Rgba32>, RiseOptions, string)` → `IReadOnlyList<ModelFile>` — Tasks 5, 6, 7. Test helpers `Art.FromAscii/Opaque/Depth`, `Shapes.All/Patterns/Uniform/EveryShapeAndPattern/UnpinchedShapesUnderUniformDepth/Voxels`, `MeshChecks.*`, `GltfValidator.ReadGlb/ReadGltf/AssertValid/ReadFloats/ReadUInts/ReadImage/Reframe/DataUriPrefix`, `Models.Write/Glb` — defined once, used with the same names.
