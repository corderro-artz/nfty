# Embed nfty in your program

Use nfty from your own code: a game server that decides what a drop contains, an engine that draws
it, a tool that cooks collections. There are three libraries. Pick the smallest one that does the
job; [Three ways to use nfty](../understand/embedding.md) explains the split.

| You want to | Reference | Gets you |
|---|---|---|
| Decide what an asset is, on a server or in an engine, and draw it yourself | `Nfty.Ticket` | The roll: `.mnu` reading, rolling, tickets, plating. No packages. |
| Cook collections, read `.cbk`, render pixels, write Sets, export | `Nfty.Core` | Everything above, plus archives, validation, generation, rendering, Sets, sealing. Brings ImageSharp. |
| Make 3D models | `Nfty.Model` | Voxel meshes and glTF. Brings `Nfty.Core`. |

The libraries are not on NuGet. Reference the projects (`ProjectReference`) or build them and
reference the DLLs. Both samples below are in the repository under `samples/`, build with the
solution, and are the source of every code block on this page, so what you copy is what compiles.

## Get a Menu

A [Menu](../reference/glossary.md) (`.mnu`) is a CookBook flattened for runtime: weights, rules,
colorization and art, no `.cbk` needed. Write one with the command line:

```bash
nfty demo ./demo
nfty menu ./demo/ChestDemo.cbk --out ./demo/chest.mnu
```

