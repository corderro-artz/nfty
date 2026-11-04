# Make models with Nfty.Model

`Nfty.Model` turns finished pixel art into a voxel mesh and writes glTF, with no glTF library. It
references `Nfty.Core`; nothing references it back. The code below comes from
`samples/Nfty.Embedding.Sample` and rises the first asset cooked on the
[previous page](core.md#read-a-book-and-generate).

```csharp
--8<-- "samples/Nfty.Embedding.Sample/Program.cs:rise"
```

- `Riser.TryBuild` makes the mesh from **finished** art plus an optional `DepthMap`. It returns `null`
  when nothing is fully opaque, and throws `PartialAlphaException` naming the picture when a pixel is
  partly transparent: a voxel is there or it is not.
- `StackDepth.ForAsset` is the rule for a layered asset: per pixel, the front is the largest front over
  the layers and the back the largest back. Pass the `Faces` it returns so each face and side takes the
  right layer's color. For one flat picture, leave both `null`.
- `ModelWriter.Write` returns `ModelFile`s (name and bytes) and writes nothing; you choose where they
  go. glTF with a `.bin` returns two files. `RiseOptions` sets format, pivot and voxel size.
- To model a whole Set as it is written, give `SetWriter.Write` a `Nfty.Model.SetModeler`.
- `GltfReader` reads back nfty's own glTF subset only and reports anything else as
  `UnsupportedModelException`.

For what front, back and paper mean, see [Depth: paper, front and back](../concepts/depth.md). Every
type is in the [Nfty.Model API pages](../reference/api/nfty-model/index.md).
