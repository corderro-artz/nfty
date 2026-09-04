# The Kitchen shelf — design

Opening a Kitchen gave you a titlebar chip, a default save folder for loose parts, and reference
layers in the Ingredient Editor. It did not let you **open** anything in it.

## What was actually wrong

`Kitchen.Open` scans the folder and returns `KitchenContents` with three lists. Grepping every
consumer in the GUI:

| List | GUI readers |
|---|---|
| `Ingredients` | 1 — the editor's reference panel |
| `Recipes` | **0** |
| `CookBooks` | **0** — read only by `KitchenReport`, which is CLI-only |

So the app computed the list of CookBooks in your workspace on every open and every rescan, and no
screen ever asked for it. Concretely: you point the app at `…/Studio/Studio.ktn`, and to open the
CookBook sitting beside it you navigate a file dialog back to the same folder. **A Kitchen sped up
writing into the workspace and did nothing for reading out of it.**

Not a correctness bug, not data loss, not a performance problem — the scan is a directory glob.

## The constraint that shaped it

`KitchenContents` holds **paths**, deliberately: `CookBookArchive.Read` eagerly decodes every variant
PNG in the tree, so materialising a folder to list it would pull the whole workspace into memory.

But a listing that can only show file names is a poor listing, and everything worth showing is in the
**outer manifest**: `CookBookManifest.RecipeWeights.Count` is the recipe count and `Canvas` is the
size. So `ArchivePeek` opens the zip, reads `manifest.json`, closes — through the same
`ArchiveIo.ReadManifest` gate as every other reader, so the schema check applies here too.

The test that carries this corrupts a PNG *inside* a written archive and peeks it anyway: the peek is
untroubled and a real read throws, which the peek could only manage by never having touched the image.

## What shipped

Variant **C** of `docs/design/mockups/explorations/kitchen-contents-variants.html` — a full-width band
beneath Landing's two columns — resolved against two requirements in
`kitchen-shelf.html`:

**The band is always present, always the same height.** With a Kitchen, without one, with an empty
one. Opening or closing a workspace swaps the ink inside a box that never moves. That settles the
geometry rule (variant C's stated cost was that Landing would change height — it does not) *and* it
is the visual argument that the Kitchen is something the app always carries rather than something a
CookBook has.

The three states share one 52px grid row, so they are the same height **by construction** rather than
by three declarations agreeing. An explicit `Height` on the band was removed for the same reason: it
was a second statement of a number the fixed rows already made, and the two had already drifted by a
pixel.

**One row, paged by kind.** The pages of every kind are concatenated in scan order — CookBooks,
Recipes, Ingredients — into one flat sequence, so crossing from the last CookBook page into the first
Recipe page is the same gesture as moving within a kind. There is no second control for "change
kind", because there is no second thing to do. Wheel, chevrons and keyboard all page it; a
pointer-only control would be the incomplete version, the same way pointer-only reorder was.

**The band carries no buttons of its own.** Open Kitchen… and New Kitchen… already sit in the Open and
Create groups a few inches to the left. Two controls for one action a few inches apart is worse than
one, so the empty states name those actions in prose instead of repeating them.

## Details worth keeping

- **Page size follows the width.** Cards fill the row, so how many fit is a function of the rendered
  width, which only the view knows: `LandingView` measures its own row and sets `PageSize`. Watching
  the row rather than the window means a change to the band's padding cannot desynchronise the two.
- **A resize keeps your place.** Repaginating anchors on the card you were looking at rather than
  snapping to page one. The guarantee is that the card is *still on screen*, not that it leads the
  page — a different page size necessarily reshuffles which card leads.
- **A short final page keeps its empty slots**, so the cards stay one width instead of re-spacing
  mid-sequence. The no-reflow rule applied to content rather than chrome.
- **The ends clamp, never wrap.** A shelf that jumped from the last Ingredient back to the first
  CookBook would make "am I at the end?" unanswerable.
- **An unreadable file keeps its card** and loses only its subtitle. A broken archive in a folder is a
  thing to *see* in the listing, not a reason for the listing to fail.
- **A kind with nothing in it contributes no page**, so an Ingredients-only workspace never shows an
  empty "Recipes" heading.

## Tested

12 ViewModel tests (paging across kinds, clamping, slots, resize anchoring, the three states, card
building from a real workspace, the unreadable file), 5 layout tests measured off a laid-out visual
tree (one height in all three states, nothing above the band moves, cards keep their width, the
chevrons never move, the view really measures), and 5 on `ArchivePeek`.

Nine mutation probes; one survived and was informative: removing the band's explicit `Height` changed
nothing, because the fixed rows already determined it. That redundant declaration was deleted rather
than the test strengthened — and probing the restructure that *could* break the claim (making the row
`Auto`) is detected.

## Deliberately not done

- **The shelf on other screens.** The Kitchen chip already travels with you in the titlebar; the shelf
  lives on Landing, which is where "no CookBook is open" lives. Putting it on the Explorer would fight
  that screen's own three-pane layout.
- **Adding to a Kitchen from the shelf.** Loose saves already default into the open workspace
  (`ExplorerViewModel.DefaultLoosePath`), so there is nothing the shelf would add beyond a fourth
  place to start a file.
