---
title: nfty
---

# nfty

**Draw a handful of layers. Cook a collection of thousands.**

nfty is a generator for layered pixel-art collections. You draw a few options for each layer of a
character (backgrounds, bodies, eyes, hats), and nfty stacks, colors and combines them into a
collection where every asset records its traits and DNA fingerprint. Unique-DNA mode rejects
repeated fingerprints; identical or hidden drawings can still produce matching pixels. It ships as a
desktop app, a command line, and three .NET libraries you can embed in a game or a server.

![The nfty opening screen: create, open, recent books and the Kitchen shelf](images/landing-light.png#only-light)
![The nfty opening screen: create, open, recent books and the Kitchen shelf](images/landing-dark.png#only-dark)

## How it works, in thirty seconds

1. **Draw layers in gray.** Most layers are drawn as *value-maps*: gray means lightness, not color.
2. **nfty supplies the color.** At generation time each asset rolls a hue and saturation for each
   layer, inside ranges you set, and keeps your shading.
3. **Weights decide rarity.** Every option has a weight; rules forbid combinations that clash.
4. **Cook with a seed.** The same CookBook and the same seed always produce byte-identical output,
   on any machine.

The demo CookBook built into nfty is twenty-nine small drawings and admits **8,745,000**
distinct DNA values. Their selections and quantized colors are explained under
[uniqueness](concepts/uniqueness.md).

## Where to go

<div class="grid cards" markdown>

-   :material-rocket-launch: **Get started**

    ---

    Install nfty, open the demo, and cook your first collection in the app or from a terminal.

    [Install and first steps →](get-started/index.md)

-   :material-lightbulb: **Concepts**

    ---

    The eight words nfty uses, the three layer kinds, how uniqueness, seeds, rarity and depth work.

    [Learn the model →](concepts/index.md)

-   :material-monitor: **Using the app**

    ---

    One page per screen and task: the Explorer, the Ingredient Editor, cooking, the Set browser,
    the viewer, Rise and 3D, settings and Windows file associations.

    [App guide →](app/index.md)

-   :material-console: **Command line**

    ---

    Check a project, build one from a script, cook, extend and make models without a screen.

    [CLI guide →](cli/index.md)

-   :material-upload: **Publishing**

    ---

    Export for a marketplace, a buyer or a reviewer, upload to IPFS, and seal a collection.

    [Publish a collection →](publishing/index.md)

-   :material-code-braces: **Embedding**

    ---

    Roll and render assets from your own program with `Nfty.Ticket`, `Nfty.Core` and `Nfty.Model`.

    [Embed nfty →](embedding/index.md)

-   :material-book-open-variant: **Reference**

    ---

    Every command and option, every keyboard shortcut, file formats, settings and the API.

    [Look it up →](reference/index.md)

-   :material-lifebuoy: **Help**

    ---

    What an error message means, and answers to common questions.

    [Troubleshooting →](help/troubleshooting.md)

</div>

!!! note "Building or changing nfty itself?"

    The developer README and the contribution notes live in the
    [repository](https://github.com/corderro-artz/nfty). This manual is for people who use nfty.
