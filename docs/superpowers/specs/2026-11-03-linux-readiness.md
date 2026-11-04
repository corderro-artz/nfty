# Linux readiness review (2026-11-03)

> **RESOLUTION (same day, on top of `339e009` v2.2.0).** Every blocker and should-fix item below is
> fixed or, where it cannot be fixed in nfty, documented; each item carries a *Resolved* note. Proof
> was taken on WSL2 Ubuntu 24.04.4 + WSLg from the Windows-built linux-x64 tarball: exec bits survive
> `tar -xzf`; the CLI ran `demo` → `generate --count 12 --models` → `export --preset marketplace` →
> `inspect`; the GUI opened the Set from argv and an export was driven THROUGH THE GUI (Export card →
> Marketplace → GTK folder picker → "Exported 12 assets"), its 37 entries byte-identical to the CLI
> export of the same Set; a read-only install kept its state in `$XDG_CONFIG_HOME/nfty` (Settings →
> Data says so); the X11 window carries `_NET_WM_ICON`. Still unverified: a real GNOME/KDE keyring
> round trip, a native Wayland session, a portal-only file dialog, linux-arm64 execution.

Reviewed at `b3bfb48` (worktree-nfty-ticket). Tested on WSL2 Ubuntu 24.04.4 (x86_64, WSLg 1.0.71, dotnet SDK 10.0.109 + runtime 10.0.9). Product code untouched.

## Verdict: READY WITH FIXES

- **CLI: ready now.** Publishes, runs, byte-identical to Windows, all four non-GUI test projects green on Linux.
- **GUI: runs and renders on Linux** (WSLg), `App.Tests` (1871) pass headless on Linux. One startup crash on minimal distros (libICE/libSM) and no Linux packaging yet.
- **Release pipeline: not Linux-capable.** `tools/release/build.py` produces a broken Linux artifact (see B1).

## Evidence summary

| Check | Result |
|---|---|
| `dotnet publish src/Nfty.Desktop -r linux-x64` (self-contained, from Windows) | OK, 21 s for both heads; 107 MB folder; `libSkiaSharp.so`, `libHarfBuzzSharp.so` present, `Avalonia.X11.dll`, `Tmds.DBus.Protocol.dll` |
| single-file self-contained | OK: desktop 98.9 MB, cli 73.5 MB, ONE file each (natives embedded) |
| single-file framework-dependent | OK: desktop 28.8 MB, cli 3.4 MB |
| `-r linux-arm64` framework-dependent | Publishes OK, arm64 `libSkiaSharp.so` + `libHarfBuzzSharp.so` included. Not run (no arm64 host) |
| CLI in WSL: `demo` -> `generate --count 20` -> `inspect` -> `export` | all OK, all three CLI shapes |
| Determinism cross-OS | `generate --seed lx --count 20`: image SHA-256 of Linux output == Windows output (3 of 20 compared, identical) |
| `ipfs status` | "Local IPFS: not reachable" (graceful, no Kubo on 127.0.0.1:5001 inside WSL) |
| `dotnet test` in WSL | Ticket 70/70, Core 1562 pass + 1 skip, Model 498/498, Cli 320/320, **App 1871 pass + 2 skip (headless, 3m53s)** |
| GUI launch under WSLg | Window renders (landing screen, custom chrome, fonts, kitchen shelf), stays up 20-40 s, exit only by `timeout`. `.nfty/` created beside exe. Needs the lib shim in B2 |
| Windows-only API sweep | `advapi32` P/Invoke only behind `OperatingSystem.IsWindows()`; `explorer.exe` behind `RuntimeInformation` with `xdg-open` fallback; no Registry, no `UseWindowsForms`, no `\\` literals, temp via `Path.GetTempPath()`. CI already runs Core/Cli/Model on ubuntu |

## Blockers

### B1. build.py cannot make a working Linux artifact
`tools/release/build.py` (evidence: reading it, plus Python zip test: `zipfile.write` on Windows stores mode 0o666 by default; on Linux 0o755 is preserved).
- Always emits `.zip`; built on Windows the zip loses the exec bit, so `Nfty.Desktop` / `Nfty.Cli` do not run after unzip (`Permission denied`).
- `READ-ME-FIRST.txt` says `Nfty.Desktop.exe`, `cli/Nfty.Cli.exe`, written CRLF; "DESKTOP runtime" wording is Windows-specific (on Linux it is `dotnet-runtime-10.0`).
- `unpack_demo` picks the CLI name by `os.name`, not by RID, so a Windows-hosted Linux build would try `Nfty.Cli.exe` and silently fall back to the source copy (it prints that, but it defeats the check the docstring promises). Cross-RID run is impossible on Windows; a WSL step could do it.
- `.release` folder name already carries the RID, fine.

