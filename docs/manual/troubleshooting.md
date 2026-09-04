# When something goes wrong

## "This image is 256×256; the canvas is 512×512."

Every drawing in a project shares one canvas size. nfty will not resize your art, because resizing
pixel art destroys it. Redraw or re-export at the right size.

## "…allows exactly N unique assets, but M were requested."

The book cannot make that many distinct assets. Add variants, widen a hue range, or raise a layer's
quantize steps. The message tells you the real maximum.

See [Weights, rules and how many you can make](weights-and-rules.md).

## "Recipe X has two ingredients named Y."

Two layers in one Recipe share a name. A layer's name becomes the trait name in the published data,
so two of them would merge into one trait and one rarity bucket. Rename one.

## Validation lists several problems at once

That is deliberate — it reports everything it finds rather than stopping at the first, so you can fix
them in one pass.

## A layer is not showing up in the output

Check its variants' weights. A variant at weight 0 is shelved. Check the Recipe's rules too — a rule
may be excluding it more often than you intended.

## Your art looks blurry

It should not. nfty never smooths, resamples or anti-aliases your pixels anywhere — in the editor, the
previews, or the output. If something looks soft, it is soft in the source art, or it is partial
transparency — see [Transparency and the opacity lock](transparency.md).
