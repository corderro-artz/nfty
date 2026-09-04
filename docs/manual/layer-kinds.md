# The three kinds of layer

When you create a layer you choose its kind. This is the most important decision you make about a
layer, and it decides how you draw it.

## <span class="kind dynamic">D</span> Dynamic — a different color every time

You draw it in **gray**. nfty picks a color for it per asset, from a range you set.

Use it for anything that should vary: auras, fur, clothing, gems. This is where the multiplication
happens — one gray drawing with a wide hue range becomes dozens of visibly different assets.

You set a **hue range** (0–360°, the color wheel) and a **saturation range** (0–100%, gray to
vivid). Every asset rolls a color inside those.

## <span class="kind static">S</span> Static — one fixed color, always

You draw it in **gray**, and nfty applies a single color you choose. Same color on every asset.

Use it for anything that should be consistent but is easier to draw in gray — an outline, a shadow, a
brand color.

## <span class="kind custom">C</span> Custom — exactly what you drew

Full-color art, placed as-is and never recolored.

Use it for anything whose colors matter as drawn: a logo, a detailed background, a piece of art that
is not a silhouette.

!!! note "Why draw in gray at all?"

    Because a gray drawing carries the *shading* and nfty supplies the *color*. Draw a sphere in gray
    once and it can be a red sphere, a blue sphere or a gold one, all correctly shaded. Draw it in
    red and it is only ever red.
