# Embedding nfty

nfty is one engine with three front doors: the **app**, the **command line**, and the **libraries**.
The app and the command line are thin layers over the same libraries, so a collection cooked from
either is byte-for-byte what your own code cooks from the same book and seed. This section shows how to
use the libraries from a game, a server or a tool of your own.

## Choose a library

Pick the smallest one that does the job.

| You want to | Reference | Brings |
|---|---|---|
| Decide what an asset is (on a server, in an engine) and draw it yourself | `Nfty.Ticket` | Nothing else. `net10.0`, **no NuGet packages**, no imaging library. |
| Read `.cbk` files, cook collections, render pixels, write Sets, export, write Menus | `Nfty.Core` | `Nfty.Ticket` and ImageSharp. |
| Make 3D models (voxel meshes, glTF) | `Nfty.Model` | `Nfty.Core`. No glTF library. |

| Page | Covers |
|---|---|
| [Menus and tickets](menus-and-tickets.md) | The runtime workflow: write a `.mnu`, roll tickets, render them, from the CLI first. |
| [Roll and draw with Nfty.Ticket](ticket.md) | The dependency-free roll, with a compiled sample. |
| [Cook and render with Nfty.Core](core.md) | Reading books, generating, writing Sets, rendering tickets. |
| [Make models with Nfty.Model](models.md) | Rising finished art into glTF. |
| [Ticket and Menu API](../reference/ticket-api.md) | The roll's types at a glance. |
| [API reference](../reference/api/index.md) | Every public type, generated from the source. |

## Getting the libraries

The libraries are not on NuGet. Clone the [repository](https://github.com/corderro-artz/nfty) and
either reference the projects:

```xml
<ItemGroup>
  <ProjectReference Include="..\nfty\src\Nfty.Ticket\Nfty.Ticket.csproj" />
</ItemGroup>
```

or build them (`dotnet build -c Release`) and reference the DLLs. Both samples quoted in this section
live under `samples/`, build with the solution, and are run against the demo Menu by CI, so the code
you copy is code that compiles.

## Why the roll is its own assembly

Deciding **what an asset is** and **drawing it** are different jobs. Deciding needs a few hundred bytes
and some arithmetic; drawing needs every image decoded and a compositor. A server that hands out drops
does only the first, so the roll lives alone in `Nfty.Ticket`. Its output is a **Ticket**: the Recipe,
one variant per layer, each colored layer's rolled hue and saturation, and the DNA.

`Nfty.Core`'s `Generator` calls the very same `Ticketer` for every roll and then renders, so there is
exactly one definition of the order in which an asset's layers consume random numbers. Server and
client cannot disagree about what was rolled. `Nfty.Model` sits above `Nfty.Core`; the arrows only point
one way.

## Rules that hold everywhere

- **Determinism.** A seed string becomes a SplitMix64 stream through its SHA-256 (read little-endian).
  Same book and seed give the same output on any locale and CPU.
- **Thread count never changes output.** Rolling is sequential by nature; rendering and archive work
  fan out. `Parallelism` options change speed only.
- **You own images.** `Nfty.Core` hands back live `Image<Rgba32>` objects; the types that hold them are
  `IDisposable`. `using` them.
- **Old and new formats are refused by name.** An archive newer than the library, or older than schema
  2, throws `UnsupportedSchemaVersionException` rather than being misread.
- **Sort ordinally.** Anything you sort that reaches output should use `StringComparer.Ordinal`; the
  default sorts by culture and breaks determinism.
