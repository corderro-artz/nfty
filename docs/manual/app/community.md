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

Nothing else leaves your machine. The CookBook is only included if you tick **Include source CookBook**
on the Share card. A [sealed](../concepts/sealing.md) `.tin` can't be shared. Your GitHub name appears
beside every share.

## Why it runs on GitHub

The gallery is a repository of small listing files, and every Set lives in its author's own
`nfty-public` repository. Nobody runs a server and nobody pays for storage: each Set uses your own GitHub
allowance, which is why the limits below exist. They keep nfty a polite user of a free service.

## Browse and search

1. Press **Browse the community gallery** on the [opening screen](opening-screen.md). The list is
   fetched once and kept, so the next visit opens at once, even offline. A status line under the title
   says when it was fetched (`Updated 14:02`) and how many collections are new or updated.
2. Page through the tiles. **SORT** offers **Updated** (newest change first, the default), **Name** and
   **Assets**. The **TAG** tray shows the most-used tags as chips; **More tags** lists the rest.
3. Type in the search box (`#tag, @user or a name`). Three kinds of word combine, and a tile must match
   all of them:

    | You type | Matches |
    |---|---|
    | `#pixel` | Shares carrying the tag `pixel`. |
    | `@someone` | Shares owned by that GitHub account. |
    | `chest` | Shares whose name or collection name contains the word. |

    Clicking a tag chip, a tag, or an owner's name on a tile just edits the same text. **Clear search**
    empties it.
4. Click a tile to see its details on the right: preview, owner, dates, assets, canvas, tags, description,
   and whether a CookBook or 3D models are included. **Refresh** fetches the list again.

Each tile carries one mark, never two: **New** for a collection shared since your first visit,
**Updated** for one changed since you last selected it, **Pending** for a share of yours the gallery has
not listed yet. **Mark all seen** clears the New and Updated marks; selecting a tile clears its own. Your
first visit marks nothing, since every collection would be new.

### When the page is empty

The page says which kind of empty it is:

| Message | Means |
|---|---|
| `No collection matches “…”.` | Your search matches nothing. **Clear search** undoes it. |
| `Nobody has shared a collection yet.` | The gallery list is empty. |
| `The list hasn't been fetched yet, and GitHub can't be reached.` | First visit with no network. Press **Refresh** when you are back online. |

When GitHub can't be reached but a saved list exists, the page shows it and says so:
`GitHub can't be reached. Showing the list from 14:02.`

## Download a Set

1. Select a tile and press **Download**. **Stop** cancels it.
2. nfty checks the file against the listing's fingerprint, then opens it in the Set browser. A file
   that doesn't match is not opened: `The download doesn't match what the gallery lists, so it was not
   opened.`

A downloaded Set is an ordinary `.set` on your machine. **Report** opens the gallery's report form for
the selected collection in your browser; only the collection's id is sent. Use it for a share that
breaks the gallery's rules.

## Share a Set

The steps are in [Share a Set to the community gallery](../publishing/share-to-the-gallery.md). In
short: connect GitHub once in [Settings](settings.md#publishing), press **Share…** in the
[Set browser](set-browser.md), and the card shows what leaves your machine and checks the limits before
anything uploads.

### The Share card

The left column, **WHAT LEAVES THIS MACHINE**, lists the contents (the **Include source CookBook** and
**3D models** boxes, then the exact files and size) and the **LIMITS** rows: `Canvas 32 x 32 of 512 x 512`,
the asset count, `Size 4.0 MB of 200 MB`, and `2 of 3 shared`. A row past its limit turns to warning ink and
names the fix. The right column is the listing: **NAME**, **DESCRIPTION**, **TAGS**, and the
account chip (`Connected as @you`, or **Connect GitHub…**). Between them, one reserved block shows
whichever of these applies:

| The block shows | When | What you do |
|---|---|---|
| Nothing to decide | A new share, under the limit, `nfty-public` clean. | Press **Share**. |
| `Replace one to share this. Nothing is removed until you press Share.` | You already share 3 collections. | Pick which of your three to give up; Share stays dim until you do. |
| `You already share this collection.` with **Replace the shared version** | The Set is a collection you shared before. | Tick the box to update the listing; an update is never a side effect of pressing Share. |
| `Nothing changed since you shared "…" on …` | The Set matches what you shared. | Nothing to send; change the Set first. |
| `This Set is already in the gallery, shared by @…` | Someone else already shares this Set. | Refused; there is nothing to choose. |
| `Your nfty-public holds things the gallery doesn't allow. Remove them to share.` with **Remove these…** | The repository has extras. | See below. |

While it sends, the card names each stage (`Reading your nfty-public…`, `Packing the Set…`,
`Checking the pack…`, `Making the preview…`, `Uploading the files…`, `Writing your listing…`,
`Opening the pull request…`). **Stop** takes back what this attempt made on GitHub; nothing already in
the gallery changes. It ends on `Submitted. The gallery checks it now; it usually lists within a few
minutes.` Until the gallery lists it, your tile reads **Pending**. If the gallery turns the request away,
the tile reads **Closed**, the rail shows the reasons, and **Dismiss** forgets the request.

### Update or replace a share

Sharing the same collection again **updates** it: tick **Replace the shared version**, and the new
`.set` replaces the old file while the listing keeps its place. At three shares the card offers the
**replace one** choice instead: pick the share to give up, and the swap goes in one request, so the
gallery never shows a gap you didn't choose.

### Unshare

Select your own tile and press **Unshare**, then confirm. nfty asks the gallery to remove the listing,
which drops from the list within a few minutes. The repository is never deleted by nfty.

### When your `nfty-public` holds other things

The repository is for nfty's files only. Before every share nfty scans it; anything beyond
`README.md`, `nfty-public.json`, the releases of your shares and their files is listed on the card, one
row each, and Share stays dim. **Remove these…** names every item again, warns that removal from GitHub
can't be undone from nfty, and deletes only after you confirm. Something GitHub's API can't delete, such
as a submodule, is listed without the button; remove it on github.com. A repository named `nfty-public`
that nfty didn't make, or a private one, is refused rather than adopted; see
[Troubleshooting](../help/troubleshooting.md#community-gallery).

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
| Names and descriptions | no links |

The same check runs in three places: the **Share…** card, the [`nfty share check`](../reference/cli.md#share-check)
command, and the Action that guards the gallery, so a Set that passes on your machine passes there.

## Next steps

- [Share a Set to the community gallery](../publishing/share-to-the-gallery.md), step by step.
- [Connect GitHub](settings.md#publishing) in Settings.
- [Gallery problems](../help/troubleshooting.md#community-gallery) and the [FAQ](../help/faq.md).
- [Export a Set for a marketplace](../publishing/export.md) instead of the gallery.
- [Open a downloaded Set](set-browser.md).
