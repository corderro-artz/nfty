# Stage 5 — Nfty.App review

Status: both review passes complete on 2026-11-06. **Implementation approved after publishing v2.4.8; no App fixes implemented at this checkpoint.** Baseline: `cf67cce`, local tag `audit-cli-verified-2026-11-06`. The worktree remains isolated; nothing has been merged or pushed.

## Result

The review identified **21 behavior findings: five P1 and 16 P2**, plus 20 grouped documentation corrections and 12 bounded simplification proposals. Twenty behavior groups have local executable counterexamples; the macOS replacement failure is a source-confirmed conditional failure, without native execution. Passing existing tests does not cover these counterexamples.

The highest-priority defects change authored constraints or lose work: filtered rule actions address the wrong rule, Undo after Save remains clean, concurrent edits can be marked saved, and modal/chrome actions can abandon running operations or dispose their borrowed images. No additional RNG, DNA, mesh, colorization or generation arithmetic defect was established. Those algorithms remain unchanged.

## Coverage and progress

- [x] Inventory all 223 production C#/AXAML files, 48,136 lines, project configuration and 252 companion C# test files.
- [x] Review authoring/editor/details/models/imaging: 36 files, 12,353 lines.
- [x] Review Rise/viewer/preview/Set browsing and corresponding views: 57 files, 13,586 lines.
- [x] Review cook/export/upload/settings/landing/wizards/shell/help/dialog workflows: 62 files, 9,529 lines.
- [x] Review shared services, controls, converters, themes and composition root: 68 files, 12,668 lines.
- [x] Pass 1: inspect correctness, math, state transitions, ownership, deterministic output boundaries and hot paths; inspect relevant tests and reproduce actionable failures.
- [x] Pass 2: separately inspect XML contracts and concrete simplification opportunities.
- [x] Run fresh Release build and App tests; inspect rendered frames and targeted failing interactions.
- [x] Update the total tracker and stop before implementation.
- [x] User approves implementation after publishing the accepted checkpoint as v2.4.8.
- [ ] Delegate approved fixes, verify preservation and report before any merge/push.

Three `gpt-6.1-sol` reviewers owned separate App slices; the main agent owned shared infrastructure and synthesis. Source inventories and detailed sub-review evidence remain in ignored `artifacts/audit/`. All owned production files received both passes. Relevant tests were read alongside their production owner; the 252-file test inventory is not a claim that every test received a separate two-pass audit.

The briefing, backlog and historical GUI handoff were consulted. Running behavior is the appearance reference. Archived designs, frozen fixtures, package versions and completed engine algorithms were not changed.

## Pass 1 — behavior findings

Each remedy below is proposed, not implemented. P1 means a high-priority risk to authored data or an active operation; P2 means a material workflow, lifetime or presentation defect.

### A01 — P1: filtered/sorted rule actions mutate a different authored rule

