# Ingredient Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `Nfty.Core.Editing` — the tested, framework-agnostic engine for editing one Ingredient's grayscale value-maps into a `.igt` — and the on-brand `ingredient-editor.html` design mockup.

**Architecture:** A new `Nfty.Core.Editing` namespace owns a mutable `ValueMap` raster (raw value+alpha buffer, grayscale by construction), reversible edit commands over it with an undo/redo `EditHistory`, editable `IngredientDraft`/`VariantDraft` graphs, an exporter to the existing `.igt` archive, and a colorized preview reusing the real cook path. The Avalonia UI (later) supplies pointer input + rendering; this plan builds only the library + the HTML mockup. No `Nfty.Cli` changes.

**Tech Stack:** C# / .NET 10, xUnit, SixLabors.ImageSharp 3.1.11, self-contained HTML/CSS for the mockup.

## Global Constraints

- Target **.NET 10**; tests are **xUnit**, method names in `Snake_case_sentences` (matched by `dotnet test --filter FullyQualifiedName~...`).
- **ImageSharp is pinned to 3.1.11** — do not upgrade it.
- New library code lives in namespace **`Nfty.Core.Editing`** (folder `src/Nfty.Core/Editing/`). No changes to `Nfty.Cli`.
- **Vocabulary:** metaphor for the five identities (CookBook/Recipe/Ingredient/Variant/Set); literal for machinery (`ValueMap`, `Brush`, `EditHistory`, …). Never "Authoring"/"Mint" in this namespace.
- **Grayscale = the R channel is the value; alpha is preserved** (matches `Imaging.Colorizer`, which reads `row[x].R`). A value-map pixel materializes to `Rgba32(v, v, v, a)`.
- **Canvas is the single source of truth for size** — every variant raster is created at the CookBook `Dimensions`.
- **Callers own image disposal.** `ValueMap.ToImage()`, `IngredientDraftExporter.Export`, and `ColorizedPreview.Render` return **live** `Image<Rgba32>` the caller must dispose.
- Tests build fixtures in memory from tiny rasters; filesystem tests use `Directory.CreateTempSubdirectory()`.
- Mockup: token block copied **verbatim** from `explorer.html`; a new hex literal anywhere is the drift signal.

---

## Phase 1 — `Nfty.Core.Editing` library

### Task 1: `ValueMap` — the editable raster

**Files:**
- Create: `src/Nfty.Core/Editing/ValueMap.cs`
- Test: `tests/Nfty.Core.Tests/ValueMapTests.cs`

**Interfaces:**
- Consumes: `Nfty.Core.Model.Dimensions`, `SixLabors.ImageSharp.Image<Rgba32>`.
- Produces: `ValueMap(int width, int height)`; `ValueMap.ForCanvas(Dimensions)`; `int Width`/`int Height`; `bool InBounds(int,int)`; `byte GetValue(int,int)`; `byte GetAlpha(int,int)`; `void Set(int x,int y,byte value,byte alpha)`; `Image<Rgba32> ToImage()`; `static ValueMap FromImage(Image<Rgba32>)`.

- [ ] **Step 1: Write the failing test**

```csharp
using Nfty.Core.Editing;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

public class ValueMapTests
{
    [Fact]
    public void New_map_is_fully_transparent_and_zero_value()
    {
        var m = ValueMap.ForCanvas(new Dimensions(4, 3));
        Assert.Equal(4, m.Width);
        Assert.Equal(3, m.Height);
        Assert.Equal(0, m.GetValue(2, 1));
        Assert.Equal(0, m.GetAlpha(2, 1));
    }

    [Fact]
    public void ToImage_writes_grayscale_R_equals_G_equals_B_and_preserves_alpha()
    {
        var m = new ValueMap(2, 1);
        m.Set(0, 0, 200, 255);
        m.Set(1, 0, 40, 128);
        using Image<Rgba32> img = m.ToImage();
        Assert.Equal(new Rgba32(200, 200, 200, 255), img[0, 0]);
        Assert.Equal(new Rgba32(40, 40, 40, 128), img[1, 0]);
    }

    [Fact]
    public void FromImage_reads_R_as_value_and_A_as_alpha()
    {
        using var img = new Image<Rgba32>(1, 1, new Rgba32(150, 10, 10, 90));
        var m = ValueMap.FromImage(img);
        Assert.Equal(150, m.GetValue(0, 0));
        Assert.Equal(90, m.GetAlpha(0, 0));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~ValueMapTests`
Expected: FAIL — `ValueMap` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

```csharp
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Editing;

/// <summary>
/// Editable single-layer raster: one grayscale value (0–255) plus one alpha (0–255) per pixel,
/// bound to a fixed canvas size. Grayscale is guaranteed by construction — there is no way to
/// store independent R/G/B. Materialize an <see cref="Image{Rgba32}"/> only at export/preview.
/// </summary>
public sealed class ValueMap
{
    private readonly byte[] _value;
    private readonly byte[] _alpha;

    public int Width { get; }
    public int Height { get; }

    public ValueMap(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "ValueMap dimensions must be positive.");
        Width = width;
        Height = height;
        _value = new byte[width * height];
        _alpha = new byte[width * height];
    }

    public static ValueMap ForCanvas(Dimensions canvas) => new(canvas.Width, canvas.Height);

    private int Index(int x, int y) => y * Width + x;
    public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

    public byte GetValue(int x, int y) => _value[Index(x, y)];
    public byte GetAlpha(int x, int y) => _alpha[Index(x, y)];

    public void Set(int x, int y, byte value, byte alpha)
    {
        int i = Index(x, y);
        _value[i] = value;
        _alpha[i] = alpha;
    }

    public Image<Rgba32> ToImage()
    {
        var img = new Image<Rgba32>(Width, Height);
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < Width; x++)
                {
                    byte v = _value[Index(x, y)];
                    row[x] = new Rgba32(v, v, v, _alpha[Index(x, y)]);
                }
            }
        });
        return img;
    }

    public static ValueMap FromImage(Image<Rgba32> img)
    {
        var map = new ValueMap(img.Width, img.Height);
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < img.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < img.Width; x++)
                    map.Set(x, y, row[x].R, row[x].A);
            }
        });
        return map;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~ValueMapTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.Core/Editing/ValueMap.cs tests/Nfty.Core.Tests/ValueMapTests.cs
git commit -m "feat(editing): ValueMap grayscale+alpha raster with image round-trip"
```

---

### Task 2: `IEditCommand` + `RegionEditCommand` + `EditHistory`

**Files:**
- Create: `src/Nfty.Core/Editing/IEditCommand.cs`, `src/Nfty.Core/Editing/RegionEditCommand.cs`, `src/Nfty.Core/Editing/EditHistory.cs`
- Test: `tests/Nfty.Core.Tests/EditHistoryTests.cs`

**Interfaces:**
- Consumes: `ValueMap` (Task 1).
- Produces: `interface IEditCommand { void Apply(ValueMap); void Undo(ValueMap); }`; `abstract class RegionEditCommand : IEditCommand` with `protected abstract IReadOnlyList<(int x,int y,byte value,byte alpha)> ComputePixels(ValueMap map)`; `class EditHistory` with `bool CanUndo`/`bool CanRedo`, `void Do(IEditCommand,ValueMap)`, `void Undo(ValueMap)`, `void Redo(ValueMap)`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using Nfty.Core.Editing;
using Xunit;

