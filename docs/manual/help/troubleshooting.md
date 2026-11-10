# Troubleshooting

What nfty's messages mean and what to do about them. nfty reports problems in plain sentences, never a
stack trace; on the command line, add `--verbose` to see the trace as well.

## Installing and starting

### The app does not start on Linux and names a missing library

A minimal or server install lacks the X11 session libraries. Install them; see
[Install nfty: Linux](../get-started/install.md#linux). The command line needs none of them.

### The window is cut off on a small screen

nfty needs at least 1280×720. Below that, some screens cannot fit.

### Double-clicking a file does not open nfty (Windows)

Register the file types: **Settings → SYSTEM → Apply**. For `.png`, `.jpg`, `.json` and models, also
press **Default apps…** and choose nfty there; Windows lets only you make a program the default for
those. If the chip reads **needs repair**, the app was moved; press **Repair**. See
[Open files with nfty on Windows](../app/file-associations.md).

## Building a book

### "This image is 256x256; the canvas is 512x512."

Every drawing in a book shares one canvas size, and nfty never resizes art. Export the picture at the
right size. See [Why nfty never resizes your art](../concepts/no-resizing.md).

### "Recipe X has two ingredients named Y."

A layer's name becomes the published `trait_type`, so two layers with one name would merge into one
trait and the collection would ship percentages above 100%. Rename one. `Type` is reserved for the
Recipe; a layer named like another layer's color trait (`Body Color` beside `Body`) is refused for the
same reason, and so is a variant named `None` on an optional layer.

### Color spec "is missing a prefix"

Colors need `hex:`, `rgb:`, `hsl:` or `hsv:`. See [Color specs](../reference/color-specs.md).

### "Unsupported schema version"

The file was written by a newer build of nfty than yours (update nfty), or it is a version 1 archive,
which this build no longer reads.

### Save is unavailable in the Ingredient Editor or Rise page

The book is locked. Unlock editing in the Explorer by clicking the **read-only** chip. The footer says
which reason applies.

### The validity report lists several problems at once

That is deliberate: every problem is reported, not just the first, so you can fix them in one pass. A
brand-new, empty book reports what to add first; that is not a fault.

## Cooking

### "...allows exactly N unique DNA, but M were requested."

N is the book's exact DNA count. Compare the requested count and any existing assets with that
space. A request beyond the remaining space needs fewer assets, a larger space or repeats; when
enough unused DNA should remain, sampling may instead have reached its retry budget. See
[If the book cannot make enough](../app/cooking.md#if-the-book-cannot-make-enough).

### A rule conflict

The Recipe's rules exclude *every* combination, so the space is empty. Unlike the message above, a
bigger space will not help. Remove or loosen a rule. See [Rules](../app/rules.md).

### "The reroll budget ran out"

The attempts did not find another acceptable roll. Read the count or bound in the message and
review the requested count, weights and rules. A larger `--max-rerolls` gives sampling more chances
when legal unused DNA remains; it cannot enlarge the space or fix contradictory rules.

### A layer never shows up in the output

Check, in order: the variant's weight (`0` shelves it), the layer's absent chance (`100` shelves it),
and the Recipe's rules.

### The same seed gave a different collection

The book changed in a way that changes the roll: a layer was added, removed or reordered, or a weight,
range, quantize step or absent chance changed. Or one cook allowed repeats and the other did not. See
[Why a seed reproduces a collection](../concepts/seeds.md).

### `extend` warns that the book does not match

The Set records its CookBook's hash, and the book you gave differs. `extend` carries on, because that is
sometimes intended, but the new assets roll from the changed book.

### Art looks blurry

nfty displays pixel art with nearest-neighbor scaling and does not silently resize it on import.
Check the source art, display scale and partial transparency:
`nfty inspect book.cbk --voxel`.

## 3D and Rise

### "Include 3D models" is dimmed, or a picture is refused for partial transparency

Some variant has a partly transparent pixel, so the book is 2D only. See
[Fix a 2D-only book](../app/2d-only.md).

### A Set's assets all rise as flat paper

The Rise page reads depth from the CookBook that cooked the Set, found by its hash. If that book is not
open, recent or beside the Set, nfty cannot find it and says so.

### A model's sides are nearly black

A side takes the color of the art's edge pixel. Give the art a lighter outline for lighter sides.

## Publishing

### The export warns that image links are relative

A marketplace cannot fetch `images/0001.png`. Set an image base URI after uploading the art, or use the
UPLOAD tab, which writes the addresses itself. See [Upload to IPFS](../publishing/ipfs.md).

### A base URI is refused

It must be absolute (`ipfs://`, `https://`, `http://` or `ar://`) and end with `/`.

### The UPLOAD tab says "not reachable"

Start IPFS Desktop or `ipfs daemon`, or check **Settings → PUBLISHING → IPFS API**. For Pinata, check
the key (`key refused`) with **Test connection**.

### Pinata key cannot be saved (Linux)

nfty stores keys only in a credential store. Install `libsecret-tools` (or `libsecret`) and run a
keyring such as GNOME Keyring or KWallet.

### A sealed file will not open

A wrong passphrase and an altered file fail the same way, and nfty says it could be either. There is
no recovery for a lost passphrase. Passphrases must be at least 12 characters when sealing.

### `extend` refuses an export

An export with renamed files cannot be extended without mixing names. Extend the source Set, then
export again.

## Community gallery

### "Your GitHub account is under 30 days old; the gallery takes shares from older accounts."

Sharing needs an account at least 30 days old. Browsing and downloading are unaffected. Try again when
the account is old enough.

### "You share 3 collections, the most the gallery takes. Pick one to replace."

Each account holds three shares. On the Share card pick the one to give up (nothing is removed until
you press **Share**), or [unshare](../app/community.md#unshare) one first.

### "You already have a repository called nfty-public that nfty didn't make."

nfty only adopts an `nfty-public` repository it made (one with a `nfty-public.json`). Rename or remove
yours on GitHub, then share again; nfty makes a fresh one. A private `nfty-public` is refused too: the
gallery reads only public repositories, so make it public or replace it.

### "Your nfty-public holds things the gallery doesn't allow."

The repository may hold only `README.md`, `nfty-public.json` and your shares' releases. Press
**Remove these…** to delete what nfty can, after confirming. A submodule, a duplicate release, or
history past about 5 MB can't be cleaned by nfty; the card says what to do on GitHub, which for history
means deleting the repository and sharing again.

### "This Set is already in the gallery, shared by @…" or "Nothing changed since you shared…"

The gallery refuses a Set someone has already listed, and an update that changes nothing. Cook or edit
the Set so it differs, or leave the existing share as it is.

### "The gallery takes one new share a day."

Updates don't count, new shares do. Try again tomorrow.

### "GitHub can't be reached."

The Community page then shows the last list it saved, with its time, and Download and Share need the
network. If the message says `GitHub is limiting requests`, wait the minutes it names. After connecting
again, press **Refresh**. If sharing says `Reconnect GitHub to share.`, connect again in
[Settings](../app/settings.md#publishing).

### A share stays Pending, or turns Closed

The gallery checks a share within a few minutes. If it is still **Pending** later, **Refresh**. A
**Closed** tile means the gallery turned the request away; select it to read the reasons, fix them and
share again, then **Dismiss** the old request.

### A sealed Set can't be shared

`Sealed Sets can't be shared or come from the gallery.` Share the unsealed Set instead.
