# nfty GUI — Create a new CookBook (C0) design spec

**Date:** 2026-08-01
**Status:** Approved (design), pending implementation planning
**Scope:** Wire the **New CookBook** wizard so it actually creates an empty `.cbk` on disk and opens it
in the Explorer. Closes the last hole in the core create → edit → cook loop: today the GUI can only open
an *existing* cookbook. Discovered while scoping slice C.

## 0. Program bar
Rock-solid, efficient; best practices; pull docs (Context7) rather than assume any library API; escalate
anything off. Reuse everything already shipped: the wizard's bound fields, `CookBookArchive.Write`, the
real `DesktopFilePicker.SaveFileAsync` (B3a), `session.Open`, the Explorer factory, and the
temp-then-move overwrite pattern (B3b). No `Nfty.Core` change.

## 1. Goals & non-goals
**Goals**
- Landing's **New CookBook** opens the wizard; on **Create** it writes an **empty** cookbook (no recipes)
  to a user-chosen `.cbk` and opens it in the Explorer, with the session's source path set — so the user
  can immediately **Add recipe** → **Add ingredient** (A2) and later **Cook**.
- The manifest is built from the wizard's existing fields: `Id = DerivedId` (name slug), `Name`,
  `Canvas = (Width, Height)`, `Collection = (Name, Description, Symbol)`, `RecipeWeights = {}` (empty).
- **Create is gated on a non-blank derived id** (the F1 lesson), and the Explorer's own guard is
  unnecessary here because the wizard is the only entry point — but the Landing flow re-checks
  authoritatively.
- The write is **overwrite-safe**: a Save picker can legitimately return an existing path, and
  `CookBookArchive.Write` opens `ZipArchiveMode.Create` (`FileMode.CreateNew`), which throws on an
  existing file — so the write goes through a sibling temp then an atomic `File.Move(overwrite: true)`,
  matching `CookBookPersistence`/`LooseWorkspace.WriteIngredient`.