Fix: see "Proposed build.py changes" below. Ship **tar.gz** for Linux (exec bit, symlink-safe), zip stays for Windows.

*Resolved.* `build.py <ver> linux-x64` writes four `.tar.gz` (portable 79.5 MB, single-file 72.7,
framework-dependent 12.3, single-file-net10 12.3) with modes set by the tar filter (0755 binaries,
`createdump`, the install script and dirs; 0644 the rest; uid/gid 0). No `.exe`; READ-ME-FIRST in
LF with Linux wording, the dependency line and the runtime named `dotnet-runtime-10.0`. The demo is
extracted by the TARGET's CLI: on a Windows host through `wsl` (drvfs shows every file executable),
otherwise the source copy with the line saying so. One trap found on the way: the `.desktop` file
first named `nfty.desktop` IS `Nfty.Desktop` on the case-insensitive Windows build host and replaced
the app binary with 237 bytes of text (the 1.3 MB "single-file-net10" tarball gave it away). Now
`dev.vaporsoft.nfty.desktop`, and build.py refuses a Linux artifact whose two heads are not ELF.
Windows output unchanged: file lists of all four win-x64 zips and the READ-ME bytes identical before
and after.

### B2. Startup crash without libICE / libSM
Fresh WSL Ubuntu 24.04 (minimal, no GTK stack):
```
Unhandled exception. System.DllNotFoundException: Unable to load shared library 'libICE.so.6'
   at Avalonia.X11.ICELib.IceAddConnectionWatch(...)
   at Avalonia.X11.X11PlatformLifetimeEvents..ctor(AvaloniaX11Platform platform)
   at Avalonia.X11.AvaloniaX11Platform.Initialize(X11PlatformOptions options)
   at Nfty.Desktop.Program.Main(String[] args)
```
Same in self-contained and framework-dependent shapes. With `libice6 libsm6` supplied (extracted with `apt-get download` + `dpkg -x` into a private dir, `LD_LIBRARY_PATH`; nothing installed system-wide) the app starts fine in all three GUI shapes (portable, single-file, single-file-net10).

I tried the obvious code workaround, `.With(new X11PlatformOptions { EnableSessionManagement = false })` in `Program.cs` (throwaway copy): **does not help in Avalonia 12.1.1**, same trace. So this is an upstream behavior; a normal desktop (GNOME/KDE) has these libs, a container/minimal server does not.

Fix: document the package list and declare it as `Depends` if a .deb is made: `libice6 libsm6 libx11-6 libfontconfig1` (+ `libxcursor1 libxrandr2 libxi6` already present here). Optionally wrap `StartWithClassicDesktopLifetime` in a catch for `DllNotFoundException` that prints "install libice6 libsm6" instead of a core dump (currently exit 134, SIGABRT).

*Resolved (documented + guarded).* Decompiled `X11PlatformLifetimeEvents..ctor` (12.1.1): it calls
`IceAddConnectionWatch` unconditionally, `EnableSessionManagement` is read only later, so the
dependency cannot be avoided from nfty. `Program.Main` now catches the `DllNotFoundException` for the
listed X11 libraries and prints which library is missing and the apt/dnf/pacman line, exit 1 (shown
in WSL without the libs). Install page, README and READ-ME-FIRST list
`libice6 libsm6 libx11-6 libfontconfig1`.

## Should-fix

### S1. State lives beside the executable; unwritable on a real install
`IStateStore` order: `.nfty` beside exe, then cwd, then Kitchen, then memory. Under `/usr/lib/nfty` or `/opt/nfty` (root-owned) beside-exe is unwritable, so state lands in whatever cwd the launcher had (often `$HOME` or `/`) or memory-only (recents lost). The portable tarball in a user dir works as designed. Fix: add an XDG step on Linux (`$XDG_CONFIG_HOME/nfty` or `~/.config/nfty`) between beside-exe and cwd, only when beside-exe is not writable and nothing exists. Needed before any .deb/AppImage; AppImage mounts read-only so it is mandatory there.

*Resolved.* `StateStore` gained the rule straight after beside-exe, Linux/FreeBSD only:
`UserConfigFolder` = `$XDG_CONFIG_HOME/nfty` (absolute values only, per the spec) else
`~/.config/nfty`. Every state file (recents, palette, settings.json, editor view, viewer windows,
publishing) already went through the one `IStateStore`, so nothing else moved. The rule is wired only
by `StateStore.ForThisMachine`; the constructor's parameter defaults to null so no test can reach a
real `~/.config`. Beside-exe stays first so a portable copy is still self-contained; Windows order is
unchanged. `UserConfigFolderTests` drive it with injected env + temp dirs; in WSL a `chmod -R a-w`
install launched from `/` wrote `recents.json` to `$XDG_CONFIG_HOME/nfty` and nothing beside the exe
or in cwd.

