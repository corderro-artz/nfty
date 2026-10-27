# Task 18 report: Export a Menu from the CookBook card

Built:
- `MenuExportDialogViewModel` + `MenuExportDialogView` (modal small): heading, sentence, Atlas/Loose tray + one muted line each, pathstrip picker (default `{book}.mnu` beside `.cbk`), always-laid-out ProgressBar, one reserved 66px message line (warning ink toggles), Cancel/Close + Export.
- Calls `MenuWriter.WriteAsync` through `AtomicFile.ReplaceAsync` (temp beside, move over). No second rule.
- Invalid book: `Validator` asked up front; Export disabled, reason + first problem in reserved line. Gate re-checked in the command.
- Atlas too large: writer's `CheckAtlasFits` message verbatim + " Choose Loose above."
- Success: status `Wrote {file} ({size}).`, card closes. Works on locked book (reads only).
- CookBook card: `Export Menu…` tbtn beside Reports; `exportMenu` callback (5th optional ctor arg) wired in `ExplorerViewModel`.
- `Core/Publish/SizeText.Of(long)` added (brief assumed it existed; it did not). `ExportPlan.SizeText` now delegates, same output.
- Fixtures: DarkModeContrast screen `dialog-menu-export-refused`; VisualCapture frames `zz-dialog-menu-export[-quiet]-{light,dark}`; 4-arg CookBook card fixtures given the callback so the button is not captured disabled.

Deviations / brief mismatches:
- `MenuWriter.WriteAsync` has no `parallelism` param, `IParallelismSetting` does not exist here (T7 not in 2c092ae). Omitted.
- Writer not atomic itself; atomicity done in the VM via `AtomicFile`.
- Progress bar full after refusal: `Progress<T>` posts late; callback ignores reports after run end, reset on failure.

Tests (`MenuExportDialogTests`, 8): atlas journey (dialog -> .mnu -> MenuArchive -> Ticketer roll -> MenuRenderer pixels == CLI-path menu), loose, atlas refused (16385x1 canvas), locked, invalid, button opens card (real pointer press, locked book), fits (x2 states).
Targeted + sweeps (MenuExportDialog, ModalFit, DarkModeContrast, ToolbarButtonParity, MinimumWindowFit, DnaSpaceLayout, TextBaseline, WiringCoverage, DeadCodeAudit): 44 pass. Wider (Explorer, CookBookDetail, ExportDialog too): 169 pass. VisualCapture 23 pass. Core Export/Menu: 65 pass. Build 0 errors (108 pre-existing NU190x warnings).

Probes: (1) `Mode` always Atlas -> loose test + refused-then-loose test fail. (2) drop `_unavailable` gate from CanExport -> invalid-book test fails. Both reverted.
Red-first not done: implementation written before tests (green first run).

Frames: `.superpowers/frames-t18/` (gitignored). Viewed refused dark/light, quiet light, card dark/light.
