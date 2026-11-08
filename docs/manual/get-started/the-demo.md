# Open the demo CookBook

nfty carries a finished collection inside the program. Opening it is the fastest way to see what a
complete project looks like before you build your own. Nothing is downloaded.

## Open it

=== "In the app"

    On the opening screen, click **Open the demo CookBook**.

    nfty writes a real `ChestDemo.cbk` into a `demo` folder beside the application and opens it in
    the Explorer.

=== "From the command line"

    ```bash
    nfty demo .              # writes ./ChestDemo.cbk
    nfty inspect ChestDemo.cbk
    nfty stats ChestDemo.cbk
    ```

From then on it is an ordinary CookBook: it has the same edit lock as any other, and **Cook Set**
produces a real Set.

## What is in it

![The demo CookBook open in the Explorer](../images/explorer-light.png#only-light)
![The demo CookBook open in the Explorer](../images/explorer-dark.png#only-dark)

Layered treasure chests, drawn at 32×32 pixels. **Chest**, with a barrel lid, and **Strongbox**, with
a flat slab lid, each stack six layer categories. They share five Ingredients and use different
Body Ingredients: seven Ingredients in the book altogether.

| Layer | Kind | What it does |
|---|---|---|
| **Glow** | Dynamic | Sparks or runes floating around the chest. Absent 72% of the time. |
| **Feet** | Custom | Pewter blocks, a runner rail, or gilt ball feet. |
| **Body** | Dynamic | The chest itself: planked, plated, slatted, paneled or stone. |
| **Bands** | Dynamic | Hoops, straps, corner brackets, side edging or studs. |
| **Trim** | Custom | Gilt edging, set gems, pearls or filigree. Absent 55% of the time. |
| **Lock** | Static | A keyhole, latch, padlock, dial, crest or keypad, in one fixed brass. |

The book deliberately shows one of everything:

- **All three [layer kinds](../concepts/layer-kinds.md).** Body and Bands roll their own color on
  every asset, the Lock is one brass for the whole collection, and Trim and Feet are full-color art
  composited exactly as drawn.
- **Weighted color bands.** A body rolls warm timber about 60% of the time and cold metal the rest.
- **Two optional layers.** Glow and Trim are often left out, which is what makes a few chests rare.
  See [Weights and rarity](../app/weights.md).
- **A rule.** A stone chest never carries a keypad. See [Rules](../app/rules.md).
- **Depth.** Body, Feet, Bands, Trim and Lock carry relief; Glow deliberately stays flat paper.
  Explore the result on the [Rise page](../app/rise.md).

Twenty-nine drawings permit exactly **8,745,000** distinct DNA values. That figure counts
fingerprints, not guaranteed visually different pictures; see
[How uniqueness is decided](../concepts/uniqueness.md).

## It is yours to change

Reorder the layers, repaint the lock, change the weights, cook it. Reopening the demo does **not**
restore it: your copy stays as you left it. To get a clean copy, delete `demo/ChestDemo.cbk` and
click the button again, or run:

```bash
nfty demo . --force
```

## Next step

[Your first collection in the app →](first-collection.md), or skip to
[your first collection from the command line](first-collection-cli.md).