### S2. No window/taskbar icon on Linux
`ApplicationIcon` (`nfty.ico`) is a Windows PE resource; neither `MainWindow.axaml` nor `ViewerWindow.axaml` sets `Icon`, and the window uses `SystemDecorations=None` custom chrome. Linux shows a generic icon. Fix: ship a PNG set (e.g. 256/48), set `Icon="/Assets/nfty.png"` on both windows (or `Window.Icon` from `BrandMark` render), and `.desktop` file (below). Not verified in a compositor with a real taskbar (WSLg wrapper shows its own).

*Resolved.* `tools/icons/make-app-icon.py` also writes `src/Nfty.Desktop/Assets/nfty.png` (its 256
frame; `nfty.ico` regenerated byte-identical), embedded as an Avalonia resource; `AppIcon.Apply` (from
`ChromeWiring.Attach`, so all three windows) sets `Window.Icon` off Windows only, Windows keeps the PE
icon. Read back over X11 in WSLg: the `nfty` window has `_NET_WM_ICON` 128x128, `WM_CLASS`
`Nfty.Desktop`. The tarball ships `dev.vaporsoft.nfty.desktop` (`StartupWMClass=Nfty.Desktop`),
`nfty.png` and `install-desktop-entry.sh` (writes to `~/.local/share` only). A real desktop's taskbar
is still unseen.

### S3. Secret store untested on a real keyring
`SecretToolStore` shells out to `secret-tool` (stdin for the secret, good). In WSL: `which secret-tool` -> absent, no gnome-keyring/kwallet daemon, so I could only exercise the "unavailable" path by reading the code (`UnavailableReason` text is accurate: "install libsecret-tools... make sure a keyring is running"). Store/lookup/clear round trip never run. Do on a GNOME/KDE box: `nfty ipfs`/Settings -> save Pinata key, restart, read back; check behavior with keyring locked. Note `secret-tool` is a separate package (`libsecret-tools`) not installed by default on many distros; consider listing as `Recommends`.

*Partly resolved.* `SecretToolStoreTests` runs the store against a stand-in `secret-tool` script
(Linux/macOS only) that logs argv and captures stdin: the secret arrives on stdin byte for byte,
appears in no argument, and store/lookup/clear round-trip. Install docs and READ-ME name
`libsecret-tools`. Still open: the same round trip against a real GNOME Keyring/KWallet, and a
locked keyring.

