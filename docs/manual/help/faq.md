# Frequently asked questions

**Does nfty need an account or a network connection?**
nfty runs entirely on your machine with no account. Uploading to Pinata needs a Pinata account; a
local IPFS node needs none.

**Does nfty mint, deploy a contract, or list on a marketplace?**
No. It produces the files a drop needs, can upload them to IPFS, and gives you the `baseURI` and
`contractURI` for your contract. See [Publishing](../publishing/index.md).

**Does the same seed always give the same collection?**
Yes: the same book, seed and uniqueness mode give byte-identical output on any machine, locale, CPU
and thread count. See [Why a seed reproduces a collection](../concepts/seeds.md).

**I reordered my layers and the same seed gave different assets. Is that a bug?**
No. Each layer consumes one random draw in stack order, so moving a layer changes which draw reaches
it. A reordered book is a different collection.

**Why are my layers drawn in gray?**
Gray is lightness; nfty supplies the hue per asset, so one drawing becomes many colors with your
shading intact. Use a Custom layer when the colors are the art. See
[Layer kinds and value-maps](../concepts/layer-kinds.md).

**The unique DNA figure says "more than" or "at most". Why not a number?**
Some books cannot be counted exactly, and the wording says which way the figure is bounded. See
[How uniqueness is decided](../concepts/uniqueness.md#when-the-figure-is-a-bound-not-a-count).

**Why is the count not just the product of my variants?**
It counts distinct fingerprints: legal variant combinations (rules remove some) times each colored
layer's quantized color buckets, with each optional layer adding its "absent" case.

**Can a collection have duplicates?**
Only if you tick **Allow repeats** (or pass `--unlimited`). Weights still decide rarity.

**Why can't I make a 3D model from soft-edged art?**
A voxel is solid or empty, and a partly transparent pixel has no honest answer. nfty names it rather
than rounding. Snap it with **Make voxel-ready** or `nfty set voxel-ready`. See
[Fix a 2D-only book](../app/2d-only.md).

**Can I resize my art in nfty?**
No. nfty never resamples a pixel; a wrong-sized picture is refused. See
[Why nfty never resizes your art](../concepts/no-resizing.md).

**How strong is a sealed `.tin`?**
The encryption (AES-256-GCM) is real against anyone without the passphrase. The view-only mark is
policy: someone with the passphrase can get the files. A lock on a door, not a wall. See
[Sealing](../concepts/sealing.md).

**Can I recover a lost passphrase?**
No. There is no backdoor and no `unseal` command. Keep your original Set.

**Can my game roll assets without the CookBook?**
Yes: write a Menu (`.mnu`) and roll tickets with the dependency-free `Nfty.Ticket` library. See
[Menus and tickets](../embedding/menus-and-tickets.md).

**Are the libraries on NuGet?**
Not yet. Reference the projects from the repository. See [Embedding](../embedding/index.md).

**My canvas is large. Is cooking slow?**
Cooking and exporting scale with canvas area and asset count. Raise the worker threads in
[Settings](../app/settings.md#performance) or with `--threads`. A spritesheet is limited to 16,384 pixels
a side, and nfty says so before starting.

**Where is my work stored?**
Books and Sets are the files you chose. Recents, palettes and settings are in a `.nfty` folder; see
[Settings files](../reference/settings.md).

**Why won't nfty open my old archives?**
Archives before schema version 2 are refused by name rather than misread. Archives newer than your
build are refused too: a field your build cannot see might change what the file means.

**Ctrl+click does not open the viewer on my Mac.**
On macOS, Ctrl+click is a right click. Use middle-click or the pop-out button.

**Why is the image library version fixed?**
nfty pins ImageSharp 3.1.11 on purpose; later versions need a build-time license key. Building with a
different version is not supported.

**What are the four downloads?**
Portable (unzip and run), single-file (one executable, slower first launch), framework-dependent (needs
.NET 10) and single-file-net10 (one small executable, needs .NET 10). See
[Install nfty](../get-started/install.md#choose-a-download).
