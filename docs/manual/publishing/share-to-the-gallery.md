# Share a Set to the community gallery

List a cooked Set in the gallery so other nfty users can browse and download it.

**Before you start:** a cooked Set open in the [Set browser](../app/set-browser.md) (not a sealed `.tin`),
and a GitHub account at least 30 days old. The Set must fit the [limits](../app/community.md#limits):
a canvas up to 512 × 512, up to 1,000 assets, up to 200 MB packed.

1. Connect GitHub once. Open **Settings → PUBLISHING → GITHUB** and press **Connect GitHub…**. nfty
   shows a short code: press **Open GitHub**, type the code there and approve nfty. nfty never sees
   your password, and the token is kept in your system's credential store, never a file. See
   [Settings](../app/settings.md#publishing).
2. In the Set browser press **Share…** in the header. If nothing is connected the card's lower block
   reads `Sharing needs a GitHub account` and offers **Connect GitHub…**, which opens Settings at
   PUBLISHING over the card; once connected, close Settings and the card carries on.
3. Wait for the card to read your `nfty-public` repository and check the Set. Anything it refuses is
   marked in the **GALLERY LIMITS** table or named in the lower block, and **Share** stays dim, with its
   reason in the footer and on the tooltip,
   until nothing is left. See [Gallery problems](../help/troubleshooting.md#community-gallery).
4. Choose what travels. **Include source CookBook** lets others remix (enabled only when nfty finds the
   book that made the Set). **3D models** can be left out to make the file smaller.
5. Give the share a **NAME**, an optional **DESCRIPTION** (up to 500 characters, no links) and up to five
   **TAGS** from the gallery's list.
6. If the middle block asks for a choice (replace one of your three shares, or tick **Replace the shared
   version** to update), make it. Nothing is removed until you press **Share**.
7. Press **Share**. nfty packs the Set, makes the preview, uploads both to a Release in your
   `nfty-public` repository (creating it the first time), writes your listing and opens a request on the
   gallery. **Stop** cancels and takes back what this attempt made.

**Result:** the card ends on `Submitted. The gallery checks it now; it usually lists within a few
minutes.` Your tile reads **Pending** on the [Community page](../app/community.md) until the gallery lists
it. Browsing and downloading need no account, so anyone can then find it.

## Change or remove a share

- **Update:** share the changed Set again and tick **Replace the shared version**. An unchanged Set is
  refused (`Nothing changed since you shared…`).
- **Replace one:** at three shares, pick the share to give up on the card; it goes with the new one in
  one request.
- **Unshare:** select your tile on the Community page and press **Unshare**. The gallery drops the
  listing within a few minutes. nfty never deletes your repository.

## Check a Set from the command line

[`nfty share check`](../reference/cli.md#share-check) runs the same limits check without signing in,
which is useful before you spend time on the card.

## Next steps

- [The community gallery](../app/community.md): browsing, downloading, every card state.
- [Export a Set for a marketplace](export.md) is the other way out of nfty.
