# API reference generator

Writes the pages under `docs/manual/reference/api/` (one per public type of `Nfty.Ticket`,
`Nfty.Core` and `Nfty.Model`, plus an index per assembly) from the libraries' own XML documentation.

```bash
python tools/api-docs/build.py            # rebuild the libraries, regenerate the pages
python tools/api-docs/build.py --check    # regenerate to memory, diff against the committed pages, exit 1 on drift
python tools/api-docs/build.py --surface saved.json   # reuse a saved dump of the surface (skips dotnet)
```

Needs the .NET SDK in `global.json` and Python 3 (standard library only). The **output is
committed**: the manual is built by the site repository, which must not need a .NET SDK. CI runs
`--check`, so a `///` comment edited without regenerating fails the build.

## Two inputs, joined on the XML documentation id

| Input | Supplies | Why not just the XML |
|---|---|---|
| `src/<Assembly>/bin/Release/net10.0/<Assembly>.xml` | All the prose: summary, remarks, params, returns, exceptions, examples, `see cref` links | |
| `surface/` (a small console project, not in `nfty.sln`) | Which members are public, their C# declarations, parameter names and types, nullability, default values | The compiler's XML lists **every documented member, internal and private included**, and has no return types, modifiers or nullability. |

`surface/` reads the built assemblies with reflection and prints JSON; `build.py` is then plain
text processing. `<inheritdoc/>` is resolved by `build.py` (the compiler leaves it as written): from
its `cref` when given, otherwise from the base or interface member the surface tool reports.

## Properties of the output

- **Deterministic.** Ordinal sorts, LF endings, no timestamps, no versions. `.gitattributes` pins the
  folder to LF so a Windows checkout does not break `--check`.
- **Links resolve.** A `cref` to a documented public symbol becomes a link to its page and member
  anchor; anything else (framework types, internal members) is plain code.
- **Page names** are the full type name with generic ticks made file-safe, for example
  `Nfty.Ticket.Ticketer.md` and `Nfty.Core.Editing.EditHistory-1.md`. Namespaces are not assemblies:
  `Nfty.Ticket.dll` declares types in `Nfty.Core.*` namespaces too, so pages are grouped by assembly.

## Changing it

Edit `build.py` (prose and layout) or `surface/Program.cs` (signatures), regenerate, and commit the
pages with the change. Never edit a generated page by hand: the next run overwrites it, and `--check`
fails until you do.
