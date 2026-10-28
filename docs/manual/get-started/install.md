# Install nfty

nfty runs on Windows, macOS and Linux. Prebuilt Windows downloads are on the
[releases page](https://github.com/corderro-artz/nfty/releases/latest); on other systems, build from
source.

## What you need

- **Windows, macOS or Linux.** Prebuilt downloads are for Windows; elsewhere, build from source.
- **A screen of 1280×720 or larger.** That is the smallest window nfty will open at. It opens at
  1366×768 and remembers nothing about size, so a small display is the only case where it matters.
- **.NET 10** only for the two framework-dependent downloads. The other two carry their own runtime.

## Download it

Every download holds both the app and the command line, the demo CookBook and the licenses.

| Download | .NET 10 needed | Choose it when |
|---|---|---|
| **portable** | No | You want to unzip and double-click. Start here if unsure. |
| **single-file** | No | You want one `.exe`. It unpacks itself, so the first launch is slower. |
| **framework-dependent** | Yes | You already have the .NET 10 desktop runtime and want a small folder. |
| **single-file-net10** | Yes | One small `.exe`, with the runtime left out. |

Unzip anywhere and run `Nfty.Desktop.exe`. The command line is in the `cli` folder.

## Or build it

You need the **.NET 10 SDK** from [dotnet.microsoft.com](https://dotnet.microsoft.com/). Open a
terminal in the folder you cloned nfty into:

```bash
dotnet build nfty.sln
dotnet run --project src/Nfty.Desktop
```

The first build takes a minute or two while packages download.

You should get this window:

![the opening screen with nothing open: create and open actions, recents, Kitchen shelf](../images/landing-light.png#only-light)
![the opening screen with nothing open: create and open actions, recents, Kitchen shelf](../images/landing-dark.png#only-dark)

If you see it, you are done.

!!! tip "There is a command line too"

    The same engine runs headless, for scripting and for checking a project before you cook it:

    ```bash
    dotnet run --project src/Nfty.Cli -- --help
    ```

    You do not need it to follow this manual. See [Command line](../reference/cli.md).

---

**Next:** [The demo CookBook →](the-demo.md)
