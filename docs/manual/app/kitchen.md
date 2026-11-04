# Work out of a Kitchen

A **Kitchen** is your shelf of loose parts: Ingredients and Recipes that belong to no one CookBook,
kept in a folder so you can pull them into any project. A Kitchen is a `.ktn` file, and *the folder it
sits in* is the workspace: everything beside it is in the Kitchen.

## Create or open one

On the opening screen, click **New Kitchen…** (choose where to save the `.ktn`) or **Open Kitchen…**.
From the command line: `nfty new kitchen Studio/Studio.ktn --name Studio`.

## What it gives you

The **shelf** along the bottom of the opening screen lists the folder's contents: CookBooks first, then
loose Recipes and Ingredients. Click a card to open it; the chevrons page through by kind; **close**
leaves the Kitchen.

![The Kitchen shelf, listing the CookBooks in the folder](../images/kitchen-shelf-light.png#only-light)
![The Kitchen shelf, listing the CookBooks in the folder](../images/kitchen-shelf-dark.png#only-dark)

With a Kitchen open:

- loose Recipes and Ingredients you create from the opening screen are saved into it, instead of
  asking each time;
- the Ingredient Editor's **REFERENCES** tab lists its loose Ingredients under **FROM THE KITCHEN**,
  to line art up against;
- `nfty inspect Studio.ktn` lists what it holds.

## Pull a part into a CookBook

1. Open the CookBook and unlock editing.
2. To add a **layer**, select the **Recipe** it should join. To add a **Recipe**, select the book.
3. Press **Import** in the toolbar and pick the `.igt` or `.rcp` from the Kitchen folder.

A Recipe joins with a weight equal to the average of the Recipes already in the book. An Ingredient
goes into the selected Recipe; with only the book selected, nfty says so rather than guessing.

The file is **copied** in. Editing the layer inside the book does not change the loose file, so the
same hat can be guaranteed in one project and a chase item in another.

!!! tip "Nothing to keep in sync"

    Membership is found by **looking at the folder**, never recorded. Drop a `.cbk` in with your file
    manager and it appears; move one out and it is gone.
