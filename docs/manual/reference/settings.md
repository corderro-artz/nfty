# Settings files and environment variables

Where nfty keeps its own state, what each file holds, and the environment variables it reads. For the
Settings card itself, see [Settings](../app/settings.md). Your CookBooks and Sets are never stored here;
they are the files you chose.

## The `.nfty` folder

nfty keeps its state in a folder named `.nfty`, found in this order:

1. **Beside the program**, when that folder is writable. An unpacked copy keeps its history to itself.
2. On Linux, **`$XDG_CONFIG_HOME/nfty`** (or `~/.config/nfty`), when the program's folder is read-only
   (`/opt`, `/usr`, a read-only mount).
3. The **current working directory**.
4. The open **Kitchen's** folder.
5. Nowhere: state lasts for the session, and the Settings card's DATA tab says so.

**Settings → DATA → KEPT IN** shows which one is in use; **Open folder** opens it. Delete the folder and
everything starts fresh.

| File | Holds |
|---|---|
| `settings.json` | The Settings card's choices (keys below). |
| `recents.json` | The opening screen's Recent list (up to ten). |
| `palette.json` | Saved swatches: one list for Gray mode, one for Color. |
| `editor-view.json` | The Ingredient Editor's remembered view (grid, rail tab). Not zoom or pan. |
| `viewer-window.json` | Viewer window sizes and positions. |

## `settings.json` keys

Each key is written on its own, so changing one never resets another. A missing key means the default.

| Key | Values | Default |
|---|---|---|
| `theme` | `"system"`, `"light"`, `"dark"` | `"system"` |
| `defaultCanvas` | Starting canvas side, 1 to 4096 | 32 |
| `threads` | A whole number, or `null` for Auto | Auto (two thirds of logical processors) |
| `pinningProvider` | `"localIpfs"` or `"pinata"` | `"localIpfs"` |
| `kuboApi` | The local IPFS RPC address | `"http://127.0.0.1:5001"` |

The Pinata key is **never** in this file. It is in the operating system's credential store under the
name `nfty/pinata` (Windows Credential Manager, the macOS keychain, or the Secret Service through
`secret-tool` on Linux).

## Environment variables

| Variable | Does |
|---|---|
| `NFTY_THREADS` | The worker thread count, as a whole number or `max`. Read by the command line when `--threads` is absent; in the app it overrides and locks the Settings card. |
| `NFTY_PERF` | `1` switches on the built-in timing scopes (`Nfty.Core.Diagnostics.Perf`) for code embedding the libraries, read with `Perf.Report()`. Prints nothing by itself, never changes output. |
| `XDG_CONFIG_HOME` | Linux: where the fallback state folder goes. |

## Windows registry

nfty writes to the registry only when you press **Apply** on the Settings card's SYSTEM tab, and only
under `HKEY_CURRENT_USER`. Every key and value is listed in
[What gets registered](../app/file-associations.md#what-gets-registered).
