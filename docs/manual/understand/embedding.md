# Three ways to use nfty

nfty is one engine with three front doors: the **app**, the **command line**, and the **libraries**.
They are not three products. The app and the command line are thin layers over the same library, so
a collection cooked from either is byte-for-byte what your own code would cook from the same book and
seed.

| Door | For | Reach it by |
|---|---|---|
| The app | Drawing, tuning weights, looking at what you made | The desktop program |
| The command line | Scripts, CI, anything without a screen | `nfty <command>`, see [Command line](../reference/cli.md) |
| The libraries | A game, a server, a tool of your own | `Nfty.Ticket`, `Nfty.Core`, `Nfty.Model`, see [Embed nfty in your program](../how-to/embed-nfty.md) |

Everything the app does, the command line does too, and what the libraries expose is the
command line's whole surface, plus the pieces it is built from.

## Why the roll is its own assembly

Deciding **what an asset is** and **drawing it** are different jobs with different costs. Deciding
needs a few hundred bytes and some arithmetic. Drawing needs every image decoded and a compositor.

A game server that hands out drops only does the first. So the roll lives alone in `Nfty.Ticket`:
no NuGet packages, no imaging library, nothing it could not take to a phone or a game engine. Its
output is a **Ticket**: the recipe, one variant per layer, each colored layer's rolled hue and
saturation, and the DNA. It names a selection. It is not pixels, and no pixel is needed to produce it.

`Nfty.Core` references `Nfty.Ticket`, never the reverse, and its `Generator` calls the very same
`Ticketer` for every roll, then renders. There is exactly one definition of the order in which an
asset's layers consume the random stream. Two copies is how "the server and the client agree" stops
being true, and nothing would show it until a player saw a different asset from the one they were
sold.

`Nfty.Model` (the Rise engine) sits above `Nfty.Core`, and `Nfty.Core` has no reference to it. The
arrows only point one way, and a test checks.

## The Menu is the book, flattened

A `.cbk` is an authoring file: it holds everything an artist edits. Rolling needs only a subset:
weights, rules, colorization, canvas, art. A **Menu** (`.mnu`) is that subset, written by
`nfty menu`, so a runtime host needs no `.cbk` and nothing that exists only for editing.

A Menu's art comes either as one **atlas** image with a rectangle per variant, or **loose**, one PNG
each. The reader hands back the bytes and the rectangle; in loose mode the rectangle is the whole
image, so the code that crops is the same for both.

## Why a ticket is not a picture

Because the picture can always be recomputed from it, and the other way round is not true. A server
that stores tickets stores the collection in a few hundred bytes an asset. The client turns a ticket
into pixels with the same Menu, through the same colorizer, and gets identical output.

That is also why the DNA is **recomputed, not trusted**, when a ticket is read: the wire text carries
the DNA, and `AssetTicket.Parse` rejects a ticket whose contents do not produce it. Some fields (the
layer kind, the color model, the quantize steps) are not in the hash, because the book fixes them for
a given layer. They are checked against the Menu at plating instead: the ticket says what was
*rolled*; the Menu says what the layer *is*.

## Who owns an image

You do. `Nfty.Core` hands back live `Image<Rgba32>` objects and does not track them. Reading an
archive decodes every variant PNG, so the loaded book owns images; a generated set owns the asset
images; each is `IDisposable`, and disposing the parent disposes the children. `using` them. The
GUI does the same, and it is the single most common mistake in embedded use to forget it on a path
that runs many times.

## What stays the same everywhere

- **Determinism.** One seed string becomes a SplitMix64 stream via the seed's SHA-256 read
  little-endian. Same book and same seed give the same output on any locale and any CPU.
- **Ordinal sorting.** Anything that sorts and reaches output uses ordinal comparison.
- **Thread count never changes output.** It changes only speed. Rolling is sequential by nature
  (each asset depends on every earlier one through the dedup set); rendering and archive work fan out.
- **Old formats are refused by name.** An archive newer than the build, or older than the floor,
  throws `UnsupportedSchemaVersionException` rather than being misread.

## Where to go next

- [Embed nfty in your program](../how-to/embed-nfty.md), with code that compiles
- [API reference](../reference/api/index.md)
- [Why a seed reproduces a collection](seeds.md)