**Source:** [RecipeDetailViewModel.cs:618](../../src/Nfty.App/ViewModels/RecipeDetailViewModel.cs#L618), edit at 629 and delete at 658/671.

`IndexOfRule` searches the filtered/sorted `Rules` list, then uses that index against manifest order. With an Exclude followed by a Require, filter ALWAYS and delete the sole displayed row: the Exclude is deleted while the displayed Require remains. Editing has the same index mismatch.

**Remedy and proof:** resolve the row against `_allRules` by identity; validate that identity again after a dialog. Test edit/delete under every filter and both sort directions, including equal-looking rules. Preserve authored order and all RuleEdits validation; do not change the roller.

### A02 — P1: Undo/Redo after Save changes pixels without marking work dirty

**Source:** [IngredientEditorViewModel.cs:1511](../../src/Nfty.App/ViewModels/IngredientEditorViewModel.cs#L1511).

After fill → Save → Undo, the canvas reverts, the archive does not, `IsDirty` stays false and Save remains disabled. Leaving loses the reverted draft without a prompt. `AfterHistoryMove` refreshes the picture but never marks the change dirty.

**Remedy and proof:** track the saved history position or conservatively mark successful history moves dirty. Test Undo and Redo after Save for both paint surfaces and save destinations; no-op history actions must remain no-ops. Preserve exact restored pixels and per-variant histories.

### A03 — P1: edits during Save are incorrectly marked saved

**Source:** [IngredientEditorViewModel.cs:1702](../../src/Nfty.App/ViewModels/IngredientEditorViewModel.cs#L1702), clean transitions at 1775/1807.

Save snapshots the draft before asynchronous I/O, but editing remains enabled. A later name or pixel edit is omitted from the saved snapshot; completion unconditionally clears `IsDirty`. The probe writes `snapshot`, types `typed during save` during the await, and finishes with different archive/editor names but a clean editor.

**Remedy and proof:** capture a mutation revision with the snapshot and clear dirty only if unchanged, or coherently suspend all mutations during Save. Hold a save, edit name/pixels/structure, then verify the first snapshot and that subsequent edits remain dirty and survive a second save. Keep snapshot ownership and save joining.

### A04 — P1: Escape/scrim dismissal bypasses running-job protection

**Source:** [ShellViewModel.cs:278](../../src/Nfty.App/ViewModels/ShellViewModel.cs#L278); scrim routing in [ShellChromeView.axaml.cs:29](../../src/Nfty.App/Views/ShellChromeView.axaml.cs#L29).

The shell special-cases BusyViewModel but directly closes other cards. With a running Cook card, Escape clears Active while the job remains busy; ConfirmClose changes from false to true. Cook, export/upload and Menu operations can lose their progress UI and then be terminated during output mutation.

**Remedy and proof:** enforce one busy-card dismissal rule at shell entry points. Preserve Busy cancellation as a request and retain the card until unwind. Test actual Escape/scrim routes for every running card, idle dismissal, cancellation and rollback with a held writer.

### A05 — P1: shell chrome can dispose a running job's book or orphan its dialog

**Source:** [ShellViewModel.cs:250](../../src/Nfty.App/ViewModels/ShellViewModel.cs#L250), Settings at 254 and CloseDocument at 325; [ShellChromeView.axaml:37](../../src/Nfty.App/Views/ShellChromeView.axaml#L37); [DialogService:40](../../src/Nfty.App/Services/IDialogService.cs#L40).

The titlebar/footer sit outside the body scrim. CloseDocument remains executable during Cook: the probe closes the session and observes its borrowed image disposed while Cook is still active/busy. Help/Settings can replace an active modal; DialogService overwrites its only completion source, leaving the original result permanently pending.

**Remedy and proof:** guard chrome navigation/modal launch while a modal owns the flow, including execution guards and availability notifications. Give DialogService explicit non-orphaning replacement/stack semantics. Test real logo/Ctrl+W and Help/Settings over held jobs and wizards; preserve normal close/disposal order. Include structural saves in the close-lifetime assessment, not only modal jobs.

### Remaining P2 findings

| ID | Source | Reproduction and impact | Proposed remedy and regression boundary |
|---|---|---|---|
| A06 | [IngredientDetailViewModel.cs:666](../../src/Nfty.App/ViewModels/IngredientDetailViewModel.cs#L666) | Select variant v1 and export its preview: the PNG contains v0 because `_variants[0]` is hard-coded. | Snapshot the selected id before the picker; retain VariantPreview rendering. Test second/sorted selection and selection changes during the picker. |
| A07 | [IngredientEditorViewModel.cs:172](../../src/Nfty.App/ViewModels/IngredientEditorViewModel.cs#L172), [References.cs:300](../../src/Nfty.App/ViewModels/IngredientEditorViewModel.References.cs#L300) | Save-triggered refresh appends reference rows (1 → 2 → 3) and disposes enabled stack caches without marking them dirty, so reference ink disappears. | Reconcile by stable key, retain enabled/placement choices, rebuild lookup and invalidate stacks. Test two saves with sibling and Kitchen references, membership changes and identical composite pixels. |
| A08 | [IngredientEditorViewModel.cs:1760](../../src/Nfty.App/ViewModels/IngredientEditorViewModel.cs#L1760), disposal at 1968 | Loose Save replaces `_ing`, but `_ownedBook` still owns the original graph. Save then close leaves the latest images alive. Failure paths also abandon prospective image copies. | Explicit prospective/current ownership with adoption after success and failure cleanup. Allocation-baseline tests for repeated saves, close and failed loose/in-book writes; never dispose shared session images. |
| A09 | [ExplorerViewModel.cs:1229](../../src/Nfty.App/ViewModels/ExplorerViewModel.cs#L1229), ingredient branch at 1257 | Same-id import replaces a subtree without disposing its orphaned images; the old ingredient survives session close. Incoming ownership is also abandoned before persistence succeeds. | Retain incoming ownership through adoption; dispose only orphaned old subtrees after success and incoming graphs after failure. Test new/replacement/failure cases for Recipe and Ingredient; preserve unaffected image references. |
| A10 | [ExplorerViewModel.cs:57](../../src/Nfty.App/ViewModels/ExplorerViewModel.cs#L57), [IngredientDetailViewModel.cs:561](../../src/Nfty.App/ViewModels/IngredientDetailViewModel.cs#L561) | Unlocking changes the Delete command's predicate, but the existing Ingredient pane's real Button remains disabled: no CanExecuteChanged is forwarded. | Call the existing notification method. Test lock/unlock/relock without rebuilding the pane or losing selection. Retain the authoritative deletion lock check. |
| A11 | [ExplorerViewModel.cs:759](../../src/Nfty.App/ViewModels/ExplorerViewModel.cs#L759), [RecipeDetailView.axaml.cs:101](../../src/Nfty.App/Views/RecipeDetailView.axaml.cs#L101) | Rule/chance commits bypass `_reordering`. Two overlapping whole-book edits lose one change or one write fails. Enter plus focus departure can submit a chance twice. | Use the existing structural-write gate before deriving snapshots; refuse busy or serialize against the live graph. Test rule/rule, chance/blur and rule/reorder overlap, plus close lifetime during a held structural save. Do not queue stale snapshots. |
| A12 | [ExportDialogView.axaml:92](../../src/Nfty.App/Views/ExportDialogView.axaml#L92), [ExportDialogViewModel.cs:154](../../src/Nfty.App/ViewModels/ExportDialogViewModel.cs#L154) | Clicking the models checkbox makes it visibly unchecked, but its one-way ModelsShown binding never updates Models. A real export still writes its GLB. | Add guarded write-through behavior while preserving disabled/unticked appearance on unmodeled Sets. Click both ways and verify Options, Parts and actual exported/uploaded content. |
| A13 | [SettingsDialogViewModel.cs:293](../../src/Nfty.App/ViewModels/SettingsDialogViewModel.cs#L293), result at 391 | Edit the node address during a held check; the old endpoint's response changes the new, untested endpoint to Connected. | Cancel/invalidate the old check or compare checked settings identity. Test reversed completion order and provider changes with fakes; retain explicit Test semantics. |
| A14 | [ViewerViewModel.cs:370](../../src/Nfty.App/ViewModels/ViewerViewModel.cs#L370), [LiveSubject.cs:99](../../src/Nfty.App/Viewer/LiveSubject.cs#L99) | Live Save borrows art before awaiting a picker. A source refresh disposes it; returning from the picker produces ObjectDisposedException instead of a PNG. | Own a cloned snapshot across the await. Test refresh/disposal during picker, retaining unchanged PNG bytes, enlargement behavior and seal policy. |
| A15 | [ViewerViewModel.cs:63](../../src/Nfty.App/ViewModels/ViewerViewModel.cs#L63) | Live model arrival leaves the rendered Export button disabled and Both dimmed despite true predicates; model loss can leave ThreeD selected and valid 2D art hidden. | Notify every model-derived property/command and apply the existing layout fallback when the model disappears. Test both transitions in a rendered view; preserve camera and layout on ordinary model updates. |
| A16 | [SetInspectView.axaml.cs:125](../../src/Nfty.App/Views/SetInspectView.axaml.cs#L125) | Wheel zoom assigns pan before scale. Immediate Sync clamps against old Fit limits, losing a valid anchor: expected Y −11.18, observed 0 at 1 → 1.2. | Compute from the old transform, apply new scale then the calculated pan, or synchronize once. Test actual wheel anchors from Fit and pan limits; preserve clamp and step rules. |
| A17 | [RiseView.axaml:649](../../src/Nfty.App/Views/RiseView.axaml#L649), [ModelVoxelSize.cs:14](../../src/Nfty.Core/Publish/ModelVoxelSize.cs#L14) | The control minimum is 0.01 and format is 0.##; the canonical minimum is 0.001 with three decimals. Typed 0.001 is refused; accepted 0.123 displays 0.12, and 0.001 displays 0. | Bind canonical bounds and three-decimal formatting. Exercise the real field at min/0.123/max; retain existing rounding and identical model bytes for previously accepted values. |
| A18 | [AppSettings.cs:39](../../src/Nfty.App/Services/AppSettings.cs#L39) | Reads accept case-insensitive keys, but Set adds a canonical key beside a valid PascalCase alias. Reload reads the earlier alias: Light reverts to Dark, canvas 128 to 64, and provider/API changes also revert. | Remove all case-insensitive aliases of the selected key before writing its canonical form. Test every settings owner, existing duplicates and unknown-key preservation. |
| A19 | [ViewerWindowPlacement.cs:19](../../src/Nfty.App/Services/ViewerWindowPlacement.cs#L19), Desktop caller at ViewerWindow.axaml.cs:102 | Saved sizes are DIP; working areas and positions are pixels. At 2×, a 960×540 window on a 3840×2160 screen resolves to (2880,1500), whose actual right/bottom become (4800,2580). | Carry each candidate screen's scaling into overlap/clamp arithmetic. Preserve saved JSON and pixel positions; test 100%, 200%, mixed DPI and monitor removal. A minimal Desktop caller update is required; this does not constitute the full Stage 6 review. |
| A20 | [SecretStore.cs:251](../../src/Nfty.App/Services/SecretStore.cs#L251) | SecretTool.Delete ignores process failure; a stand-in returning exit 2 is treated as success. ForgetPinataKey clears the UI hint although the stored key may remain. | Distinguish absence from command errors and throw a sanitized failure. Real libsecret can use exit 1 for either; inspect stderr presence without exposing its contents. Test success, absent/no-op and failed deletion; retain the hint on failure. |
| A21 | [SecretStore.cs:383](../../src/Nfty.App/Services/SecretStore.cs#L383) | macOS Write deletes the old credential before allocating/adding its replacement. If Add fails after Delete succeeds, Save reports failure but the previous key is gone. Source-confirmed; not executed on macOS. | Update the existing item with SecItemUpdate; Add only when absent. Exercise native status failures through a narrow seam and require native macOS verification before calling that path verified. Preserve service/account keys and secret handling. |

A08/A09 successful-path leaks were allocation-probed. Their additional exception-path ownership holes and the parallel Recipe import branch were traced statically rather than independently allocation-probed. A19 proves the pure calculation and caller's missing scale; no native mixed-DPI window was exercised. A20 uses a failed subprocess stand-in, never a real credential store.

## Pass 2 — documentation corrections

The compiler enforces comment presence, not truth or useful contracts. A Roslyn scan counted 1,682 handwritten public/protected method, constructor and property declarations and found 64 missing tag sets. This includes public members of private helpers; it is not 64 missing externally exposed APIs. Do not add filler comments to trivial private storage. Compiled App XML also contains ten members with multiple summary elements.

| ID | Correction |
|---|---|
| D01 | Replace Phase-1/stub/future-task claims in IFilePickerService, ServiceRegistration, NewIngredient, NewRecipe and the editor with their shipped behavior and persistence responsibilities. |
| D02 | Remove NewCookBook's obsolete schema-v1 compatibility promise; document optional omission under the actual v2 floor. |
| D03 | Move export workflow XML off CornerOption, cancellation XML off Busy.Counting, and the obsolete extra summary off WizardViewModelBase's constructor. |
| D04 | Consolidate conflicting type summaries contributed by IngredientEditor and Rise partials into one current ownership/behavior contract. |
| D05 | Correct duplicate/misattached summaries on LooseWorkspace, FactorChip.Tip, IngredientEditorView._gestureTool and RecipeDetailView.PendingReorder. Put gesture/handler contracts on their actual declarations. |
| D06 | Document borrowed graphs versus owned bitmaps on detail/Explorer/editor types; correct ExplorerNode.Domain's ingredient tuple and primary-record/cache meanings. |
| D07 | Correct VariantImagery's fixed-color branch, nonnullable fixedColor parameter, sampled Colorways meaning and ownership of returned bitmap elements. |
| D08 | Distinguish illustrative variant chips from legal DNA counts including color/rules/weights; replace the obsolete never-shortened/no-tooltip headline claim. |
| D09 | Describe authored rule identity separately from filtered display order; correct LayerKind casing, optional TableSort callback wording and any promised sort-reset UI that does not exist. |
| D10 | Make save/import adoption and exception ownership comments agree with A08/A09 fixes; document that persistence and hashing are separate steps, without overstating full-operation atomicity. |
| D11 | Correct Settings/export page counts, Landing PendingOpen and bundle-recents claims, current seal-tab/reveal comments and references treating archived art as authoritative. |
| D12 | Correct VariantRiseSource save exclusivity, RiseSubject texture mutation, RiseItem chip states, RiseSource.IsTwoDOnly and ModelSubjects snapshot-versus-builder lifetime. |
| D13 | Qualify Rise export byte-size promises for glTF-plus-bin: the final chosen stem affects JSON length and the estimate is already marked approximate. |
| D14 | Reconcile TwoDPane logical control units with device-pixel wording and the tested 1:1 fit floor. Do not change that floor as a documentation fix. |
| D15 | Correct StripPanel's unconditional no-clip/fewest-lines claims and O(n²) claim: RoomIn scans within the nested span loops. Describe the implemented cost/tie rule and oversized-group overhang. Qualify CanvasMapping's zero-pan-at-fit statement for letterboxed axes. |
| D16 | State ThumbnailGate's actual per-instance/per-operation limit; it does not enforce one global budget across cooks and multiple viewer/browser gates. |
| D17 | Add meaningful parameter/return contracts to AddNftyApp, CookBookPersistence.WriteNew, CookBookSession.Replace and LooseWorkspace write/wrap methods. |
| D18 | Complete KeyGlyphs.CapWidth, EstimateWidth and Spoken parameter/return descriptions; retain that width is an estimate tied to the current cap style. |
| D19 | Complete HelpMarkup/HelpCatalog helpers, HelpSection/HelpColumn constructors, NewIngredient builders, RangeAxis and RuleDialog builder contracts. Describe valid ranges, returned ownership and validation responsibility. |
| D20 | Complete ModelPreview constructor/gesture parameters, Rise IsHittable return meaning and PixelCanvasHost Geometry/Frame returns. Keep inherited override contracts and avoid duplicating private-field prose. |

The ordinary RowChunkConverter comment claiming a slice/no extra array is also false: the implementation allocates and copies a new array per row. This is commentary correction, not an XML defect. WindowFrame's Minimise/Maximise tooltip spelling also conflicts with the US-English house rule; normalize visible words while preserving icon resources and commands.

## Bounded simplification proposals

These are review recommendations. No broad UI framework, navigation rewrite, algorithm merger or mesh refactor is proposed.

| ID | Simplification | Preservation/verification |
|---|---|---|
| S01 | Remove unused eager IngredientDetail Colorways rendering, or make its public accessor lazy if compatibility requires it. The shipped view uses swatches/SelectedThumb instead. | Preserve displayed pixels and real color swatches; assert bridge/allocation counts and captured frames. Do not remove a public rendering helper incidentally. |
| S02 | Compute immutable HeroBars ordering once per detail lifetime. | Stable ties, exact unrounded shares, paging and selection identities remain identical. |
| S03 | Resolve rule indices with one authored-list scan instead of repeatedly filtering/sorting Rules inside a loop. | Part of A01; preserve display sort/filter behavior and authored order. |
| S04 | Use local prospective owners and try/finally adoption at existing save/import seams. | Part of A08/A09; dispose only owned/orphaned images. Avoid a generic dispose-old-book helper. |
| S05 | Replace the three hand-written row-copy loops with Enumerable.Chunk plus their typed row projection. | Keep existing identity caches, fallback four columns, Rise's three columns, stable order and short final rows. Readability proposal only; no claimed allocation/performance win. |
| S06 | Consider replacing the two pinned percent-to-width multipliers with existing ShareWidthConverter bindings. | Remove duplicated track widths only if measured output/geometry stays identical; preserve rounding behavior. No generation arithmetic changes. |
| S07 | Capture and dispose each cook/menu/check CTS at its operation's existing boundary. | Cancellation remains a request; no disposal while continuations still read the CTS, and no generic job framework. |
| S08 | Reuse the ExportPlan already computed by Refresh when checking sheet layout. | Preserve error precedence, upload WrittenOptions, Parts, SizeText and OutputName. Any speed improvement remains unmeasured. |
| S09 | Compute NewRecipe's mix rows/capped legend/omitted count from one projection per change. | Preserve sibling order, stable ties, five-row cap and floating-point addition order. |
| S10 | Make Viewer._preview readonly and remove the duplicate DepthNote notification. | Preserve camera identity and the effective notification sequence; no layout changes. |
| S11 | Share the composition root's repeated current-book-plus-recents candidate projection locally. | Preserve candidate priority, duplicates, deferred resolution and hash-only acceptance; do not introduce a locator cache. |
| S12 | Replace stale implementation-history essays with concise current contracts at the D01–D20 sites. | Keep load-bearing ownership, coordinate, determinism and cancellation explanations. Historical context remains in the briefing/audit; no indiscriminate comment deletion. |

Source-book hashing during every export Refresh, broader open-routing consolidation and additional range-notification consolidation remain measurement/policy-dependent candidates. The backlog's GPU, wall-emission, counting-walker, decoder and workspace decisions remain deferred. Similar-looking Variant/Recipe save paths retain different history/stale-state invariants and should not be merged merely to reduce line count.

## Verification and limits

Fresh checks at this unchanged App baseline:

- `dotnet build nfty.sln -c Release --no-restore`: zero warnings/errors (`app-review-build.log`).
- First full Release App run: **1,927 passed, one failed, one platform skip**. The failure was a test-case cleanup cross-thread exception in Avalonia.Headless's application/compositor initialization, reported against `RiseExportTests.Rise_and_export_word_sizes_the_same_way` (`app-review-tests.log`). No App source changed.
- Failed class rerun: **27/27 passed** (`app-review-cleanup-repeat.log`). Second full Release App run: **1,928 passed, zero failed, one Linux-only skip**, 5m02s (`app-review-repeat-tests.log`). The first failure remains recorded; a successful repeat does not prove the flake resolved.
- Existing focused reviewer selections: 119 authoring, 58 workflow and 637 Rise/viewer tests passed. Selections overlap; do not add these counts to the full suite.
- Ignored probes reproduce the reported defects; their passing assertions mean the defective behavior was observed, not repaired. Locations: `app-authoring-probe/`, `app-workflow-probe/`, `app-rise-probe/`, and `app-shared-probe/`.
- Both full App runs produced 431 capture PNGs. Representative shell, editor, recipe, cookbook, export, settings, Rise and viewer frames were visually inspected in both themes, alongside targeted live-model/voxel control captures. This is rendered-frame review, not a claim that every frame was manually inspected or all frames were byte-identical.
- The accepted CLI checkpoint retains the full-solution result of 5,228 passed/one platform skip and its 470-file output/culture/thread parity evidence. That full solution suite was not repeated here; App production and tests are unchanged.

A similar headless initialization thread-ownership race is reported upstream for the pinned framework: [Avalonia issue 22021](https://github.com/AvaloniaUI/Avalonia/issues/22021). The stack similarity is evidence for investigation, not proof that it caused this particular isolated-application cleanup failure. **H01: retain this intermittent harness failure as an unresolved verification issue; determine its cause before declaring the release gate consistently clean.** No dependency update or suppression was made.

Official references used for boundary checks: [Avalonia screen units/scaling](https://docs.avaloniaui.net/docs/app-development/window-management), [GNOME secret-tool source](https://raw.githubusercontent.com/GNOME/libsecret/main/tool/secret-tool.c), and [Apple keychain update guidance](https://developer.apple.com/documentation/security/updating-and-deleting-keychain-items). Context7 tools were not available; official sources and installed code were used instead. Secret-tool's extra newline is terminal-only, so redirected lookup output was checked and rejected as a false-positive newline defect.

Verification ran on Windows x64 using headless controls, temporary files and synthetic images. No native mixed-DPI desktop run, macOS keychain, Linux keyring, cloud upload, external model viewer, other CPU architecture or real-output process termination was exercised. Directly invoking a disabled generated Save command can bypass its predicate, but no actual sealed UI escape was found; that remains defense-in-depth, not a demonstrated product defect. Post-write cancellation in CookBookPersistence was not promoted without a current UI caller supplying cancellation.

## Stop point

Ticket, Core, Model and CLI are accepted local checkpoints. **App review is complete; the user approved A01–A21, D01–D20 and S01–S12 after publishing v2.4.8.** H01 remains an unresolved validation issue. No App fix, App commit/tag, Desktop review, merge or push has been performed.
