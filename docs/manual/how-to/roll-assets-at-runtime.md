# Roll assets at runtime from a Menu

Roll a new asset when a player or buyer asks for one, instead of cooking a whole Set ahead of time.
A **Menu** (`.mnu`) is your CookBook flattened for this: every weight, rule, colorization and all the
art, with no `.cbk` needed. A **Ticket** is one rolled outcome: a few hundred bytes naming the recipe,
one variant per layer, each colored layer's rolled hue and saturation, and the DNA.

## Make the Menu

```bash
nfty menu mybook.cbk --out mybook.mnu
nfty menu mybook.cbk --out mybook.mnu --art loose
```

`--art atlas` (the default) packs all the art into one image. `--art loose` keeps one PNG per variant,
for a book whose art would not fit one atlas. `nfty inspect mybook.mnu` lists what is inside.

## Try it from the command line

```bash
nfty serve mybook.mnu --seed player-42 --out asset.png
nfty serve mybook.mnu --ticket "<wire text>" --out asset.png
```

Pass exactly one of `--seed` (roll a new asset) or `--ticket` (render one already rolled). Quote the
ticket: its wire form contains `|`, which a shell reads as a pipe. `--max-rerolls` applies to `--seed`
only and is refused with `--ticket`. The command prints the DNA and recipe it used.

## Roll and render in your own program

Reference `Nfty.Ticket` to roll. It is a plain `net10.0` library with no packages, so a server that
only decides what a drop contains takes `Nfty.Ticket.dll` and nothing else.

```csharp
using Nfty.Core.Generation;
using Nfty.Ticket;

using var menu = MenuArchive.Open("mybook.mnu");
PreparedMenu prepared = Ticketer.Prepare(menu.Book);        // do this once, reuse it

var rng = new SplitMix64Rng(SeedHash.ToUlong("player-42"));
AssetTicket? ticket = null;
for (int attempt = 0; attempt < 1000 && ticket is null; attempt++)
    ticket = Ticketer.TryRoll(prepared, rng);               // null = a rule rejected this roll
if (ticket is null) throw new InvalidOperationException("Rules rejected every roll.");

string wire = ticket.ToWire();                              // store or send this
```

`TryRoll` returns `null` when the recipe's incompatibility rules reject the roll; roll again from the
same generator. The same Menu and seed give the same ticket on any machine.

Later, anywhere that has the Menu, read the ticket back and work out what to draw:

```csharp
AssetTicket back = AssetTicket.Parse(wire);                 // checks the DNA against the contents

foreach (Draw draw in Plating.Plate(menu, back))            // in the ticket's layer order
{
    byte[] png = menu.ArtBytes(draw.Source.Key);            // atlas mode: the whole atlas PNG
    // Crop (draw.Source.X, .Y, .Width, .Height) out of it, then:
    //   Custom          -> draw as is
    //   Dynamic/Static  -> colorize with draw.Hue and draw.Sat in draw.Model
}
```

`Plating.Plate` takes each layer's kind and color model from the Menu, never from the ticket, and
throws `FormatException` for a ticket that was not rolled against this Menu. In loose mode the
"rect" is the whole image, so the same cropping code works in both modes.

If you can reference `Nfty.Core`, skip the drawing step: `MenuRenderer.Render(menu, back)` in
`Nfty.Core.Publish` returns the finished `Image<Rgba32>` (you dispose it), through the same colorizer
and compositor `generate` uses. A client that renders a server's ticket gets identical pixels.

## See also

- [Ticket and Menu API](../reference/ticket-api.md)
- [Command line](../reference/cli.md)
