# Open files with nfty on Windows

Register selected file types for the nfty app or its read-only viewer, then choose their defaults in
Windows Settings. The **SYSTEM** tab is available on Windows; it is absent on Linux and macOS.

nfty registers nothing until you ask. Registration is **per user** and needs no administrator rights.
Registering a program and choosing the program Windows opens by default are separate actions.

## Choose what opens where

1. Open **Settings** (the sliders button at the right end of the status bar), then **SYSTEM**.
2. Check the extensions you want to change, or use **All**. The selection count shows how many file
   types the next action includes. Changing a checkbox does not write anything.
3. Choose **App** or **Viewer**, then press **Register**.

Only the selected extensions change. Unselected registrations, including entries pointing at another
copy of nfty, are preserved. On opening the card, existing registrations are selected; clear **All**
first if you want to change just one.

| Target | Opens |
|---|---|
| **App** | Supported nfty archives in the app: `.cbk`, `.rcp`, `.igt`, `.set`, `.tin`, and `.ktn`. |
| **Viewer** | Any supported type in the read-only [viewer](viewer.md), without the app shell. |

A Menu (`.mnu`), picture (`.png`, `.jpg`, `.jpeg`), model (`.glb`, `.gltf`) or JSON file always uses
**Viewer**, including when **App** is selected. A `.tin` still asks for its passphrase. JSON files
must contain supported asset metadata; registering `.json` does not make arbitrary JSON viewable.

Each double-click starts its own nfty process, so two files open in two windows.

## Choose the Windows defaults

**Register** adds nfty to the selected types' **Open with** lists and registers its capabilities for
**Default apps**. It does not override a protected Windows default, for any extension.

For nfty's archive extensions, an empty or already-owned per-user class default can point at nfty.
Another program's class default is preserved. Windows' protected effective choice still takes
precedence, so registration alone does not prove double-clicks will open this copy.

1. Press **Default apps…**. On Windows 11 23H2 or newer, this opens nfty's page. Older versions open
   the general **Default apps** page; find nfty there.
2. Choose the file types Windows should open with nfty.
3. Return to nfty and press **Refresh** to read the result.

Windows owns this choice. nfty never writes the protected `UserChoice` value or its hash.

## Read the status

Checkboxes select the next action; the status beside each extension describes the registration
already on the machine. Hover over a row for its full registration and default explanation.

| Row status | Means |
|---|---|
| **not registered** | This extension has no nfty registration nfty recognizes. |
| **app · registered** / **viewer · registered** | Registration is complete and points at this copy in that mode. It does not claim to be the Windows default. |
| **app · default** / **viewer · default** | Windows reports this type's handler and command for this copy in that mode as its effective default. |
| **needs repair** | A registration is incomplete, or points at another copy. Select the extension, choose its mode, and **Register** again. |

The summary counts registrations, defaults identified as this copy, and types whose default could
not be checked (**unknown**). Unknown does not mean another program is the default. **Refresh**
reads these states without changing registration or selection.

If a shared **Default apps** listing belongs to another copy or is missing, the row explains it.
Select **All** and **Register** to repair the shared listing, or use **Open with**. Registering one
extension preserves an unselected copy's shared listing.

## Take it back

Select the extensions to remove, then press **Unregister**. For all registrations, use **All** first.
The action removes only selected values nfty owns:

- a value is deleted only while it still contains what nfty wrote, and a key only once it is empty;
- another program's values, defaults and added entries are preserved;
- an older shared handler stays if an unselected sibling still references it, or its effective
  default cannot be checked safely.

nfty keeps a ledger of the keys and values it created, so a partial failure can be retried. Removing
a registration does not choose a replacement Windows default; use **Default apps…** if needed.

## What gets registered

For reference, or for checking with `regedit`. All paths are under `HKEY_CURRENT_USER\Software`.

| Key | Holds |
|---|---|
| `Classes\nfty.CookBook`, `nfty.Recipe`, `nfty.Ingredient`, `nfty.Set`, `nfty.Sealed`, `nfty.Kitchen`, `nfty.Menu` | Archive ProgIDs: friendly name, icon and open command. |
| `Classes\nfty.Viewer.Image.png`, `nfty.Viewer.Image.jpg`, `nfty.Viewer.Image.jpeg` | Independent picture ProgIDs. |
| `Classes\nfty.Viewer.Model.glb`, `nfty.Viewer.Model.gltf` | Independent model ProgIDs. |
| `Classes\nfty.Viewer.Metadata` | JSON metadata's viewer ProgID. |
| `Classes\nfty.Viewer.Image`, `nfty.Viewer.Model` | Older shared handlers, recognized and preserved while an unselected type may still need them. |
| `Classes\<.ext>\OpenWithProgids` | nfty's entry in the selected type's **Open with** list. |
| `Classes\<.ext>` (default value) | Set only for nfty's archive types, and only when empty or already owned by that type. |
| `nfty\Capabilities` | Description and file types for **Default apps**. |
| `RegisteredApplications` (value `nfty`) | Points **Default apps** at those capabilities. |
| `nfty\Associations` | The ownership ledger. |

The open commands are:

```text
"<folder>\Nfty.Desktop.exe" "%1"              # App
"<folder>\Nfty.Desktop.exe" --viewer "%1"     # Viewer
```

The same two forms work from a shortcut or a terminal, with or without registration.

## When something goes wrong

If Windows refuses a read, write or launch, nfty shows a product explanation. After a partial write,
close the error card and inspect the rows. The ledger records completed writes so **Register** or
**Unregister** can retry the selected operation. Unselected registrations remain outside that action.
