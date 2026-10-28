# Ticket and Menu API

The `Nfty.Ticket` assembly: `net10.0`, no NuGet packages, no imaging library. Types live in the
`Nfty.Ticket` namespace unless noted. For a worked example see
[Roll assets at runtime](../how-to/roll-assets-at-runtime.md).

| Member | Does |
|---|---|
| `MenuArchive.Open(string path)` / `Open(Stream)` | Opens a `.mnu` without decoding any image. Disposable. |
| `MenuArchive.Book` | The `MenuBook`: recipes, layers, weights, rules, colorization, canvas and `Art`. |
| `MenuArchive.ArtBytes(cellKey)` | Raw PNG bytes. Atlas mode: the whole atlas. Loose mode: that one variant's PNG. |
| `MenuArchive.Cell(cellKey)` | The `MenuCell` rect (`X`, `Y`, `Width`, `Height`) inside those bytes. The whole image in loose mode. |
| `MenuArchive.Depth(cellKey)` | The variant's `DepthMap`, or `null` when it has none. |
| `MenuArchive.Layer(recipeId, ingredientId)` | One layer's definition. |
| `MenuBook.CellKey(recipeId, ingredientId, variantId)` | The key for a cell. |
| `Ticketer.Prepare(MenuBook)` | Resolves draw order and weight totals once into a `PreparedMenu`. Reuse it. |
| `Ticketer.TryRoll(PreparedMenu, IRng)` | Rolls one asset. Returns `null` when the rules reject the selection. |
| `AssetTicket` | `Dna`, `RecipeId` and `Layers`. Each `TicketLayer` carries ids, the rolled `Hue` and `Sat`, `Model`, `Kind` and the quantize steps. |
| `AssetTicket.ToWire()` / `Parse(string)` | The text form, version `v1`. `Parse` throws `FormatException` for another version or a DNA that does not match the contents. |
| `Plating.Plate(MenuArchive, AssetTicket)` | Returns the `Draw` list. Throws `KeyNotFoundException` or `FormatException` for a ticket that does not belong to this Menu. |
| `SplitMix64Rng(ulong)`, `SeedHash.ToUlong(string)` | The generator and seed hash, in the `Nfty.Core.Generation` namespace. |

`Nfty.Core.Publish.MenuRenderer.Render(menu, ticket)` renders a ticket to an `Image<Rgba32>` and needs
`Nfty.Core`. `MenuWriter`, also in `Nfty.Core`, writes a `.mnu` from a loaded CookBook.

A ticket names a selection, not pixels. Quantized color is part of the DNA, so two tickets whose
colors fall in the same bucket have the same DNA.
