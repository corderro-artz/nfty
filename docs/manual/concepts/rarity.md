# Rarity and weights

Rarity in nfty is not a label you assign. It comes out of four settings, and a finished Set reports
it as a share of the collection. This page explains how the settings combine; for the steps, see
[Weights and rarity](../app/weights.md).

## The four inputs

| Setting | Lives on | Decides |
|---|---|---|
| **Recipe weight** | CookBook | How often each character type is rolled. `0` shelves a Recipe. |
| **Variant weight** | Ingredient | How often each option of a layer is picked, relative to its siblings. `0` shelves a variant. |
| **Absent chance** | Recipe, per layer | The percent of assets that leave the layer out entirely. `0` always present, `100` never. |
| **Color range weight** | Ingredient (Dynamic) | How often each hue and saturation range is used when a color is rolled. |

Weights are **relative**. A variant at weight 3 is picked three times as often as one at weight 1;
weights never need to add up to anything.

An asset is rolled in this order: Recipe by weight, then for each layer in paint order: absent or
present, then a variant by weight, then (for a Dynamic layer) a range by weight and a color inside
it.

## Why the absent chance is on the Recipe

One Ingredient file can sit in several Recipes, and in a [Kitchen](../app/kitchen.md) it can be
shared between projects. The same hat may be guaranteed on one character and a chase item on
another, so the chance belongs to the Recipe that stacks it.

Leaving a layer out is better than adding an empty variant. An empty variant on a Dynamic layer
still rolls a color, so two assets that both show nothing get different DNA and the unique DNA figure
inflates for no visible reason. An absent layer rolls no color and adds nothing to the DNA.

## Rules change the odds too

A [rule](../app/rules.md) rejects some combinations, and nfty re-rolls the whole asset (Recipe
included) when one fires. So rules move probability around: if stone bodies may never carry keypads,
the remaining locks become slightly commoner on stone chests. nfty's exact odds account for this;
a simple product of shares does not.

## Predicted versus observed

| Where | Reports | From |
|---|---|---|
| Ingredient panel's **IN RECIPE** / **OVERALL** columns, `nfty stats` | What the weights **predict** | The CookBook |
| `set.json`, the Set browser's **RARITY** table, `nfty inspect <set>` | What a cook **actually produced** | The Set |

**OVERALL** is the share of the whole collection that gets a variant, after the Recipe's weight and
the layer's absent chance; aim at that, because it is what a buyer sees. A Set's observed shares
drift from the prediction by chance, less so the larger the Set.

Rarity is **collection-wide**: "3% of assets have this hat" changes the moment the collection grows.
Extending a Set rewrites every existing item's `rarity` field. Images and DNA never change.

## An asset's combined chance

The Set browser shows, beside each asset, its combined chance (for example `this combo 1 in 43,200`).
When nfty can find the CookBook that cooked the Set (by its hash), the figure is exact: the
probability of that whole roll, with rules accounted for. When it cannot, the figure is marked `~`
and is the product of the trait shares, which assumes the layers are independent; its tooltip says
so. You can read rarities as percentages or as odds (`1 in 24`); the page header toggles every figure
at once.

## Colors and absences are traits

From metadata shape 2 (every new cook), each Dynamic and Static layer publishes a `"<Layer> Color"`
trait named from the rolled color, and a left-out optional layer publishes `None`. Both count in the
rarity table like any other trait. See [Color traits and None](../reference/set-contents.md#color-traits-and-none).