or from code with `MenuWriter.Write` (needs `Nfty.Core`, see [Cook from code](#cook-from-code)).

## Roll and draw with Nfty.Ticket alone

`samples/Nfty.Ticket.Sample/Nfty.Ticket.Sample.csproj` has exactly one reference, to
`Nfty.Ticket`. Building it is the proof that the roll needs nothing else.

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\Nfty.Ticket\Nfty.Ticket.csproj" />
</ItemGroup>
```

These namespaces come from `Nfty.Ticket.dll`. Some begin with `Nfty.Core` because those types began in
the engine and kept their names when the roll moved out:

```csharp
--8<-- "samples/Nfty.Ticket.Sample/Program.cs:usings"
```

### Open the Menu and prepare it

`MenuArchive.Open` reads the manifest and decodes no image. `Ticketer.Prepare` resolves the draw
order and weight totals once. Hold on to the result and reuse it for every roll; it is the
expensive part, and it is read-only, so several threads may roll from one.

```csharp
--8<-- "samples/Nfty.Ticket.Sample/Program.cs:open"
```

### Roll

```csharp
--8<-- "samples/Nfty.Ticket.Sample/Program.cs:roll"
```

- The seed goes through `SeedHash.ToUlong` into a `SplitMix64Rng`. The same Menu and seed give the
  same tickets, in the same order, on any machine and CPU.
- `TryRoll` returns `null` when the Recipe's incompatibility rules reject the roll. That is not an
  error: roll again from the same generator. Bound your loop, as above. `Generator` allows
  10,000 attempts per asset by default.
- One generator is one stream. Do not share an `IRng` between threads; give each worker its own.
- `AssetTicket.ToWire()` is one line of text, a few hundred bytes: the recipe, one variant per
  present layer, each colored layer's rolled hue and saturation, and the DNA. Store it, send it,
  put it in a token's metadata. It names a selection; it holds no pixels.

### Draw a ticket

```csharp
--8<-- "samples/Nfty.Ticket.Sample/Program.cs:plate"
```

- `AssetTicket.Parse` recomputes the DNA from the contents and throws `FormatException` if it does not
  match, or if the wire is another version.
- `Plating.Plate` returns the `Draw` list bottom-first, in the book's `layerOrder`. A layer left out
  by its absent chance is simply not in the list. It takes each layer's kind and color model from
  the **Menu**, never from the ticket, and refuses a ticket whose quantize steps differ from the
  Menu's, so a tampered ticket cannot keep its identity while drawing something else.
- `MenuArchive.ArtBytes(key)` gives PNG bytes. In atlas mode that is the whole atlas, and
  `draw.Source` is the rectangle to crop; in loose mode the rectangle is the whole image. One
  cropping routine serves both. Decoding is yours: use whatever PNG library your engine has.
- **Custom** layers composite as they are. **Dynamic** and **Static** layers are gray value-maps:
  each pixel becomes `HsvToRgb(hue, sat, gray / 255)` with its alpha untouched (or `HslToRgb` when
  `draw.Model` says HSL). `ColorConvert` in `Nfty.Ticket` has both conversions.
- Depth for 3D comes from `MenuArchive.Depth(key)`, which is `null` for a variant that has none.

## Cook from code

`samples/Nfty.Embedding.Sample` references `Nfty.Core` and `Nfty.Model`, and shows the three things
you would do with the full engine.

```csharp
--8<-- "samples/Nfty.Embedding.Sample/Program.cs:usings"
```

### Read a book and generate

```csharp
--8<-- "samples/Nfty.Embedding.Sample/Program.cs:cook"
```

**Callers own images.** Reading a `.cbk` decodes every variant PNG into memory, so `LoadedCookBook`
(and the recipes and ingredients inside it) is `IDisposable`: `using var` frees the whole tree.
`GeneratedSet` and `GeneratedAsset` are disposable too, and disposing a set disposes every asset
image. `Generator.Generate` holds the whole collection. For a collection too big for memory, use
`Generator.GenerateStreaming`, which yields one asset at a time and hands you its ownership.
Abandoning the enumeration leaves the last asset undisposed.

`Generate` validates the book itself and throws on any problem, with a message listing every one, so
nothing needs pre-checking. It rolls sequentially and renders in parallel; pass
`GenerateOptions.Parallelism` to choose the width. The output is byte-identical at any width. Async
twins exist (`GenerateAsync`, `CookBookArchive.ReadAsync`, `SetWriter.WriteAsync`) and report
progress and honor cancellation. `SetWriter.Write(set, dir, pack)` writes the Set to disk.

### Render a ticket to pixels

```csharp
--8<-- "samples/Nfty.Embedding.Sample/Program.cs:render"
```

`MenuRenderer.Render` uses the same colorizer and compositor `generate` uses, so a client rendering a
server's ticket gets identical pixels. To go the other way, from a `.cbk` to a `.mnu`:
`MenuWriter.Write(book, path, new MenuWriteOptions { Art = MenuArtMode.Atlas })`. It runs the
validator first and throws, writing nothing, if the book has problems; an atlas past the pixel
ceiling throws `AtlasTooLargeException`, and `MenuArtMode.Loose` avoids it.

### Make a 3D model

```csharp
--8<-- "samples/Nfty.Embedding.Sample/Program.cs:rise"
```

- `Riser.TryBuild` makes the mesh from **finished** art plus an optional `DepthMap`. It returns
  `null` when nothing is fully opaque, and throws `PartialAlphaException` naming the picture when a
  pixel is partly transparent: a voxel is either there or not.
- `StackDepth.ForAsset` is the rule for a layered asset: per pixel the front is the largest front
  over the layers, the back the largest back. Pass the `Faces` it returns so the sides take the
  right layer's color. For one flat picture, leave both `null`.
- `ModelWriter.Write` returns `ModelFile`s (name and bytes) and writes nothing; you choose where
  they go. glTF with a `.bin` returns two files.
- To model a whole Set as you write it, give `SetWriter.Write` an `IAssetModeler`;
  `Nfty.Model.SetModeler` is the implementation the command line uses. Core knows no model library.
- `GltfReader` reads back nfty's own glTF subset only, and reports anything else as
  `UnsupportedModelException`.

## Run the samples

```bash
dotnet run --project samples/Nfty.Ticket.Sample -- ./demo/chest.mnu player-42
dotnet run --project samples/Nfty.Embedding.Sample -- ./demo/ChestDemo.cbk ./demo/chest.mnu ./out
```

The first prints the seed's ticket in wire form and each layer's draw. The second writes three PNGs,
the rendered ticket, and one `.glb` into `./out`.

## Things that will surprise you

- **Same seed, same book, same bytes.** That holds across machines and CPU architectures. It breaks
  if the book changes: reordering layers moves which RNG draw reaches each layer, so the same seed
  gives a different collection. See [Why a seed reproduces a collection](../understand/seeds.md).
- **A sealed `.tin` cannot be opened without its passphrase, and `SetExporter` refuses to export from
  one.** The seal is a policy marker backed by real encryption, not a wall against someone who holds
  the passphrase: see [What sealing does and does not do](../understand/sealing.md).
- **Old archives are refused, not misread.** An archive written by a newer build than yours throws
  `UnsupportedSchemaVersionException`, and so does the retired v1.
- **Sort ordinally.** If you add code that sorts anything reaching an output, use
  `StringComparer.Ordinal`. The default sorts by culture and silently breaks determinism.

## See also

- [API reference](../reference/api/index.md): every public type, with signatures
- [Ticket and Menu API](../reference/ticket-api.md): the roll at a glance
- [Roll assets at runtime from a Menu](roll-assets-at-runtime.md): the command-line route
- [Three ways to use nfty](../understand/embedding.md)
