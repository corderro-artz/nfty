# Depth: paper, front and back

How a flat picture becomes a solid, and the few decisions behind it. For the steps, see
[Make a 3D model](../how-to/make-a-3d-model.md).

## A column per pixel

Rise gives every **fully opaque** pixel a column of voxels (cubes). The column has three parts:

- **Paper**: the one voxel the pixel always has. A picture you never touch is paper everywhere, one
  voxel thick, a card cut to the shape of your art.
- **Front**: how many extra voxels stand out toward the viewer.
- **Back**: how many extra voxels reach away from the viewer.

So a pixel with front 2 and back 1 is 1 + 2 + 1 = 4 voxels thick. Front and back are each one byte
(0 to 255) per pixel, which is why the stored depth map is exactly two bytes per pixel; see
[File formats](../reference/file-formats.md#depth-maps).

The model is the art, extruded. Its texture is one image with the **front** picture on the left and the
**back** picture on the right, a one-pixel gutter between them so smoothing never bleeds one into the
other. The sides of every column take the color of the art's **edge pixel** there, so a column whose
edge pixel is near black gets a near-black side. That is how it is drawn, not a bug; give the edge a
lighter outline if you want lighter sides. Where two layers own a column's front and back, its side
changes from the back owner's color to the front owner's at the seam. Nothing is colored a second time.

Sets cooked before this stacking rule keep the models they were cooked with; cook again to get the
new ones.

## "Depth" means two things here

The Explorer's **depth** is a layer's position in the stack: depth 1 paints first and sits furthest
back. That is paint order, and it decides which layer hides which. The Rise page's depth is
**thickness**, how far a pixel stands out of or sinks behind its own plane. The two meet only when
a Recipe is risen: the stack is flattened to one picture by paint order, and each visible pixel takes
the thickness of the whole stack there: the front reaches as far as any solid layer's front, the back as far as any
layer's back, and each face takes the color of the layer that owns it. A layer on top can add relief but never cut away what a layer under it built.

## One solid, from finished art

A model is built from one finished image, never from layers. A Recipe's roll is composited first,
exactly as cooking composites it, and the single result rises. So a model always matches the picture
you would have shipped.

## A voxel is there or it is not

Rise asks one question of every pixel: is it **fully opaque**? Yes means a column. Anything else,
fully clear or partly transparent, is not solid.

Fully clear is fine: nothing is there. Partly transparent is the difficulty. A 40%-opaque pixel has
no correct voxel: a cube is 100% there, and any rule that turns the pixel solid or empty throws away
something you drew. nfty will not choose for you silently, so a picture with a partly transparent
pixel is refused, naming it. For a stack, the check runs on **every layer before compositing**, so a
partial pixel cannot hide beneath an opaque one.

## Why 2D only is derived

A book, Recipe or Ingredient is **2D only** when any of its art has a partly transparent pixel. That
is a fact about the pixels, so nfty never keeps it as a setting you could get wrong. The `partialAlpha`
flag in each manifest is a **listing cache**: every writer recomputes it from the pixels, and every
reader that decodes pixels recomputes it again. A hand-edited flag cannot make a soft-edged book look
voxel-ready, and a fixed book cannot stay marked.

The cost of deriving is a pixel scan per Ingredient each time the tree rebuilds, which is why the mark
appears after a save rather than on every keystroke.

Partial alpha stays legal for everything else. Images, metadata and rarity do not care; only models do.
That is why the padlock that keeps painting opaque can be turned off, with a warning, rather than
removed.

## Why "make voxel-ready" is a tool, not a default

Snapping partly transparent pixels to solid or clear (at 50% alpha) is a decision about your art, so
it happens only when you ask, shows you every variant it will change, and can be undone. The
alternative, rounding on export, would produce a model that differs from the picture in your
collection without ever saying so. Compare [Why nfty never resizes your art](no-resizing.md).

## Why the debug mark never reaches a model

Cooking can stamp each asset's number in a corner, for checking a sheet by eye. That is a property of
an **exported image**, applied on the way out to a copy. A model is copied as cooked, and its texture
is the unstamped asset, so a mark can never end up painted on a 3D model. The export card says so
beside the mark option.

## Why a gray layer rises gray

A Dynamic or Static layer is stored as a gray value-map, and its color is decided when an asset is
cooked ([Why layers are drawn in gray](why-gray.md)). Risen from the Rise page, such a variant is
therefore its gray pixels, exactly like a loose picture of them. A model of a **cooked** asset carries
the colors that asset rolled, because cooking has colored it by then.
