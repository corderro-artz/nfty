# Install nfty

nfty runs on Windows, macOS and Linux. Prebuilt Windows and Linux (x64) downloads are on the
[releases page](https://github.com/corderro-artz/nfty/releases/latest); on macOS, build from source.

## What you need

- **Windows or Linux** for a prebuilt download; macOS builds from source.
- **A screen of 1280×720 or larger.** That is the smallest window nfty will open at. It opens at
  1366×768 and remembers nothing about size, so a small display is the only case where it matters.
- **.NET 10** only for the two framework-dependent downloads. The other two carry their own runtime.

## Download it

Every download holds both the app and the command line, the demo CookBook and the licenses.

| Download | .NET 10 needed | Choose it when |
|---|---|---|
| **portable** | No | You want to unpack and run. Start here if unsure. |
| **single-file** | No | You want one executable. It unpacks itself, so the first launch is slower. |
| **framework-dependent** | Yes | You already have the .NET 10 runtime and want a small folder. |
| **single-file-net10** | Yes | One small executable, with the runtime left out. |

### Windows

Downloads end in `.zip`. Unzip anywhere and run `Nfty.Desktop.exe`. The command line is in the
`cli` folder. The framework-dependent shapes need the .NET 10 **desktop** runtime.

### Linux

Downloads end in `.tar.gz`, named `nfty-<version>-linux-x64-<shape>.tar.gz`. Unpack with `tar`,
which keeps the executable bits:

```bash
tar -xzf nfty-<version>-linux-x64-portable.tar.gz
cd nfty-<version>-linux-x64-portable
./Nfty.Desktop            # the app
./cli/Nfty.Cli --help     # the command line
```

The app needs the X11 session libraries. Every desktop install (GNOME, KDE, Xfce) already has them;
a minimal or server install may not, and the app then says which one is missing and stops:

| Distribution | Command |
|---|---|
| Debian, Ubuntu | `sudo apt install libice6 libsm6 libx11-6 libfontconfig1` |
| Fedora | `sudo dnf install libICE libSM libX11 fontconfig` |
| Arch | `sudo pacman -S libice libsm libx11 fontconfig` |

It runs under X11, and under Wayland through XWayland. The command line needs none of these
libraries. The framework-dependent shapes need the .NET 10 runtime (`dotnet-runtime-10.0`; see
[Install .NET on Linux](https://learn.microsoft.com/dotnet/core/install/linux)); there is no separate
desktop runtime on Linux.

**A launcher entry.** The folder carries `dev.vaporsoft.nfty.desktop`, `nfty.png` and `install-desktop-entry.sh`.
Run the script once and nfty appears in your desktop's application menu with its icon; it writes to
`~/.local/share/applications` and `~/.local/share/icons` only, and points at the folder you ran it
from, so run it again if you move the folder.

**Where settings live.** In a `.nfty` folder beside `Nfty.Desktop` when that folder is writable, so
an unpacked copy keeps its recent files and palette to itself. Unpacked somewhere you cannot write
(`/opt`, `/usr`, a read-only mount), nfty uses `~/.config/nfty` (or `$XDG_CONFIG_HOME/nfty`) instead.
Settings → Data says which one it is using.

**Saving a Pinata key** (Settings → Publishing) uses your keyring through `secret-tool`: install
`libsecret-tools` (Debian, Ubuntu) or `libsecret` (Fedora, Arch), with GNOME Keyring or KWallet
running. Without it nfty says so and saves no key; it never writes one to a file.

## Or build it

You need the **.NET 10 SDK** from [dotnet.microsoft.com](https://dotnet.microsoft.com/) (on Linux,
your distribution's `dotnet-sdk-10.0` package works). Open a terminal in the folder you cloned nfty
into:

```bash
dotnet build nfty.sln
dotnet run --project src/Nfty.Desktop
```

The first build takes a minute or two while packages download.

You should get this window:

![The opening screen: create and open actions on the left, recent books on the right, the Kitchen shelf along the bottom](../images/landing-light.png#only-light)
![The opening screen: create and open actions on the left, recent books on the right, the Kitchen shelf along the bottom](../images/landing-dark.png#only-dark)

If you see it, you are done.

!!! tip "There is a command line too"

    The same engine runs headless, for scripting and for checking a project before you cook it:

    ```bash
    dotnet run --project src/Nfty.Cli -- --help
    ```

    You do not need it to follow this manual. See [Command line](../reference/cli.md).

---

**Next:** [The demo CookBook →](the-demo.md)
