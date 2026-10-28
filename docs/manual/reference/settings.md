# Settings and where nfty keeps things

## The Settings card

Press the gear in the title bar. It has one control: **worker threads**, how many threads heavy work
uses (reading and writing archives, cooking, exporting, building models). **Auto** uses two thirds of
the machine's logical processors; a custom value lets you pick a number. The count never changes what
is written: the same book and seed give the same bytes at any thread count.

If the `NFTY_THREADS` environment variable is set, it wins for the session: the card is locked and
says so. The command line takes `--threads N` or `max` on every command, and falls back to the same
variable.

## The `.nfty` folder

nfty keeps its state in a `.nfty` folder, normally **beside the program**, so a copy carries its own
history. Files inside:

| File | Holds |
|---|---|
| `recents.json` | The recent-files list. |
| `palette.json` | Your saved swatches, one list for Gray and one for Color. |
| `editor-view.json` | The editor's remembered view choices. |
| `settings.json` | The thread count you saved. |
| `viewer-window.json` | The viewer window's size and position. |

Delete the folder and everything starts fresh. Your books and Sets are the files you chose, wherever
you put them, and are never stored here.
