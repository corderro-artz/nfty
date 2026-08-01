# nfty GUI — Real, persisted Recents (D1) design spec

**Date:** 2026-08-01
**Status:** Approved (design), pending implementation planning
**Scope:** Make the Landing's **Recent** list real: record what the user actually opens, persist it
across runs, and make clicking a row reopen that file. Today the list is three hardcoded demo rows for
files that don't exist and clicking does nothing — a visible falsehood on the app's first screen.

## 0. Program bar
Rock-solid, efficient; best practices; pull docs (Context7) rather than assume any library API; escalate
anything off. Reuse the existing open paths (`OpenPath`, `OpenLooseIngredient`, `OpenLooseRecipe`,
`OpenSet`). No `Nfty.Core` change. All JSON via `System.Text.Json` (this is app state, not a manifest, so
`Nfty.Core.Formats.Json.Options` does not apply — but mirror its camelCase for consistency).

## 1. Goals & non-goals
**Goals**
- Opening a `.cbk` / `.igt` / `.rcp` / `.set` (from Open, Import, Open-Set, or a create flow) **records a
  recent entry**: display name, a short subtitle, the absolute path, and whether it is a loose file.
- Recents **persist across runs**, most-recent-first, de-duplicated by path, capped (10).
- Clicking a recent row **opens it** through the same dispatch the Import path uses (by extension).
- A recent whose file has since been **deleted/moved** shows an error and is **removed from the list**
  rather than silently failing.
- The seeded demo rows are gone: a first run shows an **empty** list.

**Non-goals (this slice)**
- Search / ⌘K (that is D2). Pinning, reordering, or a "clear recents" affordance. Thumbnails on rows.
- Recording *Set* browsing separately from cookbooks (a `.set` recent simply reopens the Set browser).
  Any `Nfty.Core` change.

## 2. Components

### 2.1 Persistence (`RecentsService`)
- Replace the hardcoded list with load-on-construct / save-on-mutate JSON at
  `Environment.GetFolderPath(SpecialFolder.ApplicationData)/nfty/recents.json`
  (`%APPDATA%\nfty\recents.json` on Windows).
- `Add(RecentItem item)`: remove any existing entry with the same `Path` (ordinal), insert at index 0,
  truncate to **10**, save.
- New `void Remove(string path)` — used when a recent turns out to be missing (§2.3); saves.
- **Failure is non-fatal:** an unreadable/corrupt file yields an empty list; a failed save is swallowed
  (recents are convenience state, never worth blocking or crashing the app for). Both are silent — no
  dialog.
- The file is a simple array of `{ name, subtitle, path, loose }`.

### 2.2 Recording (`LandingViewModel`)
- After each successful open, record the entry:
  - `OpenPath` (a `.cbk`) → `new RecentItem(book.Manifest.Name, $"{book.Recipes.Count} recipes · {W}×{H}", path, false)`.
  - `OpenLooseIngredient` → `new RecentItem(ing.Manifest.Name, $"loose ingredient · {n} variants", path, true)`.
  - `OpenLooseRecipe` → `new RecipeItem`-equivalent: `$"loose recipe · {n} ingredients"`, loose = true.
  - `OpenSet` → `new RecentItem(set.Manifest.Name, $"set · {set.Manifest.Count} assets", path, false)`.
- Recording happens **only after** the read succeeds, so a failed open never pollutes the list.
- `Recents` is exposed to the view as today; the Landing raises `OnPropertyChanged(nameof(Recents))`
  after each add so the list refreshes without a restart.

### 2.3 Reopening (`LandingViewModel.OpenRecent`)
Replace the `_notify.Report(...)` stub:
```csharp
[RelayCommand]
private void OpenRecent(RecentItem item)
{
    if (!File.Exists(item.Path))
    {
        _recents.Remove(item.Path);
        OnPropertyChanged(nameof(Recents));
        ShowError("Missing file", $"“{item.Path}” is no longer there, so it was removed from Recents.");
        return;
    }
    switch (Archives.KindOf(item.Path))      // throws on an unknown extension → caught below
    { ... .cbk → OpenPath; .igt → OpenLooseIngredient; .rcp → OpenLooseRecipe; ... }
}
```
- A `.set` path is not an `ArchiveKind`, so dispatch on the extension first: `.set` → the existing
  `OpenSet`-equivalent body (extract `OpenSetPath(path)` from `OpenSet` so both the picker flow and a
  recent can call it), everything else → `Archives.KindOf`.
- An unknown/unsupported extension → error dialog, entry left in place (it is not "missing", just not
  openable).

### 2.4 View
No change — the Landing already binds `Recents` and `OpenRecentCommand` per row. An empty list simply
renders nothing (confirm the existing markup tolerates zero rows; if it shows an empty box, that is E's
concern, not this slice's).

## 3. Data flow
```
Open/Import/OpenSet succeeds → _recents.Add(entry) → saved to %APPDATA%/nfty/recents.json
                              → OnPropertyChanged(Recents) → the Landing list refreshes
Click a recent → file missing?  → remove + error
               → .set           → OpenSetPath
               → .cbk/.igt/.rcp → the matching existing open path
```

## 4. Error handling
- Corrupt/unreadable `recents.json` → treated as empty; never blocks startup.
- Failed save → swallowed (convenience state).
- Missing file on click → removed from the list + explanatory error.
- Unsupported extension on click → error, entry retained.
- A recent that opens but then fails to read (corrupt archive) → the existing open-path error dialog; the
  entry is **retained** (the file exists; it is the content that is broken).

## 5. Testing
- **Service:** `Add` de-dupes by path (same path twice ⇒ one entry, moved to the front), caps at 10
  (11th add drops the oldest), and round-trips through the file (a second service instance over the same
  directory sees the entries). `Remove` deletes by path. A corrupt file yields an empty list without
  throwing. Tests must **not** touch the real `%APPDATA%` — make the storage directory injectable
  (ctor parameter defaulting to the real location) and point tests at a temp dir.
- **Landing:** opening a real temp `.cbk` records a recent with that path; opening a `.igt` records a
  loose entry; a **failed** open records nothing. Clicking a recent whose file was deleted removes it and
  shows an error; clicking a valid `.cbk` recent opens the Explorer.
- **No regression:** the Landing/Explorer/loose suites stay green; full suite green; build 0 warnings; no
  raw hex; no `Nfty.Core` change.
- **Manual smoke:** open a cookbook → it appears at the top of Recent → restart the app → it is still
  there → click it → it reopens; delete the file on disk → click it → it errors and disappears from the
  list.

## 6. Risks & escalation
- **Test isolation:** the service must never read/write the developer's real `%APPDATA%` during tests —
  the injectable directory is the guard; a test that forgets it would pollute the machine and could pass
  or fail depending on local state.
- **Path identity:** de-dup compares the stored path ordinally. The same file reached via a different
  spelling (relative vs absolute, different casing on Windows) would double-enter. Store
  `Path.GetFullPath(path)` on add so the common case normalises; case-insensitivity is not attempted.
- **Silent failures are deliberate:** recents must never interrupt the user. That means a broken save is
  invisible — acceptable for convenience state, but do not extend this silence to anything that writes
  the user's actual artwork.
- **The demo rows disappear:** the Landing will look empty on first run, which is correct but a visual
  change from the mockup's populated list. E may want an empty-state line ("Nothing opened yet") — out of
  scope here.