**Non-goals (this slice)**
- Creating a cookbook **pre-populated** with a recipe/ingredient (the user adds them via A2 immediately
  after). Validating the empty cookbook at create time (an empty cookbook is a legal starting point but
  not generatable — `Validator` reports that at cook time, exactly as A2c's empty recipe does). The
  Shell's own "New Kitchen" stub. Any `Nfty.Core` change.

## 2. Components

### 2.1 New CookBook wizard (`NewCookBookViewModel`)
- `Create` closes with the VM (was `Notify.Report(...) + Close(null)`), gated on a non-blank id — matching
  the New Ingredient/Recipe wizards:
  ```csharp
  private bool CanCreate() => !string.IsNullOrWhiteSpace(DerivedId);

  [RelayCommand(CanExecute = nameof(CanCreate))]
  private void Create() => Dialogs.Close(this);
  ```
- `OnNameChanged` also re-notifies `CreateCommand` (so the button enables as the user types).
- Everything else (name/symbol/width/height/aspect-lock/description, `DerivedId`) already exists and is
  bound in `NewCookBookView.axaml` — **no view change**.

### 2.2 Overwrite-safe cookbook write (`CookBookPersistence`)
Add beside `PersistAsync`:
```csharp
/// <summary>Writes a cookbook to a path the user chose, replacing an existing file (CookBookArchive
/// .Write opens CreateNew and would throw): sibling temp + atomic move. Used when creating a new .cbk.</summary>
public static void WriteNew(string path, CookBookManifest manifest, IReadOnlyList<LoadedRecipe> recipes)
{
    var tmp = path + ".tmp";
    try
    {
        CookBookArchive.Write(tmp, manifest, recipes);
        File.Move(tmp, path, overwrite: true);
    }
    finally { if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { /* best effort */ } } }
}
```

### 2.3 Landing create flow (`LandingViewModel`)
Replace the fire-and-forget stub `[RelayCommand] private void NewCookBook() => _dialogs.ShowAsync<object>(...)`:
```csharp
[RelayCommand]
private async Task NewCookBook()
{
    var wizard = new NewCookBookViewModel(_dialogs, _notify);
    var result = await _dialogs.ShowAsync<NewCookBookViewModel>(wizard);
    if (result is null) return;                                   // cancelled
    if (string.IsNullOrWhiteSpace(result.DerivedId))
    {
        ShowError("Invalid cookbook", "The cookbook needs a name.");
        return;
    }
    string? path;
    try { path = await _picker.SaveFileAsync("Save new cookbook", ".cbk"); }
    catch (Exception ex) { ShowError("Could not save", ex.Message); return; }
    if (path is null) return;                                     // cancelled the picker

    var manifest = new CookBookManifest(result.DerivedId, result.Name,
        new Dimensions(result.Width, result.Height),
        new Collection(result.Name, result.Description, result.Symbol),
        new Dictionary<string, double>());                        // no recipes yet
    try { CookBookPersistence.WriteNew(path, manifest, Array.Empty<LoadedRecipe>()); }
    catch (Exception ex) { ShowError("Could not save", ex.Message); return; }

    OpenPath(path);   // existing: reads it back (fresh SourceSha256), session.Open(book, path), → Explorer
}
```
- **`OpenPath` reuse** is the key: it already reads, opens into the session **with the source path**, and
  navigates to the Explorer — so Add recipe/ingredient and Save are immediately enabled on the new book.
- No images are allocated (an empty cookbook has none), so there is **no disposal bookkeeping** on this
  path — simpler than the loose-ingredient create.

### 2.4 View
No change — the wizard view already binds every field and the Create button.

## 3. Data flow
```
Landing New CookBook → wizard → Create (enabled only with a non-blank id)
  → path = SaveFileAsync(".cbk")                       (cancel → stop)
  → manifest = CookBookManifest(DerivedId, Name, (W,H), Collection(Name, Description, Symbol), {})
  → CookBookPersistence.WriteNew(path, manifest, [])   (temp + atomic move; replaces an existing file)
  → OpenPath(path)                                     (read → session.Open(book, path) → Explorer)
```

## 4. Error handling
- Cancelled wizard / cancelled picker → no-op.
- Blank derived id → error (belt-and-suspenders; the wizard's Create is already disabled).
- Write failure → temp cleaned up, error dialog, nothing opened, the previously-open cookbook untouched
  (`OpenPath` is only reached on success).
- A read failure right after the write → `OpenPath`'s existing error dialog.

## 5. Testing
- **Wizard:** `Create` is disabled for a blank/whitespace name and enabled once the name yields an id;
  `Create` closes the dialog with the VM. (`DerivedId` + the aspect-lock behaviour are already covered.)
- **`CookBookPersistence.WriteNew`:** writes a readable `.cbk` (round-trips via `CookBookArchive.Read`
  with the manifest's id/name/canvas/collection intact); **replaces an existing file** rather than
  throwing; leaves no `.tmp`.
- **Landing New CookBook** (`[AvaloniaFact]`, wizard-dialog stub + Save-picker stub returning a temp
  path): writes the `.cbk`, opens it into the session (`session.Current` non-null, `SourcePath` == the
  path), and navigates to an `ExplorerViewModel` whose root carries the cookbook name; a **cancelled**
  picker writes nothing and does not navigate; a **blank** name errors and writes nothing.
- **End-to-end continuity** (the point of the slice): after creating, `AddCommand` on the root is enabled
  (edit-mode on) — i.e. the new cookbook is immediately authorable. Assert `SourcePath is not null` (the
  gate A2 uses) rather than driving the whole add flow again.
- **No regression:** the Landing/Explorer/loose suites stay green; full suite green; build 0 warnings; no
  raw hex; no `Nfty.Core` change.
- **Manual smoke:** Landing → New CookBook → name/symbol/canvas/description → Create → a Save dialog
  appears → choose a path → the Explorer opens on the new empty cookbook → toggle edit → Add recipe → Add
  ingredient → paint → Save → Cook. Re-creating over the same path replaces it cleanly.

## 6. Risks & escalation
- **Empty cookbook is legal but not generatable:** `Validator` will report "no recipes" at cook time. That
  is the intended starting state (identical to A2c's empty recipe). Confirm the Explorer renders an empty
  book without throwing — `BuildTree` over zero recipes yields a childless root, and the cookbook detail's
  unique-space count must tolerate it (B2's review confirmed `UniqueSpace.Count` is non-throwing for a
  degenerate book; re-confirm for **zero recipes** in the test).
- **Overwrite:** the Save picker can return an existing path; `WriteNew`'s temp+move handles it. Without
  it the user would get a raw "file already exists" `IOException` — the same defect B3b's review found on
  the loose path.
- **Session replacement:** creating a cookbook calls `OpenPath` → `session.Open`, which disposes the
  currently-open cookbook. That is standard open-a-document behaviour (identical to Open/Import) and is
  what the user expects when starting a new project; note it in the smoke.
