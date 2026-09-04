---
title: nfty
---

# nfty

**How to make a collection, from a blank folder to a finished set of assets.**

nfty builds large collections of layered pixel art. You draw a handful of pieces; it stacks them in
every legal combination and colors them, so a few dozen drawings become thousands of distinct
assets — each one recorded with a fingerprint that proves it is unique.

<div class="vs-cards">
  <a href="concepts/"><strong>The six words</strong><p>The whole model in one table. Learn these and you have learned the app.</p></a>
  <a href="first-collection/"><strong>Your first collection</strong><p>Six steps from an empty screen to a finished set.</p></a>
  <a href="drawing/"><strong>Drawing</strong><p>The editor, its tools, and how reference layers line art up.</p></a>
  <a href="troubleshooting/"><strong>When something goes wrong</strong><p>The errors you are most likely to hit, and what they mean.</p></a>
</div>

## The idea in one minute

A character is made of stacked layers — a background, a body, eyes, an aura. You draw a few options
for each layer. nfty picks one option per layer, stacks them, and that is one asset. Do it a thousand
times with different picks and you have a collection.

The twist is **color**. Most layers are not drawn in color at all. You draw them in **gray**, and
nfty treats the gray as *lightness* — how bright each pixel is — then adds a color at generation
time. One gray drawing of an aura becomes a blue aura, a pink aura, a green one, all with the same
shading you drew.

That is why a small amount of art goes a very long way.

!!! note "Everything is repeatable"

    Every collection is generated from a *seed* — a word you choose. The same book and the same seed
    always produce exactly the same collection, on any computer. Change the seed and you get a
    different one.

---

*Building or modifying nfty itself? The developer README lives in the repository root.*
