---
title: nfty
---

# nfty

**Draw a handful of pieces. Get a collection of thousands.**

nfty stacks layered pixel art into large collections. You draw a few options for each layer — a few
backgrounds, a few bodies, a few hats — and nfty combines and colors them, giving every asset a
fingerprint that proves it is unlike any other in the set.

![The nfty opening screen](images/landing-light.png#only-light)
![The nfty opening screen](images/landing-dark.png#only-dark)

<div class="grid cards" markdown>

-   :material-rocket-launch: **New here?**

    ---

    Install it, then build a small collection end to end. About twenty minutes.

    [Get started →](get-started/index.md)

-   :material-help-circle: **Trying to do one thing?**

    ---

    Short answers to real questions: make a trait rarer, stop two traits clashing, add to a
    finished set.

    [How do I… →](how-to/index.md)

-   :material-lightbulb: **Want to know why?**

    ---

    Why layers are drawn in gray, how uniqueness is counted, why a seed reproduces a collection.

    [How nfty works →](understand/index.md)

-   :material-book-open-variant: **Looking something up?**

    ---

    Every screen, every keyboard shortcut, every command, every error message.

    [Reference →](reference/index.md)

</div>

## The idea in one minute

A character is stacked layers: a background, a body, eyes, an aura, a hat. You draw a few options for
each. nfty picks one option per layer, stacks them, and that is one asset. Do it five hundred times
and you have a collection.

The twist is **color**. Most layers are not drawn in color at all. You draw them in **gray**, and
nfty reads the gray as *lightness* — how bright each pixel is — then supplies a hue at generation
time. One gray drawing of an aura becomes a blue aura, a pink aura, a gold one, each keeping the
shading you drew.

That is why a small amount of art goes a very long way. The five-layer Cat in these screenshots is
thirteen drawings, and it can produce **699,840** distinguishable assets.

!!! note "Everything is repeatable"

    Every collection is cooked from a *seed* — a word you choose. The same CookBook and the same seed
    always produce exactly the same collection, on any computer. Change the seed, get a different
    one. See [Why a seed reproduces a collection](understand/seeds.md).

---

*Building or modifying nfty itself? The developer README lives in the repository root.*
