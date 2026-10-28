# Frequently asked questions

**Why does nfty refuse my old archives?**
Archives written before schema version 2 are refused with a message naming the version, rather than
read wrongly. Version 2 is the oldest nfty reads. Archives newer than your build are refused too: a
field your build cannot see might change what the file means.

**Does the same seed always give the same collection?**
Yes. The same book, seed and mode give byte-identical output on any machine, locale and CPU. Unique-only
and allow-repeats give different collections from one seed. See
[Why a seed reproduces a collection](seeds.md).

**I reordered my layers and the same seed gave different assets. Is that a bug?**
No. Each layer consumes one random draw in stack order, so moving a layer changes which draw reaches
it. A reordered book is a different collection. `extend` warns when the book no longer matches the
one that cooked the Set.

**Why can't I make a 3D model from my soft-edged art?**
A model needs every pixel either solid or empty. A partly transparent pixel has no honest depth, so
nfty names it and refuses. Use the opacity lock while painting, or snap existing art with
`nfty set voxel-ready`. See [Depth](depth.md) and [Fix a 2D-only book](../how-to/fix-a-2d-only-book.md).

**The unique DNA count says "at least" or "at most". Why not a number?**
Some counts cannot be reached exactly. The wording tells you which direction the figure is bounded in,
so nfty never claims more than it knows. See [How uniqueness is decided](uniqueness.md).

**Why is the count not just the product of my variants?**
It counts distinct fingerprints: legal variant combinations (rules remove some) times each colored
layer's quantized color buckets. A layer that can be left out adds that case too.

**How strong is a sealed `.tin`?**
The encryption is real (AES-256-GCM). The view-only mark is a policy: the format is open, so someone
who holds the passphrase can get the files. A sealed export is a lock on a door, not a wall. See
[Sealing](sealing.md).

**Can I recover a lost passphrase?**
No. There is no backdoor, and no command that unseals.

**Why won't nfty resize my art?**
It never resamples a pixel. A wrong-sized picture is refused so you decide how to fit it. See
[Why nfty never resizes your art](no-resizing.md).

**My canvas is large. Is that slow?**
Cooking and exporting scale with canvas area and asset count. Raise the worker threads in
[Settings](../reference/settings.md) or with `--threads`. A spritesheet is limited to 16,384 pixels a
side, and nfty tells you the numbers before it starts.

**Where is my work stored?**
Books and Sets are the files you chose. Recents, palettes and view choices are in a `.nfty` folder
beside the program. See [Settings](../reference/settings.md).

**Ctrl+click does not open the viewer on my Mac.**
On macOS, Ctrl+click is a right click. Use middle-click, or the pop-out button.

**Why is the image library version fixed?**
nfty pins ImageSharp 3.1.11 on purpose; version 4 needs a build-time license key. Building with a
different version is not supported.

**What are the four downloads?**
Portable (unzip and run), single file (one `.exe`, slower first launch), framework-dependent (needs
.NET 10) and single file for .NET 10 (one small `.exe`, needs .NET 10). See
[Install](../get-started/install.md).
