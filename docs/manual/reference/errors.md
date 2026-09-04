# Error messages

What nfty says, what it means, and what to do. Messages are written to be read -- the app never
shows a stack trace unless you ask for one with `--verbose`.

## "This image is 256x256; the canvas is 512x512."

Every drawing in a project shares one canvas size, and nfty will not resize your art.

**Fix:** re-export at the right size. See [Why nfty never resizes your art](../understand/no-resizing.md).

## "...allows exactly N unique assets, but M were requested."

The book cannot make that many distinct assets. The message states the real maximum.

**Fix:** add variants, widen a hue range, or make the quantize steps finer. See
[Raise the unique-asset count](../how-to/raise-unique-count.md).

## A rule conflict

The Recipe's rules exclude *every* combination, so there is nothing legal left to roll. This is not
the same as the message above -- there the space was real and too small, here it is empty.

**Fix:** remove or loosen a rule. See [Stop two traits appearing together](../how-to/exclude-combinations.md).

## "the reroll budget ran out"

nfty tried and failed to find a legal, unique roll within its per-asset budget. The space exists; it
is just hard to hit at random.

**Fix:** raise `--max-rerolls`, or loosen whatever is making legal rolls rare.

## "Recipe X has two ingredients named Y."

Two layers in one Recipe share a name. A layer's name becomes the `trait_type` in the published
metadata, so two of them would merge into one trait and one rarity bucket -- which is how a
collection ships percentages above 100%.

**Fix:** rename one. The same applies to the reserved name `Type`, which is used for the Recipe.

## "Unsupported schema version"

The file was written by a newer build of nfty than the one you are running. nfty reads its own
version and older ones, and refuses newer ones rather than misreading them.

**Fix:** update nfty.

## Validation lists several problems at once

That is deliberate. `validate` reports everything it finds rather than stopping at the first, so you
can fix them in one pass.

## A layer is not showing up in the output

Not an error message, but the most common surprise.

**Check:** the variant's weight -- at 0 it is shelved. Then the Recipe's
[rules](../how-to/exclude-combinations.md), which may be excluding it more often than you intended.

## Your art looks blurry

It should not. nfty never smooths, resamples or anti-aliases anywhere -- editor, previews or output.

**Check:** the source art, and then partial transparency. See
[Use soft edges](../how-to/soft-edges.md) and run `nfty inspect yourbook.cbk --voxel`.
