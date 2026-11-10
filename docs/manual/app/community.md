# The community gallery

Browse Sets other people shared, download one to open in the [Set browser](set-browser.md), and share
your own, all from inside nfty.

**Before you start:** browsing and downloading need nothing but a network connection, no account and no
sign-in. Sharing needs a GitHub account at least 30 days old.

## What is public

Sharing puts three things on GitHub, all under your own account:

- the packed `.set` (the images, metadata and rarity of the collection), as a Release file in a
  repository named `nfty-public` that nfty creates for you;
- a small preview picture, in the same Release;
- a listing, `nfty-public.json`, in that repository, plus an entry in the gallery's index so others can find it.

Nothing else leaves your machine. The CookBook is only included if you included it when you exported. A
[sealed](../concepts/sealing.md) `.tin` can't be shared. Your GitHub name appears beside every share.

## Why it runs on GitHub

The gallery is a repository of small listing files, and every Set lives in its author's own
`nfty-public` repository. Nobody runs a server and nobody pays for storage: each Set uses your own GitHub
allowance, which is why the limits below exist. They keep nfty a polite user of a free service.

## Browse and search

1. Press **Browse the community gallery** on the [opening screen](opening-screen.md). The list is
   fetched once and kept, so the next visit opens at once, even offline.
2. Page through the tiles, sort by **Updated**, **Name** or **Assets**, and filter by **TAG**.
   **More tags** lists every tag in use.
3. Type in the search box. Three kinds of word combine, and a tile must match all of them:

    | You type | Matches |
    |---|---|
    | `#pixel` | Shares carrying the tag `pixel`. |
    | `@someone` | Shares owned by that GitHub account. |
    | `chest` | Shares whose name or collection name contains the word. |

    Clicking a tag, or an owner's name on a tile, just edits the same text. **Clear search** empties it.
4. Click a tile to see its details on the right: owner, dates, assets, canvas, tags, description.

Collections that are new or updated since your last visit are marked; **Mark all seen** clears the marks.

## Download a Set

1. Select a tile and press **Download**. **Stop** cancels it.
2. nfty checks the file against the listing's fingerprint, then opens it in the Set browser.

A downloaded Set is an ordinary `.set` on your machine. **Report** sends the gallery a note about a
share that breaks its rules.

## Share a Set

**Before you start:** [cook](cooking.md) the Set and open it in the Set browser. Connect GitHub once in
**Settings → PUBLISHING → GITHUB** (**Connect GitHub…** shows a code to type on github.com; nfty never sees
your password, and the token lives in your system's credential store).

1. Press **Share…** in the Set browser header.
2. nfty reads your `nfty-public` repository, and checks the Set against the limits. Fix anything it
   names; the **Share** button stays dim until the list is empty.
3. Give the share a name, a description (up to 500 characters) and up to five tags.
4. Press **Share**. nfty packs the Set, makes the preview, uploads both and opens a pull request on the
   gallery with your listing.
5. The gallery checks the listing by itself, usually within a few minutes, then merges it. Until then
   the tile shows as pending.

**Result:** your Set is listed under your name, and the tile on your own machine reads **Unshare**
instead of **Download**.

### Update or replace a share

Sharing the same collection again **updates** it: the new `.set` replaces the old file and the listing
keeps its place. At three shares the card says so and offers a **replace one** choice: pick the share to
give up, and nothing is removed until you press **Share**.

### Unshare

Select your own tile and press **Unshare**. nfty removes the listing, which the gallery drops from the
list within a few minutes. The repository is never deleted, and the Release files stay until you
remove them on GitHub.

### When your `nfty-public` holds other things

The repository is for nfty's files only. If the scan finds anything else (extra files, branches,
releases or release files) the card lists each and refuses to share. **Remove these…** deletes the
ones nfty can delete through GitHub (after asking you to confirm) and rescans. Something GitHub's API
can't delete, such as a submodule, is listed without the button; remove it on github.com.

## Limits

| Limit | Value |
|---|---|
| Canvas | up to 512 × 512 pixels |
| Assets in one Set | up to 1,000 |
| Packed `.set` | up to 200 MB |
| Shares per account | 3 |
| GitHub account age | at least 30 days |
| New shares | 1 per day (updates don't count) |
| Name / description / tags | 64 / 500 characters / 5 tags |

The same check runs in three places: the **Share…** card, the [`nfty share check`](../reference/cli.md#share-check)
command, and the Action that guards the gallery, so a Set that passes on your machine passes there.

## Next steps

- [Export a Set for a marketplace](../publishing/export.md) instead of the gallery.
- [Open a downloaded Set](set-browser.md).
