# Roll and draw with Nfty.Ticket

`Nfty.Ticket` decides what an asset is: it reads a Menu, rolls tickets, and tells you what to draw.
It has no dependencies at all, so a server or game engine takes `Nfty.Ticket.dll` and nothing else.
It ships for **.NET 10** and **.NET Standard 2.1**, so an engine that loads managed plugins can take
it as is. Both builds run the same code and roll the same assets from the same seed.
Everything on this page comes from `samples/Nfty.Ticket.Sample`, which references `Nfty.Ticket` only:
building it is the proof that the roll needs nothing more.

**Before you start:** have a Menu. With the demo:

```bash
nfty demo ./demo
nfty menu ./demo/ChestDemo.cbk --out ./demo/chest.mnu
```

## Reference the library

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\Nfty.Ticket\Nfty.Ticket.csproj" />
</ItemGroup>
```

Some namespaces begin with `Nfty.Core` because those types began in the engine and kept their names
when the roll moved out; they still ship in `Nfty.Ticket.dll`:

```csharp
--8<-- "samples/Nfty.Ticket.Sample/Program.cs:usings"
```

## Open and prepare the Menu

`MenuArchive.Open` reads the manifest and decodes no image. `Ticketer.Prepare` resolves the draw order
and weight totals once. Keep the result and reuse it for every roll; it is read-only, so several
threads may roll from one.

```csharp
--8<-- "samples/Nfty.Ticket.Sample/Program.cs:open"
```

## Roll

```csharp
--8<-- "samples/Nfty.Ticket.Sample/Program.cs:roll"
```

- The seed goes through `SeedHash.ToUlong` into a `SplitMix64Rng`. The same Menu and seed give the same
  tickets, in the same order, on any machine.
- `TryRoll` returns `null` when the Recipe's rules reject the roll. That is not an error: roll again
  from the same generator, with a bound. nfty's own generator allows 10,000 attempts per asset.
- One generator is one stream. Give each worker thread its own `IRng`.
- `ToWire()` is one line of text, a few hundred bytes. Store it, send it, put it in a token's
  metadata.

## Draw a ticket

```csharp
--8<-- "samples/Nfty.Ticket.Sample/Program.cs:plate"
```

- `AssetTicket.Parse` recomputes the DNA and throws `FormatException` if it does not match, or if the
  wire is another version.
- `Plating.Plate` returns the `Draw` list bottom first, in the Recipe's layer order. A layer left out by
  its absent chance is not in the list. It takes each layer's kind and color model from the **Menu**,
  and refuses a ticket whose quantize steps differ from the Menu's.
- `MenuArchive.ArtBytes(key)` gives PNG bytes. In atlas mode that is the whole atlas and `draw.Source`
  is the rectangle to crop; in loose mode the rectangle is the whole image, so one cropping routine
  serves both. Decoding is yours: use whatever PNG library your engine has.
- **Custom** layers composite as they are. **Dynamic** and **Static** layers are gray value-maps: each
  pixel becomes `HsvToRgb(hue, sat, gray / 255)` with alpha untouched (`HslToRgb` when `draw.Model`
  says HSL). `ColorConvert` has both.
- `MenuArchive.Depth(key)` gives a variant's depth for 3D, or `null`.

## Run the sample

```bash
dotnet run --project samples/Nfty.Ticket.Sample -- ./demo/chest.mnu player-42
```

It prints the seed's ticket in wire form and each layer's draw.

## See also

- [Ticket and Menu API](../reference/ticket-api.md) and the
  [Nfty.Ticket API pages](../reference/api/nfty-ticket/index.md)
- [Cook and render with Nfty.Core](core.md), to skip writing your own renderer
