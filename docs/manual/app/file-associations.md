# Open files with nfty on Windows

Make double-clicking a CookBook, a Set, a picture or a model in File Explorer open it in nfty, or in
the nfty viewer on its own. This is a Windows feature: on Linux and macOS the SYSTEM tab is not
there.

nfty registers nothing until you ask. Everything it writes is **per user** (under
`HKEY_CURRENT_USER`), so no administrator rights are needed, and **Remove** takes back exactly what it
wrote.

## Choose what opens where

1. Open **Settings** (the gear in the status bar) and the **SYSTEM** tab.
2. Choose a target for each row:

    | Row | Files | Choices | Suggested |
    |---|---|---|---|
    | **NFTY FILES** | `.cbk` `.rcp` `.igt` `.set` `.tin` `.ktn` `.mnu` | Off, **App**, **Viewer** | App |
    | **PICTURES, 3D** | `.png` `.jpg` `.jpeg` `.glb` `.gltf` | Off, **Viewer** | Viewer |
    | **METADATA** | `.json` | Off, **Viewer** | Off |

3. Press **Apply**. The **REGISTRATION** chip reads **registered**, and the line under it says how many
   file types now open with this copy of nfty.

What the targets do:

- **App** opens the file the way a Recent row would: a CookBook in the Explorer, a Set in the Set
  browser, a `.tin` behind its passphrase, a Kitchen as the open workspace. A Menu (`.mnu`) always
  opens in the viewer, because the app does not edit one.
- **Viewer** opens the file in the [viewer](viewer.md) alone, with no app window behind it, read-only.
  An archive opens as a gallery; a picture, model or metadata file opens on its own.
- **METADATA** starts Off because most `.json` files are not asset metadata. The viewer shows the ones
  in a Set's `metadata/` or `nfty/` folder and says so for any other.

Each double-click starts its own nfty process, so two files open in two windows.

## Make nfty the default for pictures, models and JSON

For nfty's own extensions, **Apply** sets the default only when it is empty or already belongs to
nfty. Another program's default is preserved. If Windows already has a different choice for an
extension, select nfty through **Default apps…**.

For `.png`, `.jpg`, `.glb`, `.gltf` and `.json`, Windows does not let *any* program make itself the
default. Since Windows 10 the default is recorded in a per-user `UserChoice` key protected by a hash
that only Windows' own settings can write, so the choice is always yours. **Apply** adds nfty to those
types' **Open with** list and to Windows' **Default apps**, and stops there.

To finish:

1. Press **Default apps…**. Windows Settings opens on nfty's page.
2. Pick each type you want nfty to open.

## Read the REGISTRATION chip

| Chip | Means | Do |
|---|---|---|
| **not registered** | Nothing is registered. | Choose targets, then **Apply**. |
| **not applied** | You changed a row since the last Apply. | **Apply**. |
| **registered** | Every chosen type opens with this copy of nfty. | Nothing. |
| **needs repair** | The registration points at another copy of nfty (usually because the app was moved or updated into another folder), or part of it is missing. The line under the chip says which. | Press **Repair** (the Apply button relabels itself). |

## Take it back

Press **Remove**. nfty deletes only what it wrote, for your Windows user:

- a value is deleted only while it still holds what nfty wrote, and a key only once it is empty;
- another program's default, or an entry another program added since, stays.

nfty keeps a ledger of every key and value it created (under `HKEY_CURRENT_USER\Software\nfty`), which
is how it knows. **Apply** removes the previous registration the same way before writing the new one.

## What gets registered

For reference, or for checking with `regedit`. All paths are under `HKEY_CURRENT_USER\Software`.

| Key | Holds |
|---|---|
| `Classes\nfty.CookBook`, `nfty.Recipe`, `nfty.Ingredient`, `nfty.Set`, `nfty.Sealed`, `nfty.Kitchen`, `nfty.Menu` | One ProgID per nfty file type: its name, icon and open command. |
| `Classes\nfty.Viewer.Image`, `nfty.Viewer.Model`, `nfty.Viewer.Metadata` | The viewer ProgIDs for pictures, models and JSON. |
| `Classes\<.ext>\OpenWithProgids` | nfty's entry in each type's **Open with** list. |
| `Classes\<.ext>` (default value) | Set only for nfty's own extensions, and only when empty or already nfty's. |
| `nfty\Capabilities` | nfty's description and file types for **Default apps**. |
| `RegisteredApplications` (value `nfty`) | Points **Default apps** at those capabilities. |
| `nfty\Associations` | The ledger Remove reads. |

The open commands are:

```text
"<folder>\Nfty.Desktop.exe" "%1"              # App
"<folder>\Nfty.Desktop.exe" --viewer "%1"     # Viewer
```

The same two forms work from a shortcut or a terminal, with or without any registration.

## When something goes wrong

If Windows refuses a read or a write, the card says so in a sentence and nothing half-done is left
unrecorded: a failed Apply keeps its ledger, so **Remove** or **Repair** can finish the job.
