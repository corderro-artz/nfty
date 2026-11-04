# Cook and render with Nfty.Core

`Nfty.Core` is the whole engine: reading and writing archives, validation, generation, rendering,
Sets, export, sealing and statistics. The code on this page comes from `samples/Nfty.Embedding.Sample`,
which references `Nfty.Core` and `Nfty.Model`.

```csharp
--8<-- "samples/Nfty.Embedding.Sample/Program.cs:usings"
```

## Read a book and generate

```csharp
--8<-- "samples/Nfty.Embedding.Sample/Program.cs:cook"
```

**You own the images.** Reading a `.cbk` decodes every variant PNG, so `LoadedCookBook` (and the
Recipes and Ingredients inside it) is `IDisposable`, and `using var` frees the whole tree.
`GeneratedSet` and `GeneratedAsset` are disposable too; disposing the set disposes every asset image.

- `Generator.Generate` validates the book itself and throws on any problem, with a message listing
  every one, so there is nothing to pre-check. `Validator.Validate` reports without throwing when you
  want to show problems yourself.
- It holds the whole collection in memory. For a collection too big for that, use
  `Generator.GenerateStreaming`, which yields one asset at a time and hands you its ownership
  (abandoning the enumeration leaves the last one undisposed).
- It rolls sequentially and renders in parallel; `GenerateOptions.Parallelism` sets the width. Output
  is byte-identical at any width.
- Async twins (`GenerateAsync`, `CookBookArchive.ReadAsync`, `SetWriter.WriteAsync`) report progress and
  honor cancellation.

`SetWriter.Write(set, dir, pack)` writes the Set to disk exactly as `nfty generate` does; give it an
`IAssetModeler` (`Nfty.Model.SetModeler`) to write `models/` too.

## Render a ticket

```csharp
--8<-- "samples/Nfty.Embedding.Sample/Program.cs:render"
```

`MenuRenderer.Render` uses the same colorizer and compositor as `generate`, so a client rendering a
server's ticket gets identical pixels. You dispose the returned image.

## Write a Menu

```csharp
MenuWriter.Write(book, "book.mnu", new MenuWriteOptions { Art = MenuArtMode.Atlas });
```

It validates first and writes nothing for a book with problems. An atlas past the pixel ceiling throws
`AtlasTooLargeException`; `MenuArtMode.Loose` avoids it.

## Export and seal

`SetExporter.Plan(...)` computes every file an export would write without writing it, and
`SetExporter.Export(...)` writes exactly that list; `ExportPlan.Parts()` renders the same summary the
CLI and app print. `ExportOptions` and `ExportPreset` carry the choices from
[Export a Set](../publishing/export.md). Exporting from a sealed `.tin` is refused inside the library.

## Run the sample

```bash
dotnet run --project samples/Nfty.Embedding.Sample -- ./demo/ChestDemo.cbk ./demo/chest.mnu ./out
```

It writes three cooked PNGs, the rendered ticket, and one `.glb` into `./out`.

## See also

- [Make models with Nfty.Model](models.md)
- [Nfty.Core API pages](../reference/api/nfty-core/index.md)
