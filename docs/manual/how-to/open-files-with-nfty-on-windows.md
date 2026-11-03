# Open files with nfty on Windows

Double-click a CookBook, a Set or a picture in File Explorer and have it open in nfty, or in the nfty
viewer on its own. This is a Windows setting: on macOS and Linux the page below is not there.

## Choose what opens where

Open **Settings** and the **SYSTEM** tab. There are three rows:

| Row | Files | Can open in |
|-----|-------|-------------|
| **NFTY FILES** | `.cbk` `.rcp` `.igt` `.set` `.tin` `.ktn` `.mnu` | the app, or the viewer alone |
| **PICTURES, 3D** | `.png` `.jpg` `.jpeg` `.glb` `.gltf` | the viewer |
| **METADATA** | `.json` | the viewer |

- **App** opens the file as **Open recent** would: a CookBook in the Explorer, a Set in the Set
  browser, a `.tin` behind its passphrase, a Kitchen as the open workspace. A Menu (`.mnu`) always opens
  in the viewer, because the app does not edit one.
- **Viewer** opens the file in the pop-out viewer with no app window behind it. An archive opens as a
  gallery; a picture, a model or an asset's metadata file opens on its own.
- **METADATA** is off to begin with. Most `.json` files are not an asset's metadata; the viewer shows
  the ones in a Set's `nfty/` or `metadata/` folder and says so for any other.

Press **Apply**. The chip beside it reads **registered**.

## Make nfty the default for pictures and JSON

For nfty's own files, Apply is enough: no other program claims `.cbk` or `.set`, so a double-click
opens nfty. For `.png`, `.jpg`, `.json` and models, Windows lets only **you** choose the default
program, so Apply adds nfty to the **Open with** list and stops there. Press **Default apps…**, which
opens Windows Settings on nfty's page, and pick the types you want nfty to open.

## When the chip says "needs repair"

The registration points at a copy of nfty that is not this one, usually because the app was moved or
updated into another folder, or part of it was removed by hand. The line under the chip says which.
Press **Repair**.

## Take it back

**Remove** deletes exactly what nfty registered, for this Windows user, and nothing else: another
program's default, or an entry another program added since, stays. Nothing nfty does here needs
administrator rights.
