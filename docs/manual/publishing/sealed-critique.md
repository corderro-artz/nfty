# Send a sealed copy for critique

A **sealed export** (`.tin`) is your whole Set encrypted under a passphrase and marked view-only. Your
reviewer opens it in nfty with the passphrase, browses the art and reads the rarity table, and every
save and export is refused. Read [What sealing does and does not do](../concepts/sealing.md) before you
rely on it: the encryption is real against anyone without the passphrase, but the view-only mark is
nfty declining, not arithmetic forbidding.

## In the app

1. Open the Set, press **Export…** and choose **Sealed critique** (or tick **Seal this export** on the
   **SEAL** tab of any preset).
2. Type a **PASSPHRASE** of at least 12 characters, and again under **CONFIRM**.
3. Optionally write a **NOTE TO THE RECIPIENT**. It travels unencrypted, so they can read it before
   they have the passphrase.
4. Press **Export**. You get `<collection>.tin`.

## From the command line

```bash
NFTY_KEY='a passphrase you chose' \
nfty export ./collection --out ./dist --preset sealedcritique \
    --key env:NFTY_KEY --note "Draft for review"
```

`--key` names where the passphrase comes from: `env:NAME`, `file:PATH`, `stdin` or `prompt` (the
default, asking at the terminal without echo). There is deliberately no `--passphrase`: an argument is
visible to every process, lands in shell history and is captured by CI logs. See
[Where a secret comes from](../reference/cli.md#where-a-secret-comes-from).

## Rules worth following

- **Send the passphrase by a different route than the file.**
- **Keep the original.** Sealing copies your Set. There is no `unseal` command and no recovery for a
  lost passphrase.
- A sealed export is always one file, and an export without `set.json` cannot be sealed.

## Opening one

The recipient uses **Open a cooked .set…** (it takes `.tin`), double-clicks it if
[file types are registered](../app/file-associations.md), or runs `nfty inspect file.tin --key prompt`.
nfty first shows the collection's name, asset count and your note, then asks for the passphrase. A
wrong passphrase and an altered file fail identically, and nfty says it could be either.
