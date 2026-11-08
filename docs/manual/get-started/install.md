# Install nfty

Download a prebuilt copy for Windows or Linux, or build nfty from source on any platform. Every
download contains both the desktop app and the command line.

## Requirements

| | Needed |
|---|---|
| Operating system | Windows 10 or 11 (x64), or Linux (x64) for a download. macOS builds from source. |
| Window space | 1280×720 logical units or larger. nfty opens at this minimum; display scaling affects the physical screen space required. |
| .NET 10 runtime | Only for the two *framework-dependent* downloads. The others carry their own runtime. |
| Linux libraries | The X11 session libraries, listed [below](#linux). The command line needs none. |

## Choose a download

Downloads are on the [releases page](https://github.com/corderro-artz/nfty/releases/latest). Each
comes in four shapes:

| Shape | Needs .NET 10 | Choose it when |
|---|:--:|---|
| **portable** | No | You want to unpack and run. **Start here if unsure.** |
| **single-file** | No | You want one executable. It unpacks itself, so the first launch is slower. |
| **framework-dependent** | Yes | You already have the .NET 10 runtime and want a small folder. |
| **single-file-net10** | Yes | One small executable, with the runtime left out. |

## Windows

1. Download the `.zip` for your shape and unzip it anywhere you can write to.
2. Run `Nfty.Desktop.exe`.
3. The command line is `cli\Nfty.Cli.exe` in the same folder. Run `cli\Nfty.Cli.exe --help` to check it.

The framework-dependent shapes need the ordinary **.NET 10 Runtime**. The Windows Desktop Runtime
also supplies it, but nfty does not require WPF or Windows Forms.

nfty installs nothing and writes nothing to the registry on its own. To make double-clicking a
`.cbk`, `.set` or picture open nfty, turn it on in Settings: see
[Open files with nfty on Windows](../app/file-associations.md).

## Linux

1. Download `nfty-<version>-linux-x64-<shape>.tar.gz` and unpack it with `tar`, which keeps the
   executable bits:

    ```bash
    tar -xzf nfty-<version>-linux-x64-portable.tar.gz
    cd nfty-<version>-linux-x64-portable
    ./Nfty.Desktop            # the app
    ./cli/Nfty.Cli --help     # the command line
    ```

2. If the app reports a missing library, install the X11 and font libraries. Desktop distributions
   commonly include them; minimal or server installations may need them added:

    | Distribution | Command |
    |---|---|
    | Debian, Ubuntu | `sudo apt install libice6 libsm6 libx11-6 libfontconfig1 libxrandr2 libxi6 libxcursor1` |
    | Fedora | `sudo dnf install libICE libSM libX11 fontconfig libXrandr libXi libXcursor` |
    | Arch | `sudo pacman -S libice libsm libx11 fontconfig libxrandr libxi libxcursor` |

3. Optional: run `./install-desktop-entry.sh` once to add nfty to your application menu with its
   icon. It writes under `$XDG_DATA_HOME` (`~/.local/share` by default), in `applications/` and
   `icons/`, and points at the folder you ran it from, so run it again if you move the folder.

The app runs under X11, and under Wayland through XWayland. The framework-dependent shapes need the
.NET 10 runtime (`dotnet-runtime-10.0`, see
[Install .NET on Linux](https://learn.microsoft.com/dotnet/core/install/linux)).

To save a Pinata key for uploads, nfty uses your keyring through `secret-tool`: install
`libsecret-tools` (Debian, Ubuntu) or `libsecret` (Fedora, Arch) with GNOME Keyring or KWallet
running. Without it nfty saves no key and says so; it never writes one to a file.

## Build from source (any platform)

You need the [.NET 10 SDK](https://dotnet.microsoft.com/) (on Linux, `dotnet-sdk-10.0` from your
distribution works). In the folder you cloned nfty into:

```bash
dotnet build nfty.sln
dotnet run --project src/Nfty.Desktop        # the app
dotnet run --project src/Nfty.Cli -- --help  # the command line
```

The first build takes a minute or two while packages download.

## Check that it works

Start the app. You should see the opening screen:

![The opening screen: create and open actions on the left, recent books on the right, the Kitchen shelf along the bottom](../images/landing-light.png#only-light)
![The opening screen: create and open actions on the left, recent books on the right, the Kitchen shelf along the bottom](../images/landing-dark.png#only-dark)

From a terminal, `Nfty.Cli --version` prints the version.

!!! tip "Call it `nfty`"

    This manual writes the command line as `nfty`. Make that work with an alias, for example
    `alias nfty="$HOME/nfty/cli/Nfty.Cli"` in your shell profile, or on Windows
    `Set-Alias nfty C:\Tools\nfty\cli\Nfty.Cli.exe` in your PowerShell profile. From a source
    checkout, `nfty` means `dotnet run --project src/Nfty.Cli --`.

## Where nfty keeps its own files

Your CookBooks and Sets are wherever you save them. nfty's own state (recent files, saved palettes,
settings) lives in a `.nfty` folder beside the program when that folder is writable, so an unpacked
copy keeps its history to itself. If it is not writable, nfty falls back (on Linux, first to
`~/.config/nfty` or `$XDG_CONFIG_HOME/nfty`), and when nothing on disk is usable it keeps state for
the session only. **Settings → DATA** shows which. See
[Settings files and environment variables](../reference/settings.md).

## Next step

[Open the demo CookBook →](the-demo.md)
