# Install nfty

nfty is not released yet, so there is no installer. You build it once, and then run it whenever you
like.

## What you need

**The .NET 10 SDK.** Download it from [dotnet.microsoft.com](https://dotnet.microsoft.com/) and
install it. Everything else nfty needs is fetched automatically the first time you build.

nfty runs on Windows, macOS and Linux.

## Build it

Open a terminal in the folder you cloned nfty into, and run:

```bash
dotnet build nfty.sln
```

The first build takes a minute or two while packages download. Later builds take seconds.

## Open the app

```bash
dotnet run --project src/Nfty.Desktop
```

You should get this:

![The nfty opening screen, with nothing open yet](../images/landing-light.png#only-light)
![The nfty opening screen, with nothing open yet](../images/landing-dark.png#only-dark)

If you see that window, you are done. Leave the terminal open — closing it closes the app.

!!! tip "There is a command line too"

    The same engine runs headless, which is useful for scripting and for checking a project before
    you cook it:

    ```bash
    dotnet run --project src/Nfty.Cli -- --help
    ```

    You do not need it to follow this manual. See [Command line](../reference/cli.md) when you want
    it.

---

**Next:** [The demo CookBook →](the-demo.md)
