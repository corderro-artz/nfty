# Using the app

The desktop app is where you draw, tune and look at a collection. This section has one page per
screen or task. If you have not used nfty before, do the
[Get started](../get-started/index.md) tutorials first.

## The screens

| Screen | You go there to | Page |
|---|---|---|
| Opening screen | Create or open a book, open a Set, rise an asset, open the viewer | [The opening screen](opening-screen.md) |
| Explorer | Read and change a CookBook's structure: Recipes, layers, variants | [The Explorer](explorer.md) |
| Ingredient Editor | Draw a layer's variants and set its colors | [The Ingredient Editor](ingredient-editor.md) |
| Cook dialog | Generate a Set, or add to one | [Cook a Set](cooking.md) |
| Set browser | Look through a cooked Set, check rarity, export | [Browse a Set](set-browser.md) |
| Viewer window | Look at one asset, model or file in a window of its own | [The viewer](viewer.md) |
| Rise page | Give pixels depth and export 3D models | [Rise: make 3D models](rise.md) |
| Settings | Theme, threads, upload provider, data, Windows file types | [Settings](settings.md) |

## Tasks

**Shaping a collection:** [Weights and rarity](weights.md) ·
[Rules: never or always together](rules.md) · [Reorder the layer stack](layer-order.md)

**Art:** [Import art from another program](import-art.md) ·
[Paint in color and use transparency](color-and-transparency.md)

**3D:** [Rise a Recipe roll](rise-a-recipe.md) · [Rise a spritesheet](spritesheets.md) ·
[Fix a 2D-only book](2d-only.md)

**Workspace and system:** [Work out of a Kitchen](kitchen.md) ·
[Open files with nfty on Windows](file-associations.md)

Exporting and uploading a Set have their own section: [Publishing](../publishing/index.md).

## Things that work the same everywhere

**The edit lock.** A CookBook opens **read-only**. Click the **read-only** chip in the title bar to
unlock adding, deleting, reordering and saving. See [The edit lock](explorer.md#the-edit-lock).

**The status bar.** Along the bottom: whether the book is valid (click the count to see every
problem), the lock state, the book's counts, and a line about whatever you last did. The **gear**
(Settings) and **?** (Quick Reference) sit at its right end, beside the window zoom.

**The Quick Reference.** Press ++ctrl+slash++ or click **?** for a searchable sheet of every key and
term. The full list is in [Keyboard shortcuts](../reference/shortcuts.md).

**Light and dark.** ++ctrl+t++ switches theme; Settings → GENERAL can follow your system instead.

**Window zoom.** ++ctrl+plus++, ++ctrl+minus++ and ++ctrl+0++ scale the whole window.

**Full screen.** ++f11++, or the first button in the titlebar, fills the display with the window and
back again. ++esc++ closes an open dialog and never leaves full screen. Viewer windows opened meanwhile
stay above it.

**Other windows.** While a viewer window is open, a badge in the titlebar shows how many. Click it,
or press ++ctrl+grave-accent++, to bring **every** window back exactly as you left it (place, size,
maximized or not); click again to put them all away. Arrange your viewers once, then flip them into
view and out again while you keep working in the main window. The key works from inside a viewer
too. Right-click the badge, or press ++ctrl+shift+w++, for a list: click a window's row to show just
that one, or use the row's **minimize** and **close** buttons. ++ctrl+shift+grave-accent++ brings the
next window forward. The badge has a slot of its own, so it never moves the lock or the window
buttons. This is the way back to a viewer while the main window is full screen.

![The other-windows list: each open viewer with its show, minimize and close controls](../images/other-windows-light.png#only-light)
![The other-windows list: each open viewer with its show, minimize and close controls](../images/other-windows-dark.png#only-dark)

**Closing.** ++ctrl+w++ closes the open book or Set and returns to the opening screen. Closing the
window asks about unsaved work, and refuses while a cook, export or upload is running.

**Errors.** Problems are reported in plain sentences on a card or under the control concerned, never
as a stack trace.
