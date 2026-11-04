# Import art from another program

Draw in Aseprite, Photoshop, Procreate or anything else, and bring the PNG or JPEG into nfty as a new
layer or as one variant of an existing layer.

**Before you start:** export the picture at **exactly the CookBook's canvas size**. nfty never
resizes art (see [Why nfty never resizes your art](../concepts/no-resizing.md)); a picture of another
size is refused with both sizes named.

## Make a picture into a new layer

1. In the Explorer, unlock editing and select the **Recipe** the layer will join.
2. Click **Import** in the toolbar and choose a `.png`, `.jpg` or `.jpeg`.
3. The **Image as Ingredient** form opens. Give the layer a **NAME** and choose its **KIND**. The
   preview shows the picture as that kind will store it:

    | Kind | What nfty keeps |
    |---|---|
    | **Custom** | The art exactly as drawn, colors and all. |
    | **Dynamic** | Lightness only: a value-map whose color is rolled per asset from the hue and saturation you set. |
    | **Static** | Lightness only, colored with one **FIXED COLOR**. |

4. Click **Import & paint**. The picture becomes the layer's first variant and the editor opens on it.

!!! warning "Dynamic and Static drop the picture's colors"

    Those kinds are value-maps, so only each pixel's lightness survives. The form says so when color
    will be lost. If you meant to keep the colors, choose **Custom**.

## Replace one variant's art

In the [Ingredient Editor](ingredient-editor.md), select the variant and click **Import…** in the left
pane. The picture replaces that variant's art. To bring in several, add a variant for each and import
into them one at a time.

## Pictures with a color profile

Art drawn on a wide-gamut screen often carries an embedded profile: Display P3 on a recent Mac or
iPad, Adobe RGB from Photoshop. nfty stores plain pixels, and marketplaces, browsers and game engines
read plain pixels as sRGB, so a P3 picture stored as-is looks duller wherever it travels.

- The **Image as Ingredient** form names the profile and offers **Convert to sRGB**, ticked by
  default; the preview shows the result. On a Dynamic or Static layer the conversion happens first,
  then nfty takes the lightness.
- The editor's **Import…** converts without asking and says that it did.
- Profiles nfty cannot convert (gray, CMYK and other print profiles, table-only profiles) are named,
  and the picture comes in as stored.
- From the command line, `new ingredient` and `add variant` convert too; `--keep-profile` skips it.

## Bring in a part from another project

A loose `.igt` or `.rcp` imports the same way, with the Explorer's **Import**. See
[Pull a part into a CookBook](kitchen.md#pull-a-part-into-a-cookbook).