### S4. File dialogs untested
`DesktopFilePicker` uses Avalonia `StorageProvider` (DBus xdg-desktop-portal when available, else Avalonia's managed fallback). WSLg has no portal, I could not drive a native dialog from here (no clicking into WSLg window). Confirm on GNOME/KDE with portal and on a bare WM. `xdg-open` for Reveal is guarded and best-effort (fine).

*Partly verified.* Driven from the Windows side (SetCursorPos/SendKeys into the WSLg window): the
Export card's folder picker opened as a GTK chooser titled "Export to folder", navigated and returned
the folder; the export landed. A portal-backed dialog on GNOME/KDE is still unseen.

### S5. CI has no Linux GUI-test job
CI runs App tests only on `windows-latest`; I ran all 1871 on Linux headless, green. Add `tests/Nfty.App.Tests` to the ubuntu leg of `core-cross-platform` (cost ~4 min). Also CI and WSL pin: `global.json` wants 10.0.301 with `latestFeature` roll-forward; Ubuntu's packaged SDK is 10.0.109, so `dotnet build` from a distro SDK fails ("Install the [10.0.301] .NET SDK"). Contributors on Linux need `dotnet-install.sh`; building the release from Windows for `-r linux-x64` works with no Linux toolchain.

*Resolved.* CI job `app-linux` (ubuntu-latest) runs `Nfty.App.Tests` and `Nfty.Ticket.Tests`.
`global.json` is now `10.0.100` + `latestFeature`: any 10.0 SDK at or above the 1xx band builds,
rolling to the newest installed (Windows keeps resolving 10.0.301; `setup-dotnet` installs the latest
10.0 for `latestFeature`). WSL with Ubuntu's packaged 10.0.114 resolved it and built the solution with
0 warnings.

### S6 resolved
READ-ME-FIRST on Linux says `dotnet-runtime-10.0` and links the Linux install page; no "DESKTOP".

### S6. Framework-dependent Linux blurb
`dotnet-runtime-10.0` is enough on Linux (no WindowsDesktop runtime exists). Tell users that in READ-ME-FIRST.

## Nice-to-have

- `.desktop` file + MIME registration for `.cbk .rcp .igt .set .ktn .tin .mnu` (`application/zip` subtype globs) so double-click opens (`StartupPath.FromArguments` already handles argv).
- Packaging recommendation: **tar.gz of the portable (self-contained, multi-file) build first** (what `build.py` already shapes), then **AppImage** (single download, FUSE-less `--appimage-extract-and-run` fallback; needs S1) or **Flatpak** (portal file dialogs for free, sandbox complicates `.nfty` beside exe and `secret-tool`, would use libsecret over DBus). `.deb` last (Depends from B2). Single-file self-contained works too (extracts to `~/.net/<name>/<hash>`, tested) but is 99 MB and slow first launch; fine as secondary.
- Window: custom chrome (`SystemDecorations=None`) renders and has own controls; on Wayland compositors with SSD-only policy there is no native resize border — rely on the existing corner grip; test under GNOME-Wayland and KDE.
- `Path.GetTempPath()` demo/extract dirs: fine on Linux (`/tmp`); sealed/exports stage in `/tmp` too — large exports on tmpfs may hit RAM limits; honor `TMPDIR` (already automatic).
- Console encoding: CLI prints `→` ("Generated 3 → out"); correct on UTF-8 Linux terminals (mojibake seen only through the Windows-side `wsl.exe` pipe).
- ImageSharp pinned 3.1.11 carries 8 advisories now (NU1902/NU1903, e.g. GHSA-v76p-62qx-wwq2). Platform-independent and per CLAUDE.md deliberate; noted because a public Linux release (and distro packagers) will see them.
- arm64: publish works; run on a real arm64 board/VM before listing it (Skia arm64 `.so` present). Add `linux-arm64` to the release matrix only after that.

## Not tested, and why

- Real desktop environment (GNOME/KDE): only WSLg available; no portal, no keyring, no taskbar.
- `secret-tool` round trip (no libsecret-tools, no keyring daemon; installing was out of scope).
- Native file dialogs, drag-and-drop from a file manager, window icon, Wayland vs X11 differences.
- IPFS against a running Kubo (none reachable; only the "not reachable" path).
- linux-arm64 execution.
- Skipped tests (1 in Core, 2 in App) are permission/read-only-file cases; not investigated.

## Proposed build.py changes (small; not applied)

```diff
+import tarfile
 ...
 RID = sys.argv[2] if len(sys.argv) > 2 else 'win-x64'
+LINUX = RID.startswith('linux-')
+EXE = '' if LINUX else '.exe'
+NEWLINE = '\n' if LINUX else '\r\n'
 ...
 def unpack_demo(folder):
-    cli = os.path.join(folder, 'cli', 'Nfty.Cli.exe' if os.name == 'nt' else 'Nfty.Cli')
-    if os.path.exists(cli):
+    cli = os.path.join(folder, 'cli', 'Nfty.Cli' + EXE)
+    # Runnable only when the artifact's OS is this OS (a Windows host cannot run a Linux CLI;
+    # build the Linux set from WSL/Linux to get the real extraction check).
+    if os.path.exists(cli) and (LINUX == (os.name != 'nt')):
 ...
-    io.open(..., newline='\r\n')
+    io.open(..., newline=NEWLINE)
 ...
-    archive = folder + '.zip'
-    ... zipfile ...
+    if LINUX:
+        archive = folder + '.tar.gz'
+        def mode(i):
+            i.uid = i.gid = 0; i.uname = i.gname = ''
+            i.mode = 0o755 if i.isdir() or os.path.basename(i.name) in ('Nfty.Desktop', 'Nfty.Cli') else 0o644
+            return i
+        with tarfile.open(archive, 'w:gz') as t:
+            t.add(folder, arcname=os.path.basename(folder), filter=mode)
+    else:
+        archive = folder + '.zip'   # unchanged zip code
 ...
 COMMON: 'Nfty.Desktop.exe' -> 'Nfty.Desktop%s', 'cli/Nfty.Cli.exe' -> 'cli/Nfty.Cli%s' (pass EXE twice);
 framework blurbs: on LINUX say "dotnet-runtime-10.0 (https://learn.microsoft.com/dotnet/core/install/linux)"
 and list: libice6 libsm6 libx11-6 libfontconfig1
```
`COMMON` also needs the `.nfty` paragraph reworded once S1 lands. Also add `Nfty.Desktop`/`Nfty.Cli` chmod when the host is Linux (publish apphost already is +x there; Windows-published ones are not, so the tar filter above is the reliable place). Release README: add a Linux install section (extract, `chmod` not needed, deps line, `./Nfty.Desktop`).

## Checklist to ship Linux x64

1. B1 build.py changes; build from Windows (`python tools/release/build.py <ver> linux-x64`) and smoke from WSL (`tar xzf`, `./cli/Nfty.Cli demo`, launch).
2. B2 document/guard libice6+libsm6.
3. S1 XDG state dir, S2 PNG icon, S5 add ubuntu App.Tests job.
4. Real-desktop pass for S3/S4.
