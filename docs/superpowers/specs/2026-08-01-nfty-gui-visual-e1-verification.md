# E1 verification

**PARTIAL — 5 of 10 claims fully confirmed, 4 partial, 1 false in its stated context (the Explorer ingredient table is still clipped); plus one new legibility regression on Landing.**

Real, measurable movement — but less than claimed, and one claim is effectively false in the place the audit observed it. Group (A) is the strongest: both Fluent-blue controls are genuinely gone (a chroma-hunt for #0078d7 ±24 across all 24 frames returns only 4–5 stray anti-aliasing pixels in three frames, none on a control), the disabled-button grey slab is genuinely replaced by opacity over the real surface (arithmetic-exact), and the Landing accent label went from illegible to 6.85:1. Group (B) landed the metrics but not the application — the scale is right where it was applied, `pane-h` was added and applied to nothing, and the two headings it existed for still render at 14px full-strength. Group (C) is 2 of 3: the recipe-table collision and the set-browser reflow are genuinely fixed and hold at the captured width; the ingredient-table clipping is fixed only in the standalone frame and is still broken inside the Explorer — the primary screen and the exact frame the audit cited. The scrim change is real in source but has zero pixel evidence because the capture harness never renders MainWindow. Call it ~64% (from ~55%): the palette is now nearly clean and the type metrics exist, but structural grammar (pane headers, icons, Landing/detail composition) is untouched, and one new legibility regression was introduced.

## Claims

### [CONFIRMED] ToggleButton ControlTheme kills Fluent #0078d7 on the wizard aspect-lock in both themes

wizard-cookbook-light.png at the audit's own coords (396,178) is now #f1e2de and (415,198) #f8f3ed; dark (396,178)=#210f14. #f1e2de is the exact composite of AccentWashBrush #14a11f31 (alpha 0.0784) over PanelBrush #f8f3ed (computed 241,226,222); dark #210f14 is the exact composite of #26a11f31 over #0b0c10. A global colour hunt for #0078d7 ±24 across all 24 PNGs returns only 4–5 isolated pixels (#128ec0, #0774c2) in cookbook-detail-dark/gallery-dark/set-browser-dark — subpixel AA fringes, not control fills. Cropped at 5x the toggle is an accent-wash rounded square with an accent-line border.

### [CONFIRMED] ProgressBar ControlTheme kills Fluent #0078d7 on the recipe weight bar in both themes

wizard-recipe-{light,dark}.png at the audit's coords x=300, y=200..203 are now PanelBrush (#f8f3ed / #0b0c10) and the bar itself at (300,207) is #a11f31 = AccentBrush exactly, identical in both themes. Caveat, not a failure of the claim: the bar is still a stock ProgressBar — measured 4px tall x 380px, square-ended, with no track visible at 100% — where wizard-recipe.html .bar is 7px tall, radius 3px, background var(--tile), with .me/.other weighted segments and a .legend dot+name+pct list. Controls.axaml:212 explicitly defers that to the wizard-restructure slice, so the colour claim holds and the shape does not.

### [CONFIRMED] Disabled buttons now dim by OPACITY over the real panel surface instead of Fluent's opaque grey slab (previously greyed the whole ingredient-editor tool column)

editor-paint-light.png disabled 'Brush' tool interior is uniformly #f6f1ea = PanelBrush #f8f3ed at 0.38 over bg #f4efe8 (computed 245.5); dark is #08090d = #0b0c10 at 0.38 over #07080b (computed 8.5). The disabled accent 'Save' is #d19a9b / #481824 — accent-tinted, not grey. Compare the pre-slice frame still in scratchpad (explorer-light.png, older build) where 'Delete'/'Delete variant' are flat grey slabs. The audit's cited values #c3bfba/#39393c no longer appear as any control fill. The tool column is visibly a translucent panel run, not grey.

### [PARTIAL] Added a Landing 'soon' tier for actions whose destination does not exist yet

The background is genuinely transparent: the 'New Kitchen…' interior samples #f4efe8 at 99.15% in light and #07080b at 99.00% in dark — pure page bg, exactly landing.html .tbtn.soon{background:transparent}, distinct from the enabled 'New Recipe' interior which is 100% #f8f3ed PanelBrush. But the mockup's defining property, border-style:dashed, is NOT present: scanning the top border row y=189 from x=130..170 gives an unbroken run of #e3ded8 with no gaps. #e3ded8 is LineStrong at 0.38 (0.076 effective alpha over bg), i.e. a solid line dimmed by the new opacity rule. Styles.axaml:88-91 openly documents this as an approximation (Avalonia's Border has no dash-pattern property).

### [CONFIRMED] Fixed the pre-existing bug where the Landing accent button's label rendered near-black on maroon (illegible)

landing-light.png box (130,154)-(230,170): the label core is #f7f2ec = OnAccentBrush exactly, on #a11f31 = AccentBrush (67.6% of the box). Brightest ink measured against the accent fill gives 6.85:1 contrast. A 3x crop of the button shows crisp cream 'New CookBook' on maroon. Identical histogram in the dark frame, as expected for a theme-invariant accent fill.

### [PARTIAL] Added TextBlock.slbl/.pane-h/.plabel/.tagline/.sub-h (10-11.5px, uppercase, tracked, muted) and applied them, replacing bare Classes="muted" headings and inline FontSize/LetterSpacing

Metrics landed where applied: landing 'CREATE' ink height dropped from the audit's measured 10px to 7px (x94..134, y120..126) — exactly a 10px font; 'OPEN' and 'RECENT' likewise h7; .sub-h headings ('MINT DISTRIBUTION' h8, 'LAYERS' h8, 'RARITY' h8, 'COLORWAYS' h8) all read as 10.5px mono; .plabel ('Value', 'Brush size') h7. Zero inline LetterSpacing remains anywhere in Views/. Tracking values are arithmetically correct against the CSS (.slbl .14em@10px=1.4, .sub-h .12em@10.5=1.26, .plabel .1em@10=1.0). What is overstated: (1) TextBlock.pane-h is defined at Styles.axaml:257 and referenced by NO view — grep across Views/ returns nothing — so it is dead code; (2) the two headings it was added for, ingredient-editor.html:290/346 `.pane-h` 'Variants' and 'Colorize', still render wrong — editor-paint-light.png 'Colorize' ink is h10 (~14px, full-strength FgBrush), the exact defect the audit described, and 'Variants' got .plabel instead of .pane-h; (3) mockup .plabel is text-transform:uppercase, but every consuming view passes mixed-case strings ('Variants', 'Value', 'Brush size', 'Hue range', 'Weight', 'Resulting mix'), so no .plabel in the editor or any wizard is uppercase. Styles.axaml:247 asserts 'the consuming view supplies already-uppercased text' — no view does.

### [FALSE] Fixed clipped 'OVERALL'/'composited as-is' text in the ingredient detail

Fixed in the standalone frame only. ingredient-detail-light.png does show a full 'OVERALL' header and a single-line 'no colorize · composited as-is' in the rail — both good. But the audit's finding is titled 'Ingredient detail / Explorer' and its evidence was explorer-light.png, and there it is still broken: the header row reads 'WEIGHT  IN RECIPE  O' with OVERALL sliced, the VARIANT column header and the variant name 'A' render at zero width (entirely absent), and the 50% overall value is gone. Measured: header ink runs x370..569 and the colorways rail's left edge is at x=569 — the table runs under the rail. The reason is that the fix is a no-op on total width: IngredientDetailView.axaml:38/57 changed '48,*,80,100,100' to '48,*,Auto,Auto,Auto' but moved the same 80/100/100 onto MinWidth of the cell TextBlocks (lines 46,48,49,60,61,62), so the minimum is still 48+80+100+100 = 328px — precisely the 328px the audit called out — against ~276px available. The star column therefore collapses to 0. Only the hero-subtitle half of the finding landed (TextTrimming now gives 'no colorize · c…' with an ellipsis instead of a hard mid-word cut) and the rail 280→Auto/MinWidth(208) change did resolve the colorways collision.

### [CONFIRMED] Fixed colliding '1bg'/'2aura' columns in the recipe layer table

recipe-detail-light.png rows now read '1  bg' and '2  aura' with clear separation — index ink ends ~x=53 and the name starts ~x=66, roughly the mockup's 10px table cell padding. Ink measurement over the row (x49..80) shows two distinct glyph groups. The header '# LAYER' is spaced consistently. Same in the dark frame.

### [CONFIRMED] Fixed the sliced 4th tile in the set browser (now reflows)

set-browser-{light,dark}.png now show 3 tiles x 2 rows, all six whole, none touching the pane edge — #0004/#0005/#0006 are complete with captions, and nothing is cut at x=900. The arithmetic checks out at the captured width: available = 900 - 32 margin - 300 rail - 16 = 552; the new RowChunkConverter computes (552+10)/(132+10) = 3.95 → 3 per row, and 3*132 + 2*10 = 416 ≤ 552, so it fits with headroom rather than by luck. It is a real IMultiValueConverter over ListBox Bounds.Width, not a hard-coded 3. The header metadata is now wrapped in Border.idchip chips ('6 items', 'Seed seed1'), matching CookBookDetail's chip grammar as claimed.

### [PARTIAL] Tokenised the modal scrim (was raw #88000000; now ScrimBrush = 52% veil of the page background per the mockup's .scrim)

Source-correct, pixel-unverifiable. Tokens.axaml defines ScrimBrush #85f4efe8 / #8507080b — alpha 0x85 = 133/255 = 52.2%, matching help.html:140 `.scrim { background: color-mix(in srgb, var(--bg) 52%, transparent) }`, and MainWindow.axaml:44 now binds it (commit da16733 removed the last raw hex outside Tokens.axaml). But the scrim lives on MainWindow's DialogScrim Panel, and tests/Nfty.App.Tests/VisualCapture.cs:484 hosts every captured view directly in a bare Window — MainWindow is never instantiated. Confirmed empirically: the area outside the wizard card samples 100% pure #f4efe8 / #07080b over a 180x100 region with no trace of underlying content, i.e. no scrim is composited in any of the 24 frames. Zero rendered evidence either way, and an untested risk: a 52% light-on-light veil may not read as modal at all.

## New issues

### [High] landing-light.png / landing-dark.png

REGRESSION — the new Button.soon foreground rule stacks with the new blanket `Button:disabled { Opacity 0.38 }`, double-dimming 'New Kitchen…' into near-invisibility. Darkest ink is #bbb8b4 on #f4efe8 = 1.73:1 contrast (dark theme: #494848 on #07080b = 2.20:1). The mockup's .tbtn.soon is fg-muted at FULL strength with no opacity (it is a tier, not a disabled state) — which computes to ~6.9:1. Worse, the sibling 'Open a cooked .set…' carries the identical `landing tbtn soon` classes but its command is enabled, so it escapes the opacity and renders at 6.93:1 — two buttons in the same declared visual tier differ by 4x in contrast on the same screen.

**Fix:** Do not let .soon buttons take the :disabled opacity. Either drop Opacity from the compound selector (add `Button.tbtn.soon:disabled { Opacity: 1 }` after the blanket rule, relying on the already-muted foreground for the tier signal), or stop disabling the commands and make .soon purely presentational. Then normalise both Landing soon buttons to the same ~6.9:1 fg-muted.

### [High] explorer-light.png / explorer-dark.png

The ingredient variants table still overflows its pane and runs under the colorways rail — VARIANT header and the variant name render at zero width, OVERALL is sliced to a single 'O' at x=566..569 where the rail begins. This is the audit's own [High] finding, in the frame the audit cited, and the slice's change did not alter the width budget (328px of minimums vs ~276px available).

**Fix:** Give the numeric cells real budget rather than MinWidth: drop the MinWidth on the WEIGHT/IN RECIPE/OVERALL TextBlocks (IngredientDetailView.axaml:46,48,49,60,61,62) to something the pane can afford (~56/72/64) or make them proportional star columns, and set MinWidth on the star name column so it can never collapse to 0. Verify by re-rendering explorer-*.png, not ingredient-detail-*.png.

### [Medium] editor-paint-light.png / editor-paint-dark.png

The disabled-grey fix is Button-only, so the two remaining grey slabs are now the most conspicuous things on the screen. The disabled 'Brush size' NumericUpDown fills #c7c3be (light) / #3c3d40 (dark) — the very values the audit flagged — and the disabled 'Value' Slider track paints #cccccc / #333333, pure neutral greys that appear nowhere in Tokens.axaml. A chroma<4 scan across all 24 frames finds #cccccc at 566px in editor-paint-light as the single largest genuinely off-palette neutral region left in the app.

**Fix:** Extend the same treatment to the other ControlThemes in Themes/Controls.axaml: reset NumericUpDown/TextBox and Slider PART_ backgrounds and track brushes to their rest-state token values under ':disabled' and let the ancestor opacity do the dimming, as was done for Button.

### [Medium] editor-paint-light.png / editor-paint-dark.png

Heading hierarchy is now inverted in the editor rail. 'Colorize' and 'Preview' still render at ~14px full-strength FgBrush (measured ink h10) while every control label beneath them is now a 7px-ink micro-label — so a section heading and its own controls read as two unrelated systems, and the mockup's .pane-h (mono 10.5px, .14em, uppercase, fg-muted) is defined in Styles.axaml but applied nowhere.

**Fix:** Apply Classes="pane-h" to IngredientEditorView's 'Colorize' and 'Preview' and change 'Variants' from plabel to pane-h, passing already-uppercased text ('VARIANTS', 'COLORIZE', 'PREVIEW'). Same for ExplorerView's pane headers once they exist.

### [Low] all wizards, editor-paint-*, wizard-recipe-*, wizard-ingredient-*

Every .plabel renders mixed-case ('Variants', 'Value', 'Brush size', 'Hue range', 'Saturation range', 'Hue buckets', 'Sat buckets', 'Fixed colour', 'Weight', 'Resulting mix') where ingredient-editor.html:190 specifies text-transform:uppercase. The .slbl and .sub-h consumers were correctly uppercased; .plabel was missed, so the tracked-micro-label look only half-reads.

**Fix:** Uppercase the literal strings at the ~12 .plabel call sites in IngredientEditorView.axaml, NewIngredientView.axaml and NewRecipeView.axaml, matching the convention already used for .slbl/.sub-h/.data-h.

### [Medium] explorer-*.png, ingredient-detail-*.png, wizard-cookbook-*.png

Emoji glyphs are now the app's last source of theme-invariant off-palette colour, and the aspect-lock fix made one of them obvious: the toolbar padlock samples #f6cd6a (light) / #c49932 (dark) bright yellow; the newly-correct accent-wash aspect-lock toggle contains a chain glyph rendered at #9b9b9b/#bebebe identical greys in BOTH themes, ignoring the AccentTextBrush the ControlTheme sets (wizard-cookbook.html:93 wants color:var(--accent-text) on an inline SVG); the ingredient-detail edit button shows an orange pencil on maroon. Not caused by this slice, but its chrome fix now frames the wrong-coloured glyph.

**Fix:** The icon-system slice: replace emoji Content with monochrome inline Path/PathIcon geometry that inherits Foreground, so glyphs take AccentTextBrush/FgMutedBrush per theme.

### [Low] cookbook-detail-light.png / cookbook-detail-dark.png

The mint-distribution bar segments sample #975cb8 and #b8945c, byte-identical in light and dark and matching no token (KindCustomBrush is #6d4f9c light / #c3a6ea dark). Two more theme-invariant off-palette hues, presumably generated per-recipe.

**Fix:** Derive segment colours from the Kind* token brushes (or an accent/fg-muted ramp per the mockup's .distbar) resolved per theme variant, rather than from a hardcoded generator.

### [Low] help-light.png / help-dark.png

Help was untouched by the type-scale pass — 'Quick reference' is still Classes="muted" at 14px where the mockup treats it as a tagline, and the body is one undifferentiated 3-line paragraph against help.html's multi-column .slbl-headed sheet.

**Fix:** Belongs to the Help sheet slice; at minimum swap HelpView.axaml:14 to Classes="tagline" while nearby.

## Next slice

Attack **Explorer pane grammar** next, and fold the detail-pane width budget into it. Rationale from what the frames now show: (1) Explorer is the primary screen and the only one still displaying a visibly broken widget — the ingredient table clipped to 'O' and running under the rail — so it is the largest single remaining credibility gap, and it is cheap (a column-budget change in IngredientDetailView, already diagnosed above); (2) the slice just added TextBlock.pane-h and applied it to nothing, and Explorer is exactly where the mockup's .pane-h column headers live, so this slice pays off dead code immediately and fixes the inverted hierarchy in the editor rail at the same time; (3) with the palette now nearly clean, structural grammar is what still reads as 'not the mockup' — Explorer has no pane headers, no pane chrome, and a raw toolbar. Do the Landing soon-tier contrast fix as a one-line rider in the same PR (it is a live legibility regression this slice shipped and should not wait for the Landing restructure). Then take the **icon system** second: after this slice, emoji glyphs are the last theme-invariant off-palette colour source in the app (yellow padlock, grey chain inside a now-correct accent-wash toggle, orange pencil), and replacing them with Foreground-inheriting Path geometry closes the colour audit completely. Defer the wizard segmented-bar/legend and the Landing restructure until then — both are composition work that will churn the same files.
