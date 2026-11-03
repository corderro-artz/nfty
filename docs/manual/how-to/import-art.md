# Import art from another program

Draw in Aseprite, Photoshop, Procreate — anything — and bring the PNG in.

## Import one variant

In the Ingredient Editor, click **Import image…** on the left. It replaces the selected variant's
art.

To bring in several, add a variant for each and import into them one at a time.

## The one rule

**The image must be exactly the CookBook's canvas size.** A 256×256 file will not go into a 512×512
book.

nfty will not resize it for you, and that is deliberate: scaling pixel art destroys it. See
[Why nfty never resizes your art](../understand/no-resizing.md).

Export at the right size instead.

## Color art on a gray layer

If you import a full-color image into a Dynamic or Static layer, nfty converts it to its **lightness**
— because those layers are value-maps, not pictures. The app tells you when this happens.

That is usually what you want for shading. If you meant to keep the colors, the layer needs to be
**Custom** — see [Paint in full color](paint-in-color.md).

## Pictures with a color profile

Art drawn on a wide-gamut screen often carries an embedded **color profile** — Display P3 on a
recent Mac or iPad, Adobe RGB from Photoshop. nfty stores plain pixels, and marketplaces, browsers
and game engines read plain pixels as **sRGB**, so a P3 picture kept as stored looks duller wherever
it travels.

When you import such a picture, the form says which profile it uses and offers **Convert to sRGB**,
ticked by default; the preview shows the result. On a Dynamic or Static layer the conversion happens
first, then nfty takes the lightness. The Ingredient Editor's **Import image…** converts without
asking and tells you it did. Profiles nfty can't convert (print profiles like CMYK, and
table-based ones) are named, and the picture comes in as it is. From the command line,
`--keep-profile` skips the conversion.
