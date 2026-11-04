# Menus and tickets

Roll a new asset when a player or buyer asks for one, instead of cooking a whole Set ahead of time.
A **Menu** (`.mnu`) is your CookBook flattened for this: every weight, rule and colorization, and all
the art, with no `.cbk` needed. A **Ticket** is one rolled outcome in a few hundred bytes.

The typical split:

```text
server:  Menu ──Ticketer.TryRoll──▶ ticket ──store / send──▶
client:  Menu + ticket ──Plating.Plate / MenuRenderer.Render──▶ pixels
```

Both sides need only the `.mnu`. The same Menu and seed give the same ticket on any machine, and a
ticket renders to the same pixels `nfty generate` would.

## 1. Write the Menu

=== "Command line"

    ```bash
    nfty menu book.cbk --out book.mnu                # art packed into one atlas
    nfty menu book.cbk --out book.mnu --art loose    # one PNG per variant
    nfty inspect book.mnu
    ```

=== "App"

    Select the CookBook in the Explorer and press **Export Menu…**. Choose **Atlas** or **Loose** and
    press **Export**. The book is read, never changed.

**Atlas** (the default) stitches every distinct variant image into one PNG, identical art packed once.
**Loose** writes one PNG per variant: use it when a book's art would not fit one atlas (an atlas past
the pixel ceiling is refused). The book is validated first; a broken book writes nothing.

## 2. Try it from the command line

```bash
nfty serve book.mnu --seed player-42 --out asset.png
nfty serve book.mnu --ticket "<wire text>" --out asset.png
```

Pass exactly one of `--seed` (roll a new asset; the same roll `generate` makes for the first asset of a
run with that seed) or `--ticket` (render one already rolled). Quote the ticket: its wire form contains
`|`, which a shell reads as a pipe. `--max-rerolls` goes with `--seed` only. The command prints the DNA
and Recipe it used.

## 3. Roll in your program

Reference `Nfty.Ticket` alone:

```csharp
using Nfty.Core.Generation;
using Nfty.Ticket;

using var menu = MenuArchive.Open("book.mnu");
PreparedMenu prepared = Ticketer.Prepare(menu.Book);   // once; reuse for every roll

var rng = new SplitMix64Rng(SeedHash.ToUlong("player-42"));
AssetTicket? ticket = null;
for (int i = 0; i < 1000 && ticket is null; i++)
    ticket = Ticketer.TryRoll(prepared, rng);          // null = a rule rejected this roll
string wire = ticket!.ToWire();                         // store or send this
```

## 4. Draw it

Anywhere that has the Menu:

- **Your own renderer** (`Nfty.Ticket` only): `AssetTicket.Parse(wire)`, then `Plating.Plate(menu,
  ticket)` gives the layers to draw, bottom first, with the rectangle to crop from the art and the
  color for each. See [Roll and draw with Nfty.Ticket](ticket.md#draw-a-ticket).
- **nfty's renderer** (`Nfty.Core`): `MenuRenderer.Render(menu, ticket)` returns the finished
  `Image<Rgba32>`. See [Render a ticket](core.md#render-a-ticket).

## What a ticket guarantees

- `AssetTicket.Parse` recomputes the DNA from the contents and throws `FormatException` if it does not
  match, so a ticket cannot be edited and keep its identity.
- Each layer's kind, color model and quantize steps come from the **Menu**, never the ticket.
  `Plating.Plate` refuses a ticket that was not rolled against this Menu.
- A ticket names a selection, not pixels. Colors in one quantize bucket give the same DNA.

## See also

- [Ticket and Menu API](../reference/ticket-api.md)
- [`menu`](../reference/cli.md#menu) and [`serve`](../reference/cli.md#serve) in the command reference
