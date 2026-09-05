# Reorder the layer stack

Layers paint **bottom to top**. Depth 1 paints first and sits furthest back; the last one paints over
everything.

## Move a layer

1. Unlock editing (click the **read-only** chip in the title bar). Grips appear at the left of each row:

    ![The layer table unlocked, showing drag grips](../images/layers-unlocked-light.png#only-light)
    ![The layer table unlocked, showing drag grips](../images/layers-unlocked-dark.png#only-dark)

2. Either **drag a row by its grip**, or select a row and press ++alt+up++ / ++alt+down++.

Both do the same thing. The keyboard route exists so reordering never depends on a steady hand.

!!! warning "Reordering makes a different collection, not the same one rearranged"

    nfty draws one random number per layer, in order. Move a layer and you move *which draw reaches
    it*, so on any layer with more than one variant the selection itself changes.

    If you have already cooked a Set from this book, the next cook after a reorder produces different
    assets — not the same assets restacked. Read [Why a seed reproduces a collection](../understand/seeds.md)
    before reordering a book you have shipped.
