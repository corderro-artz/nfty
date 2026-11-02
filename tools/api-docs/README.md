# API reference generator

Writes the pages under `docs/manual/reference/api/` (one per public type of `Nfty.Ticket`,
`Nfty.Core` and `Nfty.Model`, plus an index per assembly) from the libraries' own XML documentation.

```bash
python tools/api-docs/build.py            # rebuild the libraries, regenerate the pages
python tools/api-docs/build.py --check    # regenerate to memory, diff against the committed pages, exit 1 on drift
python tools/api-docs/build.py --lint     # check the documentation itself, exit 1 listing every offender
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

## What the documentation must say

Two layers enforce it, because neither can see all of it.

- **The compiler** (`Directory.Build.props` names the diagnostics in `WarningsAsErrors`): CS1591 a
  public or protected member with no comment, CS1573/CS1572/CS1571 `<param>` that is partial, names
  nothing or is duplicated, CS1574/CS1580/CS1584 a `cref` that resolves to nothing, CS1587 a comment
  documenting nothing, CS1710-CS1712 the same for `<typeparam>`, CS1734 a `<paramref>` naming nothing.
- **`--lint`** (CI runs it): what the compiler leaves alone. A blank `<summary>`, or one that only
  repeats the member's name; a `<param>` or `<typeparam>` that is missing or empty; a method returning
  a value (a `Task<T>` counts, a bare `Task` does not) with no `<returns>`; an unresolved `!:` cref.
  `<inheritdoc/>` is resolved first, so an override or interface implementation inherits what it needs.
  A record documents its positional parameters with `<param>` on the type; its constructor and
  properties are read from that. An implicit parameterless constructor is not demanded: the compiler
  gives no way to document it.

Not linted, because no tool can know: `<exception>`. Document each exception a member **deliberately**
throws and a caller might handle (`KeyNotFoundException`, `InvalidDataException`,
`UnsupportedSchemaVersionException`, `PartialAlphaException`, `ArgumentOutOfRangeException` for a
range, ...). A plain `ArgumentNullException.ThrowIfNull` guard on a non-nullable parameter is not
documented: the nullable annotation already states that contract.

The renderer shows summary, type parameters, parameters, value, returns, exceptions, remarks,
examples (`<example>` with `<code>`) and block-level `<seealso>` for each member and type.

## Changing it

Edit `build.py` (prose and layout) or `surface/Program.cs` (signatures), regenerate, and commit the
pages with the change. Never edit a generated page by hand: the next run overwrites it, and `--check`
fails until you do.
