# The demo CookBook

nfty carries a finished collection inside it. You do not have to download it, find it or build it —
it is part of the program, and it is there on a fresh copy with nothing else on the machine.

On the opening screen, click **Open the demo CookBook**.

It writes a real `ChestDemo.cbk` into a `demo` folder beside the application and opens it in the
Explorer. From that point nothing about it is special: it is an ordinary CookBook, it has the same
edit lock, and **Cook** produces a real Set.

From the command line, the same book:

```
nfty demo .
nfty inspect ChestDemo.cbk
```

## What is in it

A collection of layered treasure chests, drawn at 32×32.

| Layer | Kind | What it does |
|---|---|---|
| **Glow** | Dynamic | Sparks or runes floating around the chest. Usually absent. |
| **Body** | Dynamic | The chest itself — planked, plated or stone. |
| **Bands** | Dynamic | Hoops, corner brackets or straps. |
| **Trim** | Custom | Gilt edging or set gems. Usually absent. |
| **Lock** | Static | A keyhole, latch, padlock or keypad. |

Two Recipes share those layers: **Chest**, with a domed lid, and **Strongbox**, with a flat one.

That is deliberately one of everything the manual talks about:

- **All three [layer kinds](../understand/layer-kinds.md).** The Body and Bands roll their own color
  on every asset; the Lock is one fixed brass for the whole collection; the Trim is full-color art
  composited exactly as drawn.
- **Two [optional layers](../how-to/rarer-trait.md).** Glow is absent 72% of the time and Trim 55%,
  which is what makes some chests plain and a few of them special.
- **A [rule](../how-to/exclude-combinations.md).** A stone chest never carries a keypad.
- **Weighted color bands.** A body rolls warm timber about 60% of the time and cold metal the rest,
  rather than any color at all.

Sixteen small drawings, and about half a million distinct assets. That is
[why the art is gray](../understand/why-gray.md), stated as a file you can open.

## It is yours to break

Reorder the layers, delete a variant, repaint the lock, change the weights, cook it and look at the
result. Reopening the demo does **not** restore it — your copy stays as you left it.

To get a clean one back, delete `demo/ChestDemo.cbk` and click the button again, or run:

```
nfty demo . --force
```

---

**Next:** [Your first collection →](first-collection.md)