namespace Nfty.Core.Tests;

public class EditHistoryTests
{
    // Minimal concrete command: set one pixel to (value, alpha).
    private sealed class Poke : RegionEditCommand
    {
        private readonly int _x, _y; private readonly byte _v, _a;
        public Poke(int x, int y, byte v, byte a) { _x = x; _y = y; _v = v; _a = a; }
        protected override IReadOnlyList<(int x, int y, byte value, byte alpha)> ComputePixels(ValueMap map)
            => new[] { (_x, _y, _v, _a) };
    }

    [Fact]
    public void Do_then_undo_then_redo_restores_and_reapplies()
    {
        var map = new ValueMap(2, 2);
        var hist = new EditHistory();
        hist.Do(new Poke(1, 1, 123, 255), map);
        Assert.Equal(123, map.GetValue(1, 1));

        hist.Undo(map);
        Assert.Equal(0, map.GetValue(1, 1));
        Assert.Equal(0, map.GetAlpha(1, 1));

        hist.Redo(map);
        Assert.Equal(123, map.GetValue(1, 1));
        Assert.Equal(255, map.GetAlpha(1, 1));
    }

    [Fact]
    public void New_edit_clears_the_redo_stack()
    {
        var map = new ValueMap(2, 2);
        var hist = new EditHistory();
        hist.Do(new Poke(0, 0, 10, 255), map);
        hist.Undo(map);
        hist.Do(new Poke(1, 0, 20, 255), map);
        Assert.False(hist.CanRedo);
        hist.Redo(map); // no-op
        Assert.Equal(0, map.GetValue(0, 0));
        Assert.Equal(20, map.GetValue(1, 0));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~EditHistoryTests`
Expected: FAIL — `IEditCommand`/`RegionEditCommand`/`EditHistory` do not exist.

- [ ] **Step 3: Write minimal implementation**

`IEditCommand.cs`:
```csharp
namespace Nfty.Core.Editing;

/// <summary>One reversible edit over a <see cref="ValueMap"/>. Apply captures enough to Undo.</summary>
public interface IEditCommand
{
    void Apply(ValueMap map);
    void Undo(ValueMap map);
}
```

`RegionEditCommand.cs`:
```csharp
namespace Nfty.Core.Editing;

/// <summary>
/// Base for edits expressed as "these pixels get these new (value, alpha)". The new pixels are
/// computed once, before any mutation, and the prior pixels are snapshotted for undo — so redo is
/// just Apply again. Region-scoped, so history stays memory-light even on a large canvas.
/// </summary>
public abstract class RegionEditCommand : IEditCommand
{
    private (int x, int y, byte v, byte a)[]? _after;
    private (int x, int y, byte v, byte a)[]? _before;

    /// <summary>Target pixels and their new (value, alpha). Only in-bounds pixels; computed before mutation.</summary>
    protected abstract IReadOnlyList<(int x, int y, byte value, byte alpha)> ComputePixels(ValueMap map);

    public void Apply(ValueMap map)
    {
        if (_after is null)
        {
            var px = ComputePixels(map);
            var after = new (int, int, byte, byte)[px.Count];
            var before = new (int, int, byte, byte)[px.Count];
            for (int i = 0; i < px.Count; i++)
            {
                var (x, y, v, a) = px[i];
                after[i] = (x, y, v, a);
                before[i] = (x, y, map.GetValue(x, y), map.GetAlpha(x, y));
            }
            _after = after;
            _before = before;
        }
        foreach (var (x, y, v, a) in _after)
            map.Set(x, y, v, a);
    }

    public void Undo(ValueMap map)
    {
        if (_before is null) return;
        foreach (var (x, y, v, a) in _before)
            map.Set(x, y, v, a);
    }
}
```

`EditHistory.cs`:
```csharp
namespace Nfty.Core.Editing;

/// <summary>Undo/redo stack of reversible edit commands.</summary>
public sealed class EditHistory
{
    private readonly Stack<IEditCommand> _undo = new();
    private readonly Stack<IEditCommand> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Do(IEditCommand cmd, ValueMap map)
    {
        cmd.Apply(map);
        _undo.Push(cmd);
        _redo.Clear();
    }

    public void Undo(ValueMap map)
    {
        if (_undo.Count == 0) return;
        var cmd = _undo.Pop();
        cmd.Undo(map);
        _redo.Push(cmd);
    }

    public void Redo(ValueMap map)
    {
        if (_redo.Count == 0) return;
        var cmd = _redo.Pop();
        cmd.Apply(map);
        _undo.Push(cmd);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~EditHistoryTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.Core/Editing/IEditCommand.cs src/Nfty.Core/Editing/RegionEditCommand.cs src/Nfty.Core/Editing/EditHistory.cs tests/Nfty.Core.Tests/EditHistoryTests.cs
git commit -m "feat(editing): reversible edit command base + undo/redo history"
```

---

### Task 3: `Brush` + `BrushStroke`

**Files:**
- Create: `src/Nfty.Core/Editing/Brush.cs`, `src/Nfty.Core/Editing/BrushStroke.cs`
- Test: `tests/Nfty.Core.Tests/BrushStrokeTests.cs`

**Interfaces:**
- Consumes: `ValueMap`, `RegionEditCommand`.
- Produces: `readonly record struct Brush(int Size, byte Value)`; `class BrushStroke(Brush brush, IReadOnlyList<(int x,int y)> path) : RegionEditCommand`. A disc of diameter `Size` (min 1) is stamped at each path point, writing `Value` at alpha 255.

- [ ] **Step 1: Write the failing test**

```csharp
using Nfty.Core.Editing;
using Xunit;

namespace Nfty.Core.Tests;

public class BrushStrokeTests
{
    [Fact]
    public void Size_one_brush_paints_a_single_pixel_at_full_alpha()
    {
        var map = new ValueMap(3, 3);
        var stroke = new BrushStroke(new Brush(1, 180), new[] { (1, 1) });
        stroke.Apply(map);
        Assert.Equal(180, map.GetValue(1, 1));
        Assert.Equal(255, map.GetAlpha(1, 1));
        Assert.Equal(0, map.GetAlpha(0, 0)); // untouched
    }

    [Fact]
    public void Stroke_clips_to_bounds()
    {
        var map = new ValueMap(2, 2);
        var stroke = new BrushStroke(new Brush(3, 90), new[] { (0, 0) });
        stroke.Apply(map); // disc would spill past the edge; must not throw
        Assert.Equal(90, map.GetValue(0, 0));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~BrushStrokeTests`
Expected: FAIL — `Brush`/`BrushStroke` do not exist.

- [ ] **Step 3: Write minimal implementation**

`Brush.cs`:
```csharp
namespace Nfty.Core.Editing;

/// <summary>Brush settings: stamp diameter in pixels and the grayscale value it paints.</summary>
public readonly record struct Brush(int Size, byte Value);
```

`BrushStroke.cs`:
```csharp
namespace Nfty.Core.Editing;

/// <summary>Paints the brush's value (at full alpha) as a filled disc stamped along a path.</summary>
public sealed class BrushStroke : RegionEditCommand
{
    private readonly Brush _brush;
    private readonly IReadOnlyList<(int x, int y)> _path;

    public BrushStroke(Brush brush, IReadOnlyList<(int x, int y)> path)
    {
        _brush = brush;
        _path = path;
    }

    protected override IReadOnlyList<(int x, int y, byte value, byte alpha)> ComputePixels(ValueMap map)
    {
        int d = Math.Max(1, _brush.Size);
        double r = d / 2.0;
        int ir = (int)Math.Ceiling(r);
        var seen = new HashSet<(int, int)>();
        var pixels = new List<(int, int, byte, byte)>();
        foreach (var (cx, cy) in _path)
            for (int dy = -ir; dy <= ir; dy++)
                for (int dx = -ir; dx <= ir; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (!map.InBounds(x, y)) continue;
                    if (dx * dx + dy * dy > r * r) continue; // round disc; size 1 (r=0.5) collapses to one pixel
                    if (seen.Add((x, y)))
                        pixels.Add((x, y, _brush.Value, (byte)255));
                }
        return pixels;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~BrushStrokeTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.Core/Editing/Brush.cs src/Nfty.Core/Editing/BrushStroke.cs tests/Nfty.Core.Tests/BrushStrokeTests.cs
git commit -m "feat(editing): brush + brush-stroke command"
```

---

### Task 4: `EraseStroke`

**Files:**
- Create: `src/Nfty.Core/Editing/EraseStroke.cs`
- Test: `tests/Nfty.Core.Tests/EraseStrokeTests.cs`

**Interfaces:**
- Consumes: `ValueMap`, `RegionEditCommand`.
- Produces: `class EraseStroke(int size, IReadOnlyList<(int x,int y)> path) : RegionEditCommand`. Sets alpha to 0 (keeps the existing value) over a disc stamped along the path.

- [ ] **Step 1: Write the failing test**

```csharp
using Nfty.Core.Editing;
using Xunit;

namespace Nfty.Core.Tests;

public class EraseStrokeTests
{
    [Fact]
    public void Erase_sets_alpha_to_zero_and_leaves_value()
    {
        var map = new ValueMap(3, 3);
        new BrushStroke(new Brush(1, 200), new[] { (1, 1) }).Apply(map);
        new EraseStroke(1, new[] { (1, 1) }).Apply(map);
        Assert.Equal(0, map.GetAlpha(1, 1));
        Assert.Equal(200, map.GetValue(1, 1)); // value untouched
    }

    [Fact]
    public void Erase_is_undoable()
    {
        var map = new ValueMap(3, 3);
        new BrushStroke(new Brush(1, 200), new[] { (1, 1) }).Apply(map);
        var erase = new EraseStroke(1, new[] { (1, 1) });
        erase.Apply(map);
        erase.Undo(map);
        Assert.Equal(255, map.GetAlpha(1, 1)); // restored to the painted alpha
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~EraseStrokeTests`
Expected: FAIL — `EraseStroke` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace Nfty.Core.Editing;

/// <summary>Erases to transparency — sets alpha to 0, keeping each pixel's value — along a path.</summary>
public sealed class EraseStroke : RegionEditCommand
{
    private readonly int _size;
    private readonly IReadOnlyList<(int x, int y)> _path;

    public EraseStroke(int size, IReadOnlyList<(int x, int y)> path)
    {
        _size = size;
        _path = path;
    }

    protected override IReadOnlyList<(int x, int y, byte value, byte alpha)> ComputePixels(ValueMap map)
    {
        int d = Math.Max(1, _size);
        double r = d / 2.0;
        int ir = (int)Math.Ceiling(r);
        var seen = new HashSet<(int, int)>();
        var pixels = new List<(int, int, byte, byte)>();
        foreach (var (cx, cy) in _path)
            for (int dy = -ir; dy <= ir; dy++)
                for (int dx = -ir; dx <= ir; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (!map.InBounds(x, y)) continue;
                    if (dx * dx + dy * dy > r * r) continue; // round disc; size 1 (r=0.5) collapses to one pixel
                    if (seen.Add((x, y)))
                        pixels.Add((x, y, map.GetValue(x, y), (byte)0));
                }
        return pixels;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~EraseStrokeTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.Core/Editing/EraseStroke.cs tests/Nfty.Core.Tests/EraseStrokeTests.cs
git commit -m "feat(editing): eraser stroke (alpha to zero)"
```

---

### Task 5: `FloodFill`

**Files:**
- Create: `src/Nfty.Core/Editing/FloodFill.cs`
- Test: `tests/Nfty.Core.Tests/FloodFillTests.cs`

**Interfaces:**
- Consumes: `ValueMap`, `RegionEditCommand`.
- Produces: `class FloodFill(int seedX, int seedY, byte value) : RegionEditCommand`. 4-connected flood of every pixel matching the seed's current (value, alpha), setting them to `value` at alpha 255.

- [ ] **Step 1: Write the failing test**

```csharp
using Nfty.Core.Editing;
using Xunit;

namespace Nfty.Core.Tests;

public class FloodFillTests
{
    [Fact]
    public void Fills_contiguous_matching_region_only()
    {
        var map = new ValueMap(3, 1);
        // left two pixels are (0,0); right pixel is a different value/alpha "wall"
        new BrushStroke(new Brush(1, 50), new[] { (2, 0) }).Apply(map); // (2,0) => value 50, alpha 255
        new FloodFill(0, 0, 220).Apply(map);
        Assert.Equal(220, map.GetValue(0, 0));
        Assert.Equal(255, map.GetAlpha(0, 0));
        Assert.Equal(220, map.GetValue(1, 0));
        Assert.Equal(50, map.GetValue(2, 0)); // wall untouched
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~FloodFillTests`
Expected: FAIL — `FloodFill` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace Nfty.Core.Editing;

/// <summary>4-connected flood fill of the region matching the seed pixel's (value, alpha).</summary>
public sealed class FloodFill : RegionEditCommand
{
    private readonly int _seedX, _seedY;
    private readonly byte _value;

    public FloodFill(int seedX, int seedY, byte value)
    {
        _seedX = seedX;
        _seedY = seedY;
        _value = value;
    }

    protected override IReadOnlyList<(int x, int y, byte value, byte alpha)> ComputePixels(ValueMap map)
    {
        var pixels = new List<(int, int, byte, byte)>();
        if (!map.InBounds(_seedX, _seedY)) return pixels;

        byte tv = map.GetValue(_seedX, _seedY), ta = map.GetAlpha(_seedX, _seedY);
        if (tv == _value && ta == 255) return pixels; // no-op fill

        var seen = new HashSet<(int, int)>();
        var queue = new Queue<(int, int)>();
        queue.Enqueue((_seedX, _seedY));
        seen.Add((_seedX, _seedY));
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            if (!map.InBounds(x, y)) continue;
            if (map.GetValue(x, y) != tv || map.GetAlpha(x, y) != ta) continue;
            pixels.Add((x, y, _value, (byte)255));
            foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                if (map.InBounds(nx, ny) && seen.Add((nx, ny)))
                    queue.Enqueue((nx, ny));
        }
        return pixels;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~FloodFillTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.Core/Editing/FloodFill.cs tests/Nfty.Core.Tests/FloodFillTests.cs
git commit -m "feat(editing): flood fill command"
```

---

### Task 6: `PixelRect` + `ShapeKind` + `DrawShape`

**Files:**
- Create: `src/Nfty.Core/Editing/PixelRect.cs`, `src/Nfty.Core/Editing/ShapeKind.cs`, `src/Nfty.Core/Editing/DrawShape.cs`
- Test: `tests/Nfty.Core.Tests/DrawShapeTests.cs`

**Interfaces:**
- Consumes: `ValueMap`, `RegionEditCommand`.
- Produces: `readonly record struct PixelRect(int X, int Y, int Width, int Height)`; `enum ShapeKind { Rectangle, Ellipse, Triangle }`; `class DrawShape(ShapeKind kind, PixelRect bounds, byte value) : RegionEditCommand` filling the shape with `value` at alpha 255.

- [ ] **Step 1: Write the failing test**

```csharp
using Nfty.Core.Editing;
using Xunit;

namespace Nfty.Core.Tests;

public class DrawShapeTests
{
    [Fact]
    public void Rectangle_fills_exactly_its_bounds()
    {
        var map = new ValueMap(4, 4);
        new DrawShape(ShapeKind.Rectangle, new PixelRect(1, 1, 2, 2), 100).Apply(map);
        Assert.Equal(100, map.GetValue(1, 1));
        Assert.Equal(100, map.GetValue(2, 2));
        Assert.Equal(0, map.GetAlpha(0, 0)); // outside
        Assert.Equal(0, map.GetAlpha(3, 3)); // outside
    }

    [Fact]
    public void Ellipse_fills_center_but_not_corner()
    {
        var map = new ValueMap(5, 5);
        new DrawShape(ShapeKind.Ellipse, new PixelRect(0, 0, 5, 5), 100).Apply(map);
        Assert.Equal(255, map.GetAlpha(2, 2)); // center inside
        Assert.Equal(0, map.GetAlpha(0, 0));   // corner outside the ellipse
    }

    [Fact]
    public void Triangle_fills_bottom_row_but_not_top_corners()
    {
        var map = new ValueMap(5, 5);
        new DrawShape(ShapeKind.Triangle, new PixelRect(0, 0, 5, 5), 100).Apply(map);
        Assert.Equal(255, map.GetAlpha(2, 4)); // bottom-center inside
        Assert.Equal(0, map.GetAlpha(0, 0));   // top-left corner outside
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~DrawShapeTests`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Write minimal implementation**

`PixelRect.cs`:
```csharp
namespace Nfty.Core.Editing;

/// <summary>An integer pixel rectangle (top-left origin).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);
```

`ShapeKind.cs`:
```csharp
namespace Nfty.Core.Editing;

public enum ShapeKind { Rectangle, Ellipse, Triangle }
```

`DrawShape.cs`:
```csharp
namespace Nfty.Core.Editing;

/// <summary>Fills a rectangle, inscribed ellipse, or upright triangle with a value at full alpha.</summary>
public sealed class DrawShape : RegionEditCommand
{
    private readonly ShapeKind _kind;
    private readonly PixelRect _b;
    private readonly byte _value;

    public DrawShape(ShapeKind kind, PixelRect bounds, byte value)
    {
        _kind = kind;
        _b = bounds;
        _value = value;
    }

    protected override IReadOnlyList<(int x, int y, byte value, byte alpha)> ComputePixels(ValueMap map)
    {
        var pixels = new List<(int, int, byte, byte)>();
        for (int y = _b.Y; y < _b.Y + _b.Height; y++)
            for (int x = _b.X; x < _b.X + _b.Width; x++)
            {
                if (!map.InBounds(x, y)) continue;
                if (Contains(x, y))
                    pixels.Add((x, y, _value, (byte)255));
            }
        return pixels;
    }

    private bool Contains(int x, int y)
    {
        switch (_kind)
        {
            case ShapeKind.Rectangle:
                return true;
            case ShapeKind.Ellipse:
            {
                double rx = _b.Width / 2.0, ry = _b.Height / 2.0;
                double cx = _b.X + rx - 0.5, cy = _b.Y + ry - 0.5;
                double nx = rx == 0 ? 0 : (x - cx) / rx, ny = ry == 0 ? 0 : (y - cy) / ry;
                return nx * nx + ny * ny <= 1.0;
            }
            case ShapeKind.Triangle:
            {
                // Upright: apex at top-center, base along the bottom edge. Half-width grows toward the base.
                double t = _b.Height <= 1 ? 1 : (y - _b.Y) / (double)(_b.Height - 1);
                double halfW = t * (_b.Width / 2.0);
                double cx = _b.X + _b.Width / 2.0 - 0.5;
                return Math.Abs(x - cx) <= halfW;
            }
            default:
                return false;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~DrawShapeTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.Core/Editing/PixelRect.cs src/Nfty.Core/Editing/ShapeKind.cs src/Nfty.Core/Editing/DrawShape.cs tests/Nfty.Core.Tests/DrawShapeTests.cs
git commit -m "feat(editing): rectangle/ellipse/triangle shape fill"
```

---

### Task 7: `MoveSelection`

**Files:**
- Create: `src/Nfty.Core/Editing/MoveSelection.cs`
- Test: `tests/Nfty.Core.Tests/MoveSelectionTests.cs`

**Interfaces:**
- Consumes: `ValueMap`, `RegionEditCommand`, `PixelRect` (Task 6).
- Produces: `class MoveSelection(PixelRect source, int dx, int dy) : RegionEditCommand`. Lifts the pixels in `source`, clears the source area to transparent (value 0, alpha 0), and writes the lifted pixels shifted by (dx, dy) — the "select region + move" tool on a single flat raster.

- [ ] **Step 1: Write the failing test**

```csharp
using Nfty.Core.Editing;
using Xunit;

namespace Nfty.Core.Tests;

public class MoveSelectionTests
{
    [Fact]
    public void Moves_pixels_and_clears_the_source()
    {
        var map = new ValueMap(4, 1);
        new BrushStroke(new Brush(1, 210), new[] { (0, 0) }).Apply(map);
        new MoveSelection(new PixelRect(0, 0, 1, 1), 2, 0).Apply(map);
        Assert.Equal(0, map.GetAlpha(0, 0));   // source cleared
        Assert.Equal(210, map.GetValue(2, 0)); // moved here
        Assert.Equal(255, map.GetAlpha(2, 0));
    }

    [Fact]
    public void Undo_restores_original_position()
    {
        var map = new ValueMap(4, 1);
        new BrushStroke(new Brush(1, 210), new[] { (0, 0) }).Apply(map);
        var move = new MoveSelection(new PixelRect(0, 0, 1, 1), 2, 0);
        move.Apply(map);
        move.Undo(map);
        Assert.Equal(210, map.GetValue(0, 0));
        Assert.Equal(0, map.GetAlpha(2, 0));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~MoveSelectionTests`
Expected: FAIL — `MoveSelection` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace Nfty.Core.Editing;

/// <summary>
/// Moves a rectangular selection by (dx, dy) on a single flat raster: the source area is cleared to
/// transparent and its pixels are re-stamped at the shifted position. Later writes win, so a pixel that
/// is both cleared and re-stamped ends up stamped.
/// </summary>
public sealed class MoveSelection : RegionEditCommand
{
    private readonly PixelRect _source;
    private readonly int _dx, _dy;

    public MoveSelection(PixelRect source, int dx, int dy)
    {
        _source = source;
        _dx = dx;
        _dy = dy;
    }

    protected override IReadOnlyList<(int x, int y, byte value, byte alpha)> ComputePixels(ValueMap map)
    {
        // Build a keyed map so a destination pixel overrides the source-clear at the same coordinate.
        var result = new Dictionary<(int, int), (byte v, byte a)>();
        for (int y = _source.Y; y < _source.Y + _source.Height; y++)
            for (int x = _source.X; x < _source.X + _source.Width; x++)
            {
                if (!map.InBounds(x, y)) continue;
                result[(x, y)] = (0, 0); // clear source
            }
        for (int y = _source.Y; y < _source.Y + _source.Height; y++)
            for (int x = _source.X; x < _source.X + _source.Width; x++)
            {
                if (!map.InBounds(x, y)) continue;
                int nx = x + _dx, ny = y + _dy;
                if (!map.InBounds(nx, ny)) continue;
                result[(nx, ny)] = (map.GetValue(x, y), map.GetAlpha(x, y));
            }
        var pixels = new List<(int, int, byte, byte)>(result.Count);
        foreach (var kv in result)
            pixels.Add((kv.Key.Item1, kv.Key.Item2, kv.Value.v, kv.Value.a));
        return pixels;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~MoveSelectionTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.Core/Editing/MoveSelection.cs tests/Nfty.Core.Tests/MoveSelectionTests.cs
git commit -m "feat(editing): select-region move command"
```

---

### Task 8: `VariantDraft` + `IngredientDraft`

**Files:**
- Create: `src/Nfty.Core/Editing/VariantDraft.cs`, `src/Nfty.Core/Editing/IngredientDraft.cs`
- Test: `tests/Nfty.Core.Tests/IngredientDraftTests.cs`

**Interfaces:**
- Consumes: `ValueMap`, `Nfty.Core.Model.LayerKind`, `Colorization`, `Dimensions`.
- Produces: `class VariantDraft { string Id; string Name (set); double Weight (set); ValueMap Map; }`; `class IngredientDraft { string Id; string Name (set); LayerKind Kind (set); Colorization? Colorization (set); Dimensions Canvas; List<VariantDraft> Variants; VariantDraft AddVariant(string id, string name, double weight); }`.

- [ ] **Step 1: Write the failing test**

```csharp
using Nfty.Core.Editing;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.Core.Tests;

public class IngredientDraftTests
{
    [Fact]
    public void AddVariant_creates_a_canvas_sized_value_map()
    {
        var draft = new IngredientDraft("body", "Body", LayerKind.Dynamic, null,
            new Dimensions(8, 8), System.Array.Empty<VariantDraft>());
        var v = draft.AddVariant("slime", "Slime", 40);
        Assert.Single(draft.Variants);
        Assert.Equal(8, v.Map.Width);
        Assert.Equal(8, v.Map.Height);
        Assert.Equal(40, v.Weight);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~IngredientDraftTests`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Write minimal implementation**

`VariantDraft.cs`:
```csharp
namespace Nfty.Core.Editing;

/// <summary>An editable variant: identity + weight + its grayscale value-map.</summary>
public sealed class VariantDraft
{
    public string Id { get; }
    public string Name { get; set; }
    public double Weight { get; set; }
    public ValueMap Map { get; }

    public VariantDraft(string id, string name, double weight, ValueMap map)
    {
        Id = id;
        Name = name;
        Weight = weight;
        Map = map;
    }
}
```

`IngredientDraft.cs`:
```csharp
using Nfty.Core.Model;

namespace Nfty.Core.Editing;

/// <summary>
/// The whole ingredient being edited: identity, layer kind, colorization, the fixed canvas size, and
/// its variants. Every variant's raster is created at <see cref="Canvas"/> — the single source of truth.
/// </summary>
public sealed class IngredientDraft
{
    public string Id { get; }
    public string Name { get; set; }
    public LayerKind Kind { get; set; }
    public Colorization? Colorization { get; set; }
    public Dimensions Canvas { get; }
    public List<VariantDraft> Variants { get; }

    public IngredientDraft(string id, string name, LayerKind kind, Colorization? colorization,
        Dimensions canvas, IEnumerable<VariantDraft> variants)
    {
        Id = id;
        Name = name;
        Kind = kind;
        Colorization = colorization;
        Canvas = canvas;
        Variants = variants.ToList();
    }

    public VariantDraft AddVariant(string id, string name, double weight)
    {
        var v = new VariantDraft(id, name, weight, ValueMap.ForCanvas(Canvas));
        Variants.Add(v);
        return v;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~IngredientDraftTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.Core/Editing/VariantDraft.cs src/Nfty.Core/Editing/IngredientDraft.cs tests/Nfty.Core.Tests/IngredientDraftTests.cs
git commit -m "feat(editing): editable ingredient + variant drafts"
```

---

### Task 9: `IngredientDraftExporter`

**Files:**
- Create: `src/Nfty.Core/Editing/IngredientDraftExporter.cs`
- Test: `tests/Nfty.Core.Tests/IngredientDraftExporterTests.cs`

**Interfaces:**
- Consumes: `IngredientDraft`, `VariantDraft`, `Model.IngredientManifest`, `Model.Variant`, `Formats.IngredientArchive`.
- Produces: `static (IngredientManifest Manifest, IReadOnlyDictionary<string, Image<Rgba32>> Images) Export(IngredientDraft draft)`. Throws `InvalidOperationException` on duplicate variant ids. Returned images are live (caller disposes).

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

public class IngredientDraftExporterTests
{
    private static IngredientDraft TwoVariantDraft()
    {
        var draft = new IngredientDraft("body", "Body", LayerKind.Dynamic, null,
            new Dimensions(2, 2), System.Array.Empty<VariantDraft>());
        var a = draft.AddVariant("slime", "Slime", 40);
        a.Map.Set(0, 0, 128, 255);
        draft.AddVariant("fluff", "Fluff", 25);
        return draft;
    }

    [Fact]
    public void Export_maps_variants_and_grayscale_images()
    {
        var (manifest, images) = IngredientDraftExporter.Export(TwoVariantDraft());
        Assert.Equal("body", manifest.Id);
        Assert.Equal(LayerKind.Dynamic, manifest.Kind);
        Assert.Equal(2, manifest.Variants.Count);
        Assert.Equal(new Rgba32(128, 128, 128, 255), images["slime"][0, 0]);
        foreach (var img in images.Values) img.Dispose();
    }

    [Fact]
    public void Export_round_trips_through_the_igt_archive()
    {
        var (manifest, images) = IngredientDraftExporter.Export(TwoVariantDraft());
        var dir = System.IO.Directory.CreateTempSubdirectory();
        var path = System.IO.Path.Combine(dir.FullName, "body.igt");
        IngredientArchive.Write(path, manifest, images);
        foreach (var img in images.Values) img.Dispose();

        using var loaded = IngredientArchive.Read(path);
        Assert.Equal("body", loaded.Manifest.Id);
        Assert.Equal(2, loaded.Manifest.Variants.Count);
        Assert.Equal(new Rgba32(128, 128, 128, 255), loaded.VariantImages["slime"][0, 0]);
    }

    [Fact]
    public void Duplicate_variant_ids_throw()
    {
        var draft = new IngredientDraft("body", "Body", LayerKind.Dynamic, null,
            new Dimensions(2, 2), System.Array.Empty<VariantDraft>());
        draft.AddVariant("dup", "One", 1);
        draft.AddVariant("dup", "Two", 1);
        Assert.Throws<System.InvalidOperationException>(() => IngredientDraftExporter.Export(draft));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~IngredientDraftExporterTests`
Expected: FAIL — `IngredientDraftExporter` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Editing;

/// <summary>
/// Turns an <see cref="IngredientDraft"/> into the pair the existing
/// <see cref="Formats.IngredientArchive"/> writes: a manifest plus one live image per variant id.
/// Callers own the returned images.
/// </summary>
public static class IngredientDraftExporter
{
    public static (IngredientManifest Manifest, IReadOnlyDictionary<string, Image<Rgba32>> Images) Export(
        IngredientDraft draft)
    {
        var ids = new HashSet<string>();
        foreach (var v in draft.Variants)
            if (!ids.Add(v.Id))
                throw new InvalidOperationException($"Duplicate variant id '{v.Id}' in ingredient '{draft.Id}'.");

        var variants = draft.Variants.Select(v => new Variant(v.Id, v.Name, v.Weight)).ToList();
        var manifest = new IngredientManifest(draft.Id, draft.Name, draft.Kind, draft.Colorization, variants);
        var images = draft.Variants.ToDictionary(v => v.Id, v => v.Map.ToImage());
        return (manifest, images);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~IngredientDraftExporterTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.Core/Editing/IngredientDraftExporter.cs tests/Nfty.Core.Tests/IngredientDraftExporterTests.cs
git commit -m "feat(editing): export ingredient draft to .igt manifest + images"
```

---

### Task 10: `ColorizedPreview`

**Files:**
- Create: `src/Nfty.Core/Editing/ColorizedPreview.cs`
- Test: `tests/Nfty.Core.Tests/ColorizedPreviewTests.cs`

**Interfaces:**
- Consumes: `VariantDraft`, `Model.LayerKind`, `Model.Colorization`, `Imaging.Colorizer`, `Imaging.ColorConvert`, `Generation.ColorRoller`, `Generation.IRng`/`SplitMix64Rng`.
- Produces: `static Image<Rgba32> Render(VariantDraft variant, LayerKind kind, Colorization? colorization, IRng rng)`. Custom or null colorization → the value image as-is; otherwise roll `(H,S)` via `ColorRoller` and colorize via `Colorizer.Apply`. Returned image is live (caller disposes).

- [ ] **Step 1: Write the failing test**

```csharp
using Nfty.Core.Editing;
using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

public class ColorizedPreviewTests
{
    private static VariantDraft Gray(byte value)
    {
        var map = new ValueMap(1, 1);
        map.Set(0, 0, value, 255);
        return new VariantDraft("v", "V", 1, map);
    }

    [Fact]
    public void Static_fixed_colour_matches_the_colorizer_output_exactly()
    {
        var colorization = new Colorization(ColorModel.Hsv, 1, 1,
            new[] { new ColorEntry(1, null, "hsv:200,50,50") });
        var rgb = ColorSpec.Parse("hsv:200,50,50");
        var (h, s, _) = ColorConvert.RgbToHsv(rgb);
        var expected = ColorConvert.HsvToRgb(h, s, 128 / 255.0);

        using Image<Rgba32> preview = ColorizedPreview.Render(Gray(128), LayerKind.Static, colorization,
            new SplitMix64Rng(1));
        Assert.Equal(new Rgba32(expected.R, expected.G, expected.B, 255), preview[0, 0]);
    }

    [Fact]
    public void Dynamic_is_deterministic_for_a_fixed_seed()
    {
        var colorization = new Colorization(ColorModel.Hsv, 12, 4,
            new[] { new ColorEntry(1, new ColorRange(196, 348, 45, 70), null) });
        using var a = ColorizedPreview.Render(Gray(128), LayerKind.Dynamic, colorization, new SplitMix64Rng(42));
        using var b = ColorizedPreview.Render(Gray(128), LayerKind.Dynamic, colorization, new SplitMix64Rng(42));
        Assert.Equal(a[0, 0], b[0, 0]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~ColorizedPreviewTests`
Expected: FAIL — `ColorizedPreview` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Editing;

/// <summary>
/// Renders what the cook produces from a variant's grayscale value-map: custom (or no colorization)
/// is returned as-is; dynamic/static roll (H,S) via the real <see cref="ColorRoller"/> and colorize via
/// the real <see cref="Colorizer"/>, so the preview matches generation. Returned image is live.
/// </summary>
public static class ColorizedPreview
{
    public static Image<Rgba32> Render(VariantDraft variant, LayerKind kind, Colorization? colorization, IRng rng)
    {
        Image<Rgba32> valueImg = variant.Map.ToImage();
        if (kind == LayerKind.Custom || colorization is null)
            return valueImg;

        using (valueImg)
        {
            var rolled = ColorRoller.Roll(colorization, rng);
            return Colorizer.Apply(valueImg, rolled.H, rolled.S, colorization.Model);
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~ColorizedPreviewTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.Core/Editing/ColorizedPreview.cs tests/Nfty.Core.Tests/ColorizedPreviewTests.cs
git commit -m "feat(editing): colorized preview via the real cook path"
```

---

### Task 11: `CookBookEdits` — splice an ingredient into a loaded cookbook

**Files:**
- Create: `src/Nfty.Core/Editing/CookBookEdits.cs`
- Test: `tests/Nfty.Core.Tests/CookBookEditsTests.cs`

**Interfaces:**
- Consumes: `Formats.LoadedCookBook`, `LoadedRecipe`, `LoadedIngredient`, `Model.RecipeManifest`.
- Produces: `static LoadedCookBook UpsertIngredient(LoadedCookBook book, string recipeId, LoadedIngredient ingredient)`. Adds or replaces (by id) the ingredient in the named recipe, appending its id to `LayerOrder` if new. Pure — returns a new graph referencing existing image objects; disposes nothing.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using System.Linq;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

public class CookBookEditsTests
{
    private static LoadedIngredient Ing(string id) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Dynamic, null,
            new[] { new Variant(id + "-v", "V", 1.0) }),
        VariantImages = new Dictionary<string, Image<Rgba32>>
        {
            [id + "-v"] = new Image<Rgba32>(1, 1)
        }
    };

    private static LoadedCookBook OneRecipeBook()
    {
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("aurora", "Aurora", new List<string> { "body" },
                System.Array.Empty<IncompatibilityRule>()),
            Ingredients = new List<LoadedIngredient> { Ing("body") }
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("vp", "VaporPets", new Dimensions(8, 8),
                new Collection("VaporPets", "", "VP"),
                new Dictionary<string, double> { ["aurora"] = 1.0 }),
            Recipes = new List<LoadedRecipe> { recipe }
        };
    }

    [Fact]
    public void Adding_a_new_ingredient_appends_it_and_updates_layer_order()
    {
        var book = CookBookEdits.UpsertIngredient(OneRecipeBook(), "aurora", Ing("ears"));
        var recipe = book.Recipes.Single();
        Assert.Equal(2, recipe.Ingredients.Count);
        Assert.Equal(new[] { "body", "ears" }, recipe.Manifest.LayerOrder);
    }

    [Fact]
    public void Replacing_an_existing_ingredient_keeps_layer_order()
    {
        var book = CookBookEdits.UpsertIngredient(OneRecipeBook(), "aurora", Ing("body"));
        var recipe = book.Recipes.Single();
        Assert.Single(recipe.Ingredients);
        Assert.Equal(new[] { "body" }, recipe.Manifest.LayerOrder);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~CookBookEditsTests`
Expected: FAIL — `CookBookEdits` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using Nfty.Core.Formats;

namespace Nfty.Core.Editing;

/// <summary>
/// Splices an edited ingredient back into a loaded cookbook. Returns a new graph that reuses the
/// existing image objects plus the new ingredient's — it disposes nothing, so the caller manages the
/// lifetime of whatever it replaces.
/// </summary>
public static class CookBookEdits
{
    public static LoadedCookBook UpsertIngredient(LoadedCookBook book, string recipeId, LoadedIngredient ingredient)
    {
        var recipes = book.Recipes.Select(r =>
        {
            if (r.Manifest.Id != recipeId) return r;

            var ings = r.Ingredients
                .Where(i => i.Manifest.Id != ingredient.Manifest.Id)
                .Append(ingredient)
                .ToList();

            var order = r.Manifest.LayerOrder.Contains(ingredient.Manifest.Id)
                ? r.Manifest.LayerOrder
                : r.Manifest.LayerOrder.Append(ingredient.Manifest.Id).ToList();

            return new LoadedRecipe { Manifest = r.Manifest with { LayerOrder = order }, Ingredients = ings };
        }).ToList();

        return new LoadedCookBook
        {
            Manifest = book.Manifest,
            Recipes = recipes,
            SourceSha256 = book.SourceSha256
        };
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~CookBookEditsTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.Core/Editing/CookBookEdits.cs tests/Nfty.Core.Tests/CookBookEditsTests.cs
git commit -m "feat(editing): upsert an edited ingredient into a loaded cookbook"
```

---

### Task 12: Validator grayscale hardening

**Files:**
- Modify: `src/Nfty.Core/Formats/Validator.cs` (the `CheckVariantImages` method)
- Test: `tests/Nfty.Core.Tests/ValidatorTests.cs` (add one test)

**Interfaces:**
- Consumes: existing `Validator.Validate`. No new public surface.
- Produces: a reported problem when a dynamic/static variant image is not grayscale (some pixel where R, G, B are not all equal).

- [ ] **Step 1: Write the failing test**

Add to `ValidatorTests.cs` (build the loaded graph the same way the existing tests in that file do; adapt the helper names to those already present):

```csharp
[Fact]
public void Non_grayscale_dynamic_variant_is_reported()
{
    // A dynamic ingredient whose only variant image has a coloured (non-grayscale) pixel.
    using var colour = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(
        8, 8, new SixLabors.ImageSharp.PixelFormats.Rgba32(200, 10, 10, 255));
    var book = BookWithSingleVariantImage(LayerKind.Dynamic, colour); // see note below
    var problems = Validator.Validate(book);
    Assert.Contains(problems, p => p.Contains("grayscale", System.StringComparison.OrdinalIgnoreCase));
}
```

Note: reuse the file's existing in-memory builder if present; otherwise construct a `LoadedCookBook` with one 8×8 canvas, one recipe, one dynamic ingredient whose single variant image is `colour`, following the construction already used elsewhere in `ValidatorTests.cs`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~ValidatorTests.Non_grayscale_dynamic_variant_is_reported`
Expected: FAIL — no grayscale problem is reported yet.

- [ ] **Step 3: Write minimal implementation**

In `Validator.cs`, inside `CheckVariantImages`, after the existing canvas-size check, add a grayscale check for non-custom kinds. Insert within the per-variant loop (the loop already has the variant image `img`, the ingredient `ing`, and the `where` context string):

```csharp
if (ing.Manifest.Kind != LayerKind.Custom && !IsGrayscale(img))
    problems.Add($"{where}: variant '{v.Id}' is not grayscale; dynamic/static value-maps must have R=G=B.");
```

Add this private helper to `Validator`:

```csharp
private static bool IsGrayscale(Image<Rgba32> img)
{
    bool gray = true;
    img.ProcessPixelRows(accessor =>
    {
        for (int y = 0; y < accessor.Height && gray; y++)
        {
            Span<Rgba32> row = accessor.GetRowSpan(y);
            for (int x = 0; x < row.Length; x++)
            {
                var p = row[x];
                if (p.R != p.G || p.G != p.B) { gray = false; break; }
            }
        }
    });
    return gray;
}
```

Ensure `using SixLabors.ImageSharp;` and `using SixLabors.ImageSharp.PixelFormats;` are present at the top of `Validator.cs` (add if missing). Match the loop variable names (`v`, `img`, `ing`, `where`) to what `CheckVariantImages` already uses; adjust if they differ.

- [ ] **Step 4: Run the full validator suite to verify pass and no regressions**

Run: `dotnet test tests/Nfty.Core.Tests --filter FullyQualifiedName~ValidatorTests`
Expected: PASS (all existing tests plus the new one).

- [ ] **Step 5: Commit**

```bash
git add src/Nfty.Core/Formats/Validator.cs tests/Nfty.Core.Tests/ValidatorTests.cs
git commit -m "feat(validator): report non-grayscale dynamic/static variant images"
```

---

### Task 13: Full library suite green

**Files:** none (verification only)

- [ ] **Step 1: Run the whole solution's tests**

Run: `dotnet test nfty.sln`
Expected: PASS — all projects, including every new `Editing` test and the untouched existing suites.

- [ ] **Step 2: Commit (only if a fix was needed)**

If any cross-cutting fix was required to get green, commit it:
```bash
git add -A
git commit -m "test: green the full suite after Editing namespace"
```

---

## Phase 2 — `ingredient-editor.html` mockup

### Task 14: Build the on-brand editor mockup

**Files:**
- Create: `docs/design/mockups/ingredient-editor.html`
- Modify: `docs/design/mockups/README.md` (add an `ingredient-editor.html` section + spec link, mirroring the explorer/landing/help sections)

**Interfaces:**
- Consumes: the locked design — spec `docs/superpowers/specs/2026-07-18-nfty-ingredient-editor-design.md` §2–§6, and the brainstorm reference `editor-b-v5.html` (direction B: variants filmstrip ▸ tool strip + canvas ▸ live Colorize rail; corner preview blip; always-editing, no lock).
- Produces: a self-contained mockup consistent with the other three (verbatim token block, 1180px window, theme-aware).

- [ ] **Step 1: Create the mockup file**

Build `docs/design/mockups/ingredient-editor.html` following these rules (no `<!doctype>/<html>/<head>/<body>`; everything inline; the publish host wraps it):
- First line: `<title>nfty — Ingredient Editor</title>`.
- Copy the **entire token block verbatim** from `explorer.html` lines 4–48 (`:root`, the `prefers-color-scheme: dark` block, and both `:root[data-theme]` overrides). Do not alter a single hex value.
- Reuse the chrome idioms: `.stage` → `.pitch` → `.frame` → `.window` → `.note` scaffold; `.titlebar`/`.brandtile`/`.wordmark`/`.crumbs`/`.wc`; `.statusbar`; the theme toggle as a `.ghost` button in `.pitch` **outside** the `.window`; 1180px window width.
- Body layout (direction B): three panes inside the window — left **Variants** filmstrip (cards: thumbnail + name + **editable weight input** + rarity %; `+ Add variant`), center **tool strip + grayscale canvas** on a transparency checker (tools, left→right: brush · eraser │ rectangle · circle · triangle │ select-region · fill │ value ramp + swatch │ undo · redo, all inline SVG), right **Colorize** rail (segmented **Static | Dynamic** toggle; hue-range dual-slider + numeric ends; saturation-range dual-slider + numeric ends; locked **Value ← from grayscale**; **Quantize step** with °/% steppers and the derived colour count).
- Include the corner **preview blip** on the canvas (rounded colorized thumbnail + integrated overlay strip with reroll / enlarge / fill-pane SVG glyphs).
- No lock control anywhere; the breadcrumb carries an `editing value-map` state marker.

(The brainstorm file `.superpowers/brainstorm/*/content/editor-b-v5.html` is the reference for exact structure/markup; it is gitignored, so the committed artifact is authored fresh here from the spec + that reference.)

- [ ] **Step 2: Verify the token block matches explorer.html verbatim**

Run:
```bash
cd docs/design/mockups
for f in explorer ingredient-editor; do
  awk '/Vaporsoft brand tokens/{p=1} p&&/box-sizing: border-box/{exit} p{print}' $f.html \
    | grep -oiE '#[0-9a-f]{3,8}' | sort | uniq -c > /tmp/hex_$f.txt
done
diff /tmp/hex_explorer.txt /tmp/hex_ingredient-editor.txt && echo "TOKENS MATCH"
```
Expected: `TOKENS MATCH` (identical hex multiset in the token block; the editor may add no new hex literals).

- [ ] **Step 3: Screenshot both themes and eyeball**

Run:
```bash
F=docs/design/mockups/ingredient-editor.html
{ printf '<!doctype html><html><head><meta charset="utf-8"><style>*{box-sizing:border-box}html,body{margin:0}</style></head><body>'; cat $F; printf '</body></html>'; } > /tmp/ie-light.html
sed 's/<html>/<html data-theme="dark">/' /tmp/ie-light.html > /tmp/ie-dark.html
google-chrome --headless --disable-gpu --hide-scrollbars --window-size=1280,900 --screenshot=/tmp/ie-light.png /tmp/ie-light.html 2>/dev/null
google-chrome --headless --disable-gpu --hide-scrollbars --window-size=1280,900 --screenshot=/tmp/ie-dark.png /tmp/ie-dark.html 2>/dev/null
echo "light=$(stat -c%s /tmp/ie-light.png) dark=$(stat -c%s /tmp/ie-dark.png)"
```
Expected: two non-trivial PNGs. Open each and confirm: three panes render; Colorize controls read as interactive (toggle, range handles, steppers); the preview blip sits in the canvas corner; no lock control; both themes legible.

- [ ] **Step 4: Add the README section**

In `docs/design/mockups/README.md`, add an `## ingredient-editor.html` section mirroring the explorer/landing/help sections: one paragraph describing the editor (direction B, always-editing, live Colorize controls, preview blip), then a `Design spec:` link to `docs/superpowers/specs/2026-07-18-nfty-ingredient-editor-design.md`, and the note that its token block is copied verbatim from `explorer.html`.

- [ ] **Step 5: Commit**

```bash
git add docs/design/mockups/ingredient-editor.html docs/design/mockups/README.md
git commit -m "design(mockups): add the ingredient editor view mockup"
```

---

## Self-Review

**Spec coverage** (each spec section → task):
- §1 whole-ingredient / single flat raster / canvas-bound → `ValueMap` (T1), `IngredientDraft.AddVariant` sizes from canvas (T8).
- §2 layout B / §3 always-editing / §5 live Colorize / §6 preview blip → mockup (T14); the editor's *state* (no lock, live controls) is a UI concern shown in the mockup.
- §4 toolset → Brush/BrushStroke (T3), Erase (T4), Fill (T5), shapes (T6), select-move (T7), undo/redo (T2); modifier keys are deferred per spec (not built).
- §5 colorization config feeding kind/ranges → carried on `IngredientDraft.Kind`/`Colorization` (T8) and previewed (T10); quantize is data on `Colorization` (existing model), surfaced in the mockup (T14).
- §7 canvas inheritance → `ValueMap.ForCanvas` / `AddVariant` (T1, T8); standalone-size selection is a UI choice (mockup/T14), no library work.
- Library namespace + types table → T1–T11.
- Save/integration seam → exporter (T9) + `IngredientArchive.Write` round-trip (T9) + `CookBookEdits` splice (T11); standalone `.igt` target is the exporter feeding the existing archive.
- Grayscale-by-construction (`ValueMap`) + optional Validator hardening → T1 + T12.
- Undo=commands, buffer backing → T1, T2.
- Testing bullets (exact-pixel ops, grayscale, reversible round-trips, exporter round-trip, preview matches cook path) → T1–T10 tests.
- Deferred (custom mode, modifier keys, entry-point wiring, Avalonia) → out of scope, not tasked.

**Placeholder scan:** every code step contains complete code; the only intentional "adapt to existing helpers" note is Task 12, which modifies an existing file whose private helper names must be matched at edit time — the added code and its insertion point are fully specified.

**Type consistency:** `ValueMap.Set(x,y,value,alpha)`, `RegionEditCommand.ComputePixels`, `Brush(Size,Value)`, `PixelRect(X,Y,Width,Height)`, `ShapeKind`, `IngredientDraft`/`VariantDraft`, `IngredientDraftExporter.Export`, `ColorizedPreview.Render`, `CookBookEdits.UpsertIngredient` are used with identical signatures across tasks. Existing APIs referenced (`IngredientManifest`, `Variant`, `IngredientArchive.Write/Read`, `Colorizer.Apply`, `ColorRoller.Roll`, `ColorConvert`, `ColorSpec.Parse`, `IRng`/`SplitMix64Rng`, `RecipeManifest`/`LoadedRecipe`/`LoadedCookBook`) match the current source.
