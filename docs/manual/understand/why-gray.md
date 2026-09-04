# Why layers are drawn in gray

This is the idea the whole product is built on. Everything else follows from it.

## A drawing is shape and shading. Color is separate.

When you draw a sphere, you are really doing two things at once: deciding its shape, and deciding
which parts of it are bright and which are dark. The hue -- red, blue, gold -- is a third, independent
decision.

A **value-map** keeps the first two and throws away the third. You draw in gray, where the gray is
*lightness*: dark where the object is in shadow, bright where the light hits it. At generation time
nfty keeps every pixel's brightness exactly as you drew it and injects a hue and a saturation.

Draw a sphere in gray once and it can be a red sphere, a blue sphere or a gold one, correctly shaded
in each. Draw it in red and it is only ever red.

## What this buys

**Your art multiplies.** A layer with two gray drawings and a wide hue range is not two options --
it is two shapes times however many colors nfty can tell apart, which is usually hundreds.

**Your collection stays coherent.** Every asset shares your shading and your shapes, so it reads as
one collection rather than a pile of unrelated images, even while every asset is visibly different.

**The output space is far bigger than the input.** This is the reason a five-layer character made of
thirteen drawings can produce nearly seven hundred thousand distinguishable assets.

## What it costs

You are drawing in a way that is not quite drawing. A gray drawing looks unfinished, and it takes a
little practice to judge shading without hue to help you. The editor's live preview exists precisely
for this -- it shows the layer in a rolled color as you work, so you are never guessing.

## When not to use it

When the colors *are* the art: a logo, a piece with several deliberate colors, a background with a
photograph in it. Those are what the **Custom** kind is for. See
[The three kinds of layer](layer-kinds.md).
