# nfty GUI — visual audit vs locked mockups

**Verdict:** Structurally recognisable but not close to 1:1 — the token layer is accurate and both themes are honest, yet a missing icon system, three untokenised Fluent control paths (including a raw #0078d7 in both themes), an unimplemented small-label type scale, and several screens that are still functional skeletons put the app roughly halfway to the locked mockups.

## Overall

The foundation is genuinely good and should not be re-litigated: the colour tokens are correct and correctly alpha-rotated for Avalonia's #AARRGGBB (I verified BgBrush #f4efe8/#07080b, PanelBrush #f8f3ed/#0b0c10, TileBrush #ece5db/#12141c, BgAlt2 #ede7df/#0f1118 and LineStrong at #c7c5bf-over-bg against the mockup's #12141833 by pixel sample), both themes swap cleanly on every surface, the accent is exactly #a11f31 on every accent button in both themes, the tree selection is a pixel-accurate accent-wash + inset 2px accent bar (#f2e2de light / #210e15 dark, matching the computed blend), the kmark D/S/C kind colours are right, mono and sans both resolve (Cascadia/Consolas + Segoe UI), and the metric band, data-h/data-row tables, rop/rchip rule chips, cw-panel, idchip and distbar are all assembled from the correct tokens, radii and padding. What dominates the remaining gap is not colour — it is four systemic absences that repeat on every screen: (1) there is no icon system at all, so the mockups' inline SVG becomes colour emoji, ASCII arrows and literal tofu boxes; (2) three untokenised control paths leak Fluent's own palette — #0078d7 on the aspect-lock ToggleButton and the weight ProgressBar in both themes, and an opaque grey disabled slab that turns the entire ingredient-editor toolstrip, the Explorer Delete button and the Landing Kitchen button into off-brand grey rectangles; (3) the 10–11.5px uppercase tracked label scale was never added to Styles.axaml, so every section heading renders at ~14px with no tracking (measured: \"CREATE\" cap-height 10px where the mockup implies ~7px) and the whole app reads loose and oversized; and (4) several screens are structurally unfinished rather than mis-styled — Help is a single paragraph where the mockup is a three-column sheet, the ingredient editor is a stack of default primitives where the mockup is an icon toolstrip plus dual-range gradient sliders plus an overlaid blip, and CookBook detail is missing its identity card, its combination-space column and its cook bar. Layered on top are three places where the app looks outright broken rather than merely off-spec: \"OVERALL\" and \"no colorize · compc\" hard-clipped in the ingredient detail, \"1bg\"/\"2aura\" colliding in the recipe layer table, and the fourth set-browser tile sliced at the pane edge. Fix the four systemic items and those three clipping bugs and the app jumps from roughly 55% to roughly 85% of the way to the mockups; the per-screen structural rebuilds carry it the rest of the way.

## Findings

### [Critical] All screens (system)

**Gap:** There is no icon system. Every mockup renders its glyphs as inline 12–18px stroked SVG (`.tbtn svg{14px}`, `.ti{18px}`, `.wc svg{14px}`, `.rico svg{14px}`, `.ttool svg{16px}`, `.rop svg{13px}`). The app substitutes Unicode text characters and colour emoji — and several of them are missing from the shipped fonts and render as tofu boxes.

**Evidence:** ingredient-detail-{light,dark}.png: the accent square in the hero draws a full-colour orange/pink ✏️ emoji — I sampled #ff822d inside it (explorer-light.png at 536,140), a colour that exists in no token. explorer-{light,dark}.png shows a colour 🔒 padlock (bright yellow in dark). recipe-detail-*.png: the dice button renders a hollow box-with-dot tofu glyph (IngredientDetailView/RecipeDetailView use Content="✏" and Content="⚄"); the same tofu appears in gallery-*.png's last icon button. landing-*.png uses ＋ (U+FF0B fullwidth plus), ↗, ↧ — the ↧ next to "Import…" reads as a bare capital 'I'. Mockup rule: landing.html `.tbtn .g svg{width:13px;height:13px;display:block}` with a stroked plus/arrow-out-of-box/download-tray path.

**Fix:** Add `src/Nfty.App/Themes/Icons.axaml` holding `StreamGeometry` resources traced 1:1 from the mockups' SVG `d` attributes (plus, arrow-out-of-box, download-tray, magnifier, lock, dice, pencil, chevron, book/recipe/ingredient type marks, window min/max/close, zoom ±, help). Add a `Style Selector="Path.ico"` setting Width/Height 14, `Stroke={DynamicResource FgMutedBrush}`, StrokeThickness 1.6, Fill=Transparent, plus `.ico.sm`(13)/`.ico.lg`(18) variants. Replace every `Content="<glyph>"` in LandingView, ExplorerView, RecipeDetailView, IngredientDetailView, IngredientEditorView, NewCookBookView and Nfty.Desktop/MainWindow.axaml with a `<Path Classes="ico" Data="{StaticResource ...}"/>`.

### [Critical] Wizards — New CookBook, New Recipe

**Gap:** Two controls paint Fluent's stock system blue #0078d7 in BOTH themes — the only non-palette hue in the entire app, and it is theme-invariant so it is equally wrong in light and dark.

**Evidence:** Pixel-sampled: wizard-cookbook-light.png and wizard-cookbook-dark.png both give #0078d7 at (396,178) and (415,198) — the aspect-lock `ToggleButton IsChecked Content="🔗"` (NewCookBookView.axaml:19). wizard-recipe-{light,dark}.png both give #0078d7 across y=200..203 at x=300 — the `ProgressBar` (NewRecipeView.axaml:19). Neither ToggleButton nor ProgressBar has a ControlTheme in Themes/Controls.axaml (which themes only TextBox, NumericUpDown, Slider, CheckBox, RadioButton, TreeViewItem), so both fall through to Fluent's SystemAccentColor. Mockup rule: wizard-cookbook.html `.link.on .lk{border-color:var(--accent-line); background:var(--accent-wash); color:var(--accent-text)}`; wizard-recipe.html `.bar .me{background:var(--accent)}` + `.other{background:color-mix(fg 18%)}`.

**Fix:** In `src/Nfty.App/Themes/Controls.axaml` add `ControlTheme x:Key="{x:Type ToggleButton}"` — rest = PanelBrush/LineStrongBrush/FgMutedBrush at 22×22 RadiusSm; `^:checked /template/ ContentPresenter#PART_ContentPresenter` = AccentWashBrush background, AccentLineBrush border, AccentTextBrush foreground. Replace the `ProgressBar` in NewRecipeView.axaml with a `Border` + horizontal `StackPanel` of weighted segments (AccentBrush for self, FgMutedBrush @ low opacity for siblings) plus the mockup's `.legend` dot+name+pct row, or add a ProgressBar ControlTheme setting Foreground=AccentBrush / Background=BgAlt2Brush.

### [Critical] All screens (system) — Landing, Explorer, Editor, Ingredient detail

**Gap:** Disabled controls render as Fluent's opaque grey slab, which reads as a filled, *active* button in a palette that has no grey. The mockup disables by opacity only, preserving the panel surface.

**Evidence:** Sampled disabled fills: landing-light.png "New Kitchen…" = #c3bfba (border #dcd8d1); landing-dark.png = #39393c; explorer-light.png "Delete" = #c1bdb7, dark = #3b3c40; ingredient-detail "Delete variant" identical; editor-paint-{light,dark}.png — the ENTIRE tool column (Brush/Eraser/Rectangle/Circle/Triangle/Select/Fill + undo/redo) is #c3bfba / #39393c because `IsEnabled="{Binding CanPaint}"` is false, turning the whole centre of the screen into grey slabs. None of these values appear in Tokens.axaml. Mockup rule: explorer.html `.tbtn:disabled{opacity:.38; cursor:not-allowed}` — background stays `var(--panel)`. Landing's disabled tier is different again: `.tbtn.soon{border-style:dashed; color:var(--fg-muted); background:transparent}`.

**Fix:** In `Themes/Styles.axaml` add `Style Selector="Button.tbtn:disabled /template/ ContentPresenter#PART_ContentPresenter"` and `Button.accent:disabled /template/ ...` setting Background back to PanelBrush/AccentBrush and BorderBrush to LineStrongBrush/AccentBrush, plus `Style Selector="Button:disabled" { Opacity 0.38 }`. Add a separate `Button.soon` class (BorderBrush=LineStrongBrush, `StrokeDashArray="3,3"` via a Border-based template or `Background=Transparent` + dashed border, Foreground=FgMutedBrush) and apply it to LandingView's "New Kitchen…" and "Open a cooked .set…".

### [Critical] All screens (system)

**Gap:** The mockups' small-label type scale does not exist in Styles.axaml. `.slbl` / `.pane-h` / `.plabel` / `.tagline` / `.sub-h` (10–11.5px, uppercase, .10–.14em tracking, fg-muted) are all rendered as plain `Classes="muted"` at Avalonia's default ~14px with zero tracking, so every section heading is ~40% oversized and the density collapses.

**Evidence:** Measured ink on landing-light.png: "CREATE" cap-height = 10px (x 95→139, y 124→133) and "Asset Generator" cap-height = 10px — both imply ~14px Segoe UI. Mockup: landing.html `.slbl{font-size:10px; letter-spacing:.14em; text-transform:uppercase}` (cap-height ≈7px) and `.tagline{font-size:11.5px}`. Source: LandingView.axaml:16,18,35 are `Classes="muted"` with no size. Same defect in IngredientEditorView ("Variants", "Value", "Brush size", "Hue range", "Colorize"), NewRecipeView ("Weight", "Resulting mix"), NewIngredientView. Note that where the scale WAS implemented — `TextBlock.ml`, `.data-h`, `.ax`, `.rcl` — it is correct, and CookBookDetailView/RecipeDetailView set FontSize=10.5/LetterSpacing=1.05 inline, so the values are known; they are just not a shared class.

**Fix:** Add to `Themes/Styles.axaml`: `TextBlock.slbl` (FontSize 10, LetterSpacing 1.4, Foreground FgMutedBrush, Margin 0,0,0,8), `TextBlock.pane-h` (mono, FontSize 10.5, LetterSpacing 1.47, FgMuted), `TextBlock.plabel` (FontSize 10, LetterSpacing 1.0, FgMuted, Margin 0,0,0,8), `TextBlock.tagline` (FontSize 11.5, FgMuted), `TextBlock.sub-h` (mono 10.5, LetterSpacing 1.26). Then replace every bare `Classes="muted"` heading in LandingView / IngredientEditorView / New*View with the right class, and swap the inline FontSize/LetterSpacing in CookBookDetailView:51, RecipeDetailView:29,64, IngredientDetailView:69, SetBrowserView:71 for `Classes="sub-h"`.

### [Critical] Help

**Gap:** The Help view is a placeholder: one bordered box containing a single run-on wrapped paragraph. The mockup is a 780px three-column sheet with a branded header, five labelled sections, a glyph gutter and a footer band.

**Evidence:** help-{light,dark}.png show ~3 lines of text and 80% empty panel. HelpView.axaml is 21 lines total, with the entire content in one `TextBlock Text="CookBook .cbk · Recipe .rcp · … = 360."` (line 18). help.html requires: `.sheet{width:780px; border-radius:var(--r-lg); box-shadow:var(--shadow)}`, `.sh-h` header with `.brandtile`+`.wordmark`+`.tdiv`+`.slbl`+`.esc` chip, `.sh-b{display:grid; grid-template-columns:1.35fr 1fr .82fr}` with `.col+.col{border-left:1px solid var(--line)}`, sections "The five words" / "Layer kinds" / "Rules & state" / "Keys" / "Colour", a strict `.e{grid-template-columns:20px 1fr; gap:10px}` glyph gutter, `.kline` kbd rows, `.cs` colour-prefix rows, and a `.sh-f` footer with the DNA sentence + `.dnaeq` "4 × 3 × 5 × 6 = 360".

**Fix:** Rewrite `Views/HelpView.axaml` as `Border` (Width 780, CornerRadius RadiusLg, PanelBrush, BoxShadow WinShadow) → `Grid RowDefinitions="Auto,*,Auto"`: header row (brandtile + accent-`y` wordmark + 1px LineStrong divider + `Classes="slbl"` + an `Esc` Border.idchip); body `Grid ColumnDefinitions="1.35*,*,0.82*"` with LineBrush left borders on columns 2–3 and per-entry `Grid ColumnDefinitions="20,*"`; footer Border with BgAltBrush + top LineBrush hairline.

### [Critical] Ingredient editor (paint)

**Gap:** The editor is a functional skeleton, not the designed screen. Almost every control in `ingredient-editor.html` is replaced by a default Avalonia primitive stacked vertically.

**Evidence:** editor-paint-{light,dark}.png vs ingredient-editor.html: (a) tools are seven full-width left-aligned grey text buttons in a 180px column — mockup `.toolstrip` is a HORIZONTAL 30×30 icon bar with `.tsep` 1px dividers, an active `.ttool.on{background:var(--accent); color:var(--on-accent)}`, a 96×16 `.ramp` value gradient with an accent `.pin`, and a 20px `.swatch`; the app shows no active-tool state at all. (b) Mode is two Fluent RadioButtons — mockup `.seg` is a segmented tray (`background:var(--bg-alt2); border:1px solid var(--line); padding:3px`) with the active pill accent-filled. (c) Hue/Sat are FOUR separate single sliders stacked (IngredientEditorView.axaml:94-98) — mockup is one `.dual` per axis: an 18px-tall gradient `.track` (hue rainbow / grey→sat) with two 14px ring `.handle`s, a live `.cv` readout "196–348°", and two mono `.nin` end inputs. (d) `.vlock` dashed "Value ← from grayscale — not editable" row is absent. (e) Quantize is two NumericUpDowns labelled "Hue buckets"/"Sat buckets" — mockup `.qrow` is `Hue [−] 12° [+]  Sat [−] 4% [+]` with 19px `.sbtn`s and an "≈ 91 colors" readout. (f) Variant cards are `Button.tbtn` with a stacked 32px image — mockup `.vcard` is a horizontal 38px thumb + name + `weight [input] · 32%`, selected = accent-wash + `inset 2px 0 0 var(--accent)`; the dashed `.vaddbtn` "+ Add Variant" footer is missing. (g) `.canvas-wrap`'s 18px checkerboard `repeating-conic-gradient` and the 320px `.canvas-art` with r-lg + `box-shadow:0 10px 28px -16px` are absent — sampled flat TileBrush #ece5db / #12141c at (480,300). (h) Static mode is a bare `TextBox Watermark="hex:d6249f"` instead of hue+sat sliders + a 34px `.swatch-lg` + the hsv caption.

**Fix:** Rewrite `Views/IngredientEditorView.axaml` against the mockup's `.epanes` grid (`262 / minmax(540,1fr) / 300`) with `.pane-h` headers. Add to Styles.axaml: `Button.ttool` (30×30, PanelBrush, LineStrongBrush, RadiusSm) + `Button.ttool.on` (AccentBrush/OnAccentBrush); `Border.seg` + `Button.seg-b`/`.seg-b.on`; `Border.vcard`/`.vcard.sel` (AccentWashBrush + AccentBarShadow); `Button.vaddbtn` (dashed LineStrongBrush, FgMuted); `TextBox.nin` (58px, mono 11, RadiusXs). Build the dual-range as a custom two-Thumb control over a LinearGradientBrush track; add the checkerboard as a `DrawingBrush`/`TileBrush` on the canvas host.

### [High] Explorer

**Gap:** The three-pane grammar is wrong. The mockup's panes are full-bleed regions on `--bg` separated by 1px `--line` dividers, each opening with a 41px `.pane-h` label row. The app renders the tree as a floating rounded card filled with PanelBrush and inset by a 12px margin, with no pane headers and no dividers.

**Evidence:** explorer-{light,dark}.png: the left column is visibly a different tone from the centre — sampled #f8f3ed (PanelBrush) at (180,400) vs #f4efe8 (BgBrush) at (300,450); dark is #0b0c10 vs #07080b. Source: ExplorerView.axaml:41 `Background="{DynamicResource PanelBrush}" Margin="12,10,12,12"`. Mockup: explorer.html `.pane{border-right:1px solid var(--line); min-width:0; overflow-y:auto}` with NO background (inherits `.window{background:var(--bg)}`), and `.pane-h{height:41px; padding:0 16px; border-bottom:1px solid var(--line)}` carrying "CONTENTS" + `.hcount`. Widths are also off: app `ColumnDefinitions="260,*"` vs mockup `286px minmax(392px,1fr) 336px`.

**Fix:** ExplorerView.axaml:38-58 — change the grid to `ColumnDefinitions="286,*"`, drop the TreeView's PanelBrush Background (set Transparent) and its Margin, and wrap each pane in a `Border BorderBrush={DynamicResource LineBrush}` `BorderThickness="0,0,1,0"` with a 41px header row using the new `TextBlock.pane-h` class. Widen the detail rail from 280/300 to 336.

### [High] Ingredient detail / Explorer

**Gap:** Text is hard-clipped mid-word and a table column is cut off — the screen looks broken, not merely off-spec. Fixed-pixel table columns plus a fixed-width rail exceed the pane width.

**Evidence:** explorer-light.png: the hero subtitle reads "no colorize · compc" (truncated mid-word, no ellipsis) and the table header reads "WEIGHT  IN RECIPE  OVER" with OVERALL sliced at the pane edge. IngredientDetailView.axaml:10 is `ColumnDefinitions="*,280"` with Margin 16, and lines 29/48 are `ColumnDefinitions="48,*,80,100,100"` — 328px of fixed columns. Inside ExplorerView's 260+* split at 900px the main column gets ≈300px, so the fixed columns alone overflow it. The same fixed rail forces the colorways `.cwaxis` value to wrap onto two lines and collide with the divider (visible in ingredient-detail-*.png: "no colorize · composited as-is" overlapping the COLOUR row rule).

**Fix:** IngredientDetailView.axaml:29,48 — change to star/Auto columns (`ColumnDefinitions="48,*,Auto,Auto,Auto"` with MinWidth on the numeric cells) and add `TextTrimming="CharacterEllipsis"` to the hero subtitle and name TextBlocks. Change the rail column from `280` to `Auto` with `MinWidth="208"` to mirror explorer.html `.ing-grid{grid-template-columns:minmax(0,2fr) minmax(208px,1fr)}`.

### [High] Recipe detail

**Gap:** The layer table's index and name columns collide — rows read "1bg" and "2aura" with zero gap.

**Evidence:** recipe-detail-{light,dark}.png, LAYERS table. RecipeDetailView.axaml:48-50: `ColumnDefinitions="40,*,110,90"` with column 0 `Classes="data-row num"` — `TextBlock.num` sets `TextAlignment=Right`, so the index is flushed hard against the left edge of the name column, which has no left margin. Mockup: explorer.html `table.data td{padding:12px 10px}` gives every cell 10px of horizontal padding.

**Fix:** RecipeDetailView.axaml:49 — add `Margin="0,0,10,0"` to the index TextBlock (or drop `num` and left-align it), matching the 10px cell padding; do the same in the header row at line 34.

### [High] Landing

**Gap:** The action column is centred rather than left-aligned, buttons are wider and ~17% taller than spec, the two-tier Create structure is flattened, and the "learn" link is drawn as a bordered button.

**Evidence:** landing-light.png measured: buttons start at x=94 and are 300px wide; button height 41px (border runs at y=149→190, 245→286), gaps 7px. LandingView.axaml:14 sets `MaxWidth="300"` on a Stretch-aligned StackPanel inside a `*` column, which centres it — mockup `.actions{max-width:272px}` inside a left-aligned grid column, `.tbtn{padding:9px 12px; font-size:12px}` ≈ 35px tall. The mockup's second tier is `.subrow{display:flex; gap:7px}` with `.tbtn{padding:7px 10px; font-size:11.5px}` — two SMALL side-by-side buttons labelled "Recipe" and "Ingredient"; the app renders them as two more full-width 41px rows labelled "New Recipe"/"New Ingredient". The learn link is `Button Classes="landing tbtn"` (line 51) — mockup `.learn{background:transparent; border:0; padding:0; margin-top:22px}` with "The cooking metaphor" in `var(--accent-text)`.

**Fix:** LandingView.axaml:14 — `HorizontalAlignment="Left" MaxWidth="272"`. Styles.axaml:166 — `Button.landing` Padding `12,9` and add `Button.landing.sub` Padding `10,7` FontSize 11.5; put New Recipe/New Ingredient in a horizontal StackPanel with Spacing 7 and labels "Recipe"/"Ingredient". Replace the learn Button's classes with a new `Button.learn` (Transparent background, BorderThickness 0, Padding 0, Margin 0,22,0,0) using two `<Run>`s so "The cooking metaphor" takes AccentTextBrush.

### [High] Landing

**Gap:** The Recent column is empty and has no rows, no icon tiles, no path column and no first-run empty state, so half the screen is blank.

**Evidence:** landing-{light,dark}.png show only the word "RECENT" and nothing else across the entire right half. LandingView.axaml:57-70 renders each recent as a `Button.tbtn` with two stacked TextBlocks. Mockup `.rrow` is a borderless transparent row (`:hover{background:var(--bg-alt); border-color:var(--line)}`) containing a 22px `.rico` tile (`background:var(--accent-wash); border:1px solid var(--accent-line)`) with a book/ingredient SVG, a `.rname`(12px)/`.rmeta`(10px) stack, and a right-aligned `.rpath`(10px, opacity .7). First run must show `.rzero`: a dashed LineStrong box, radius 8, padding 28/20, centred, "Nothing here yet" (13px, 600) over "Cookbooks you open will collect here. Start with **New CookBook**."

**Fix:** LandingView.axaml:57-70 — retemplate the row as `Button Classes="rrow"` (Transparent bg, 1px transparent border, Padding 10,8, RadiusMd; hover → BgAltBrush/LineBrush) with `Grid ColumnDefinitions="22,*,Auto"`: rico Border (AccentWash + AccentLine + RadiusSm) hosting the type icon, name/meta stack, and a `.rpath` TextBlock (FontSize 10, FgMuted, Opacity .7). Add an `IsVisible`-bound `Border` empty state with `BorderThickness=1`, dashed stroke, CornerRadius 8, Padding 20,28.

### [High] Wizards — all three

**Gap:** The wizard form grammar is missing entirely: no kicker, no heading/sub, no per-field labels (watermarks are used as labels), no derived-identifier strip, no footer action band with keyboard hints, and no `?` help affordances.

**Evidence:** wizard-cookbook/recipe/ingredient-{light,dark}.png show a bare panel of stacked TextBoxes. NewCookBookView.axaml:13-22 uses `Watermark="Name"`, `Watermark="Symbol (optional)"`, `Watermark="Description"` with only a single bold "New CookBook" line. Mockup wizard-cookbook.html requires: `.kicker{font-size:10px; letter-spacing:.14em; uppercase}` with a 4px accent `.dot` ("● Create"), `.m-h{font-weight:700; font-size:19px; letter-spacing:-.015em}`, `.m-sub{font-size:11.5px; color:var(--fg-muted)}` ("Makes an empty .cbk — Recipes and layers get added in the Explorer."), `.fld label{font-size:10px; letter-spacing:.1em; uppercase; color:var(--fg-muted)}` above each input, `.derived{border-top:1px dashed var(--line-strong); padding-top:11px}` with the id in a `<code>` chip, and `.foot{padding:10px 16px; border-top:1px solid var(--line); background:var(--bg-alt)}` carrying `<kbd>Tab</kbd>move fields / <kbd>Enter</kbd>create / <kbd>Esc</kbd>cancel` on the left and `Cancel` + a labelled `Create CookBook` accent button on the right. The app's buttons say just "Create".

**Fix:** Rewrite the three `New*View.axaml` files to the mockup skeleton: kicker StackPanel (4px accent Ellipse + `Classes="slbl"`), `TextBlock FontSize="19" FontWeight="Bold" LetterSpacing="-0.29"`, `TextBlock Classes="tagline"`, then per-field `StackPanel Spacing="6"` of `TextBlock Classes="plabel"` + input, a dashed-top `Border` derived row using `Border.idchip` for the code, and a footer `Border Background={DynamicResource BgAltBrush} BorderThickness="0,1,0,0"` with a new `Border.kbd` class (LineStrong 1px, RadiusXs, PanelBrush, mono 10) and the accent button relabelled "Create CookBook"/"Create Recipe"/"Create Ingredient".

### [High] CookBook detail

**Gap:** The identity header, the whole right-hand combination-space column, and the cook footer bar are missing; only the chips, a 2×2 metric band and a distribution bar survive.

**Evidence:** cookbook-detail-{light,dark}.png: three naked chips at the top-left, then metrics, then the bar, then a lone "Cook set" button. CookBookDetailView.axaml has no equivalent of explorer.html `.cbk-id{display:flex; gap:14px; padding:14px 16px; margin-bottom:17px; border:1px solid var(--line); border-radius:var(--r-md); background:linear-gradient(var(--bg-alt2),var(--panel))}` containing `.idname` (mono 700 18px + 19px type icon), `.iddesc` (12.5px, max 64ch) and the `.idchips` row — the chips are currently orphaned outside any card. `.cbk-cols{grid-template-columns:minmax(260px,.82fr) minmax(340px,1.18fr); gap:24px}` puts the metric band + distribution in the LEFT column and `.cspace` in the right: per-recipe `.crow`s with a rotated `.cdot`, `.cname`, right-aligned `.fchip` factor chips separated by `.ftimes` ×, `.ceq` =, a bold `.cnum`, then a `.cbar` share bar and `.cshare` — none of it exists. `.cookbar{margin-top:18px; padding-top:15px; border-top:1px solid var(--line); justify-content:space-between}` is also absent.

**Fix:** CookBookDetailView.axaml — wrap name/description/chips in a new `Border.cbk-id` style (BgAlt2Brush, LineBrush, RadiusMd, Padding 16,14) and move the existing chip StackPanel inside it; restructure the root into `Grid ColumnDefinitions="0.82*,1.18*"` with the metric band + distribution left and a new `.cspace` ItemsControl right (add `Border.fchip` styles keyed off KindDynamic/Static/CustomBrush with a tinted background); wrap the Cook button in a `Border` with `BorderThickness="0,1,0,0"` LineBrush, Margin 0,18,0,0, Padding 0,15,0,0 and an info line on the left.

### [High] Application shell (titlebar + status bar)

**Gap:** The window chrome is present but stripped: the title bar lacks its gradient, hairline, Kitchen chip, breadcrumbs and lock flag; the status bar is sans-serif at default size with unstyled zoom buttons and a plain "?" instead of the accent help tile.

**Evidence:** gallery-{light,dark}.png (the only frame showing the shell): the status bar reads "Ready · 1,024 assets  −  100%  +  ?" in proportional Segoe UI with no chrome on any control. Nfty.Desktop/MainWindow.axaml:22 sets a flat `Background="{DynamicResource PanelBrush}"` with no bottom border — mockup `.titlebar{background:linear-gradient(var(--panel),var(--bg-alt)); border-bottom:1px solid var(--line); box-shadow:inset 0 1px 0 …}`. The brandtile (line 24) has AccentWash but no `border:1px solid var(--accent-line)` (visible in the zoomed crop as a borderless pink square). `.kroot` (accent-wash Kitchen chip), `.crumbs` and `.lockflag` (999px pill) are absent from the bar — ExplorerView instead renders crumbs as its own row at line 11. Status bar (line 68+): `Classes="muted"` sans vs `.statusbar{font-family:var(--font-mono); font-size:11.5px; font-variant-numeric:tabular-nums; border-top:1px solid var(--line)}`; no `.ok` success dot + "Valid" + counts; zoom buttons are `Classes="icon"` (borderless) vs `.zoomctl button{width:24px; height:24px; border:1px solid var(--line-strong); background:var(--panel); border-radius:var(--r-sm)}`; the help button is `Classes="icon"` vs `.helpbtn{color:var(--accent-text); background:var(--accent-wash); border:1px solid var(--accent-line)}`.

**Fix:** MainWindow.axaml:22 — give the Titlebar a `LinearGradientBrush` PanelBrush→BgAltBrush and wrap it in a Border with `BorderThickness="0,0,0,1" BorderBrush={DynamicResource LineBrush}`; add `BorderBrush={DynamicResource AccentLineBrush} BorderThickness="1"` to the brandtile at line 24; move ExplorerView's crumbs ItemsControl into the titlebar's `*` column and add the `.kroot`/`.lockflag` chips. Line 68 — add `Classes="mono"` FontSize 11.5 to the status text, a top LineBrush hairline, a SuccessBrush validity dot + counts, a new `Button.zoomctl` class (24×24, PanelBrush, LineStrongBrush, RadiusSm) and `Button.helpbtn` (AccentWash/AccentLine/AccentText).

### [High] Recipe detail

**Gap:** The portrait hero is missing its factor arithmetic and stats block, and the reroll die sits beside the text instead of overlaid on the portrait's bottom-right corner.

**Evidence:** recipe-detail-{light,dark}.png show only a 92px tile, "Cat", and "Seed 1 ⊡". explorer.html `.rhero` requires `.rport-wrap` with the dice absolutely positioned (`position:absolute; bottom:6px; right:6px; width:27px; height:27px; box-shadow:var(--shadow)`) OVER the canvas, plus `.rmeta` containing `.rfactors` (kind chips × separated by `.feq`), `.rtotal{font-family:mono; font-size:20px; font-weight:700}`, `.rlabel{font-size:12px}` and `.rstats{display:flex; gap:8px 20px; margin-top:12px}`. RecipeDetailView.axaml:13-25 has a plain `ColumnDefinitions="Auto,*"` with the dice as an inline sibling of the seed text.

**Fix:** RecipeDetailView.axaml:14-24 — put the 92px portrait Border and the dice Button in a shared `Panel`, with the dice `HorizontalAlignment="Right" VerticalAlignment="Bottom" Margin="0,0,6,6"`; add a `.rfactors`/`.rtotal`/`.rlabel`/`.rstats` block to the right column, reusing the new `Border.fchip` styles and `TextBlock.mv`-style mono 20px for the total.

### [Medium] All screens (system)

**Gap:** Text renders with LCD subpixel antialiasing, producing visible orange/blue colour fringes on every glyph — an artefact the mockups explicitly avoid.

**Evidence:** Zoomed crops of gallery-light.png show strong blue/orange fringing on "Open CookBook", "nfty" and the tree labels; naive pixel sampling of text picked up #c58a18 and #9bc9e6 on landing-light.png at (300,548) and landing-dark.png at the same point — pure fringe colours from a nominally monochrome label. Mockup rule: every stage sets `-webkit-font-smoothing: antialiased` (grayscale AA).

**Fix:** Set `RenderOptions.TextRenderingMode="Antialias"` on the root Grid of `Nfty.Desktop/MainWindow.axaml`, or add `<Setter Property="(RenderOptions.TextRenderingMode)" Value="Antialias"/>` to the `Window` style in Themes/Styles.axaml:3.

### [Medium] CookBook detail

**Gap:** Mint-distribution segment colours are generated at runtime from a hash outside the token system and are identical in both themes.

**Evidence:** Sampled cookbook-detail-light.png AND cookbook-detail-dark.png at (150,242) → #975cb8 in both; at (450,242) → #b8945c in both. Neither is a token: light KindCustom is #6d4f9c and dark #c3a6ea; light KindStatic is #6a4a25 and dark #e0c28c. Source: `ViewModels/CookBookDetailViewModel.cs:51-56` — `SegmentColorFor` returns `HsvToRgb(SeedHash.ToUlong(id) % 360, 0.5, 0.72)`. CLAUDE.md's rule is token brushes only in Views; this is an unthemed palette bypassing Tokens.axaml, and it is why the light frame's bar is heavier than anything else on the screen.

**Fix:** Either derive the segment fill from the existing kind tokens (cycle KindDynamic/KindStatic/KindCustom/Accent) resolved as DynamicResources so they flip with the theme, or add an explicit `RecipeSeries1..6` brush pair to `Themes/Tokens.axaml` (light and dark variants) and have `CookBookDetailViewModel.SegmentColorFor` return a resource key instead of a `Color`.

### [Medium] Application shell (dialog scrim)

**Gap:** The modal scrim is a raw 53%-black hex, so light-theme dialogs sit on a heavy grey-black veil instead of the mockup's tinted-background wash.

**Evidence:** `Nfty.Desktop/MainWindow.axaml:44` — `Background="#88000000"`, the only raw hex outside Tokens.axaml in the whole UI. Mockup help.html `.scrim{background: color-mix(in srgb, var(--bg) 52%, transparent)}` — i.e. a 52% veil of `--bg` (#f4efe8 in light, #07080b in dark), which lightens in light theme rather than darkening.

**Fix:** Add `ScrimBrush` to both dictionaries in `Themes/Tokens.axaml` (`#85f4efe8` light, `#8507080b` dark — Avalonia #AARRGGBB) and change MainWindow.axaml:44 to `Background="{DynamicResource ScrimBrush}"`.

### [Medium] Gallery / all forms

**Gap:** Input primitives are too large and the slider is the wrong shape: the mockup's inputs are a uniform 32px tall at 12.5px, its steppers a 20px-wide column of 9px chevrons, and its slider a 6px bordered track with a 14px ring handle.

**Evidence:** gallery-{light,dark}.png bottom row: the "aura" TextBox is ~40px tall with ~14px text; the NumericUpDown is ~44px tall with two 34px-wide chevron buttons side by side; the Slider has a ~2px hairline track and a SOLID accent dot. Mockup: wizard-cookbook.html `.in{height:32px; padding:0 11px; font-size:12.5px; border-radius:6px}`, `.num{height:32px}` with `.stepr{width:20px; flex-direction:column}` and `.stepr button svg{9px}`; ingredient-editor.html `.track{height:6px; border-radius:999px; border:1px solid var(--line)}` and `.handle{width:14px; height:14px; border-radius:50%; background:var(--panel); border:2px solid var(--accent)}`.

**Fix:** In `Themes/Controls.axaml`: on the TextBox ControlTheme set `Height="32" Padding="11,0" FontSize="12.5"`; on NumericUpDown set `Height="32"` and restyle `ButtonSpinner`'s spinner buttons to a stacked 20px column; on the Slider ControlTheme set the track parts' Height to 6 with CornerRadius 999 + LineBrush border, and retemplate `Thumb` to a 14px Ellipse with `Fill={DynamicResource PanelBrush}` and a 2px AccentBrush stroke.

### [Medium] Explorer / Recipe detail / Gallery

**Gap:** The kind indicator is a third design that appears in neither mockup: a bordered box with a transparent fill and no dot. The mockup has exactly two forms — plain coloured mono text (`.kind-txt`) and a tinted wash badge with a leading 4px square (`.fchip`/`.kbadge`).

**Evidence:** gallery-{light,dark}.png show `dynamic` / `static` / `custom` as outlined pill-ish boxes with coloured text on a transparent fill. Styles.axaml:230-247 defines `Border.kind-dynamic/-static/-custom` with `BorderThickness=1` and no Background. Mockup: explorer.html `.kind-txt.dyn{color:var(--info)}` — text only, no box; `.fchip.dyn{color:var(--info); background:color-mix(in srgb,var(--info) 12%,transparent); border-color:color-mix(in srgb,var(--info) 26%,transparent)}` and `.kbadge::before{content:""; width:4px; height:4px; border-radius:1px; background:currentColor}`.

**Fix:** Add `KindDynamicWashBrush`/`KindStaticWashBrush`/`KindCustomWashBrush` (12–13% alpha) and matching `*LineBrush` (26–30% alpha) pairs to `Themes/Tokens.axaml` for both themes; rename the existing `Border.kind-*` styles to `Border.fchip.kdyn/kstat/kcust`, give them the wash Background and a leading 4px `Border` dot in the consuming template; use the plain `TextBlock.kind-txt` (already correct) wherever the mockup shows bare coloured text.

### [Medium] Explorer (contents tree)

**Gap:** The tree is missing its 18px type-icon column, the expander glyph is oversized, and the branch guide line is drawn as disconnected per-row segments.

**Evidence:** Zoomed crop of explorer-light.png: root "VaporPets" has only a large thin chevron then the label; "cat" has a chevron plus a short guide stub; "bg"/"aura" have a guide stub, the kind letter and the label — labels start at different x per depth with no icon to anchor them, and clear vertical gaps appear between one row's guide segment and the next. Mockup: explorer.html `.node{gap:8px}` with `.tw{width:13px; font-size:9px}` (a tiny caret) and `.ti{width:18px; height:18px}` type icon on EVERY node, and `.branch{margin-left:15px; padding-left:12px; border-left:1px solid var(--guide)}` — a single continuous line down the whole branch. Source: ExplorerView.axaml:44-53 puts one `Border Classes="guide"` inside each row's DataTemplate (as the code comment acknowledges), and Styles.axaml:271-275 sizes it 1px stretched to the row only.

**Fix:** ExplorerView.axaml:44-53 — insert a `Path Classes="ico"` type-mark (cookbook/recipe/ingredient) bound to the node kind between the guide and the kmark; in Controls.axaml's TreeViewItem ControlTheme, shrink `ToggleButton#PART_ExpandCollapseChevron` to 13px with a 9px glyph, and move the guide from the item template onto the TreeViewItem container's `ItemsPresenter` (or give `Border.guide` a negative vertical Margin equal to the row spacing) so consecutive segments abut.

### [Medium] Ingredient detail

**Gap:** The variant hero drops the kind colour-coding and the rarity bars, and splits the one-line subtitle into two stacked lines; the colorways rail shows thumbnails where the mockup has a hue band.

**Evidence:** ingredient-detail-{light,dark}.png: "Custom" is rendered in neutral fg-muted, not the custom purple, even though the tree kmark and the recipe layer table DO colour it correctly (IngredientDetailView.axaml:20 uses `Classes="muted"` where `Classes="kind-txt kcust"` exists). Line 21 puts "no colorize · composited as-is" on its own second line — mockup `.vhero .hsub` is a single 12px line `custom · no colorize · composited as-is` with only the kind word in mono/colour, and the kind word is lowercase. `.rarity{margin-top:13px; display:grid; grid-template-columns:1fr 1fr; gap:11px 30px}` with `.rr .rt{height:7px; border-radius:999px}` bars is entirely absent. In the rail, lines 71-83 render 40×40 thumbnail tiles where explorer.html has `.hueband{height:34px; border-radius:var(--r-sm); border:1px solid var(--line)}` plus a `.cwtop` row (kind-txt + `.cwmodel` chip).

**Fix:** IngredientDetailView.axaml:19-21 — set the name to `Classes="mono"` FontSize 17 (already) and merge lines 20–21 into one wrapping TextBlock built from `<Run Classes=…>`-equivalent inline spans, with the kind run using `Classes="kind-txt"` + the bound kdyn/kstat/kcust class; add a rarity `Grid ColumnDefinitions="*,*"` of label/track pairs (new `Border.rt` style, Height 7, CornerRadius 999, AccentBrush fill). Lines 71-83 — replace the thumbnail WrapPanel with a 34px `Border` filled by a `LinearGradientBrush` built from the hue range, and add the `.cwtop` header row with a `Border.cwmodel` chip.

### [Medium] Set browser

**Gap:** The tile grid is hard-coded to four columns, so the fourth tile is sliced off at the pane edge; the header is a bare text row with none of the shell's chip/label grammar.

**Evidence:** set-browser-{light,dark}.png: #0004 is cut in half at x=900 while rows 2 has only two tiles and the rest of the pane is empty. SetBrowserView.axaml:36 uses `RowChunkConverter.By4` (a fixed chunk size, per the file's own virtualization note) with 120px tiles + 10px spacing + 12px button padding ≈ 142px each → 568px, exceeding the ≈584−16 available beside the fixed 300px rail at this width. The header (lines 27-31) is `Name / "6 items" / "Seed seed1"` as plain TextBlocks; every other screen in the design language uses `.idchip`/`.slbl` for that metadata.

**Fix:** SetBrowserView.axaml — make the chunk size a bound property computed from the ListBox's ActualWidth (or replace RowChunkConverter.By4 with a converter parameterised on available width) so the row count reflows; wrap the header metadata in `Border.idchip` chips and give the name the shared `.cbk-id` treatment for consistency with CookBook detail.

### [Medium] Application shell

**Gap:** The page content is wrapped in a second bordered, shadowed, radius-10 frame inside the window frame, so every screen renders as a card floating inside a card.

**Evidence:** `Nfty.Desktop/MainWindow.axaml:39` — `<Border Classes="frame" Margin="8">` around the page ContentControl, where `Border.frame` (Styles.axaml:216-222) already sets BgBrush + LineStrongBrush + RadiusWin + WinShadow. The window itself is the mockup's `.window{border:1px solid var(--line-strong); border-radius:var(--r-win); box-shadow:var(--shadow)}` — there is no inner frame in any mockup; the panes are flush against the titlebar and status bar. This inner frame plus ExplorerView's own 12px tree margin is why the explorer reads as loose floating boxes rather than a dense three-pane app.

**Fix:** MainWindow.axaml:39 — drop `Classes="frame"` and `Margin="8"` from the page host (leave a plain `Panel`/`Border` with `Background={DynamicResource BgBrush}`), and move the window border/radius/shadow onto the Window's own root Border so the pages sit flush.

### [Low] Explorer (toolbar)

**Gap:** The lock toggle is not pushed to the right edge, toolbar buttons carry no leading icons, and the toolbar contains a result-count TextBlock the mockup does not have.

**Evidence:** explorer-{light,dark}.png: the lock sits immediately after "Import" at x≈528 with the rest of the bar empty to x=900. Mockup explorer.html `.lock-toggle{margin-left:auto}` and `.lockflag` is a rounded 999px pill with a 12px lock SVG plus text, sitting at the far right. `.tbtn svg{width:14px; height:14px}` — Add variant / Delete / Import all carry a leading icon. ExplorerView.axaml:30 adds `TextBlock Text="{Binding SearchSummary}"` which has no counterpart in the mockup toolbar. Line 28's TextBox is `Width="220"` fixed, versus `.search{flex:1; min-width:180px; max-width:360px}` with an inline magnifier and a `kbd` ⌘K chip.

**Fix:** ExplorerView.axaml:27 — change the toolbar to a `Grid ColumnDefinitions="*,Auto,Auto,Auto,Auto"` so the lock lands in the last column; give the lock a new `Border.lockflag` style (CornerRadius 999, LineStrongBrush, Padding 10,4, mono 11) with an icon Path; add leading `Path Classes="ico"` children to the three toolbar buttons; delete the SearchSummary TextBlock (line 30) and rebuild the search as a `Border` (PanelBrush/LineStrongBrush/RadiusMd, Padding 11,8) containing a magnifier Path, a chromeless TextBox, and a right-aligned `Border.kbd` "⌘K".

### [Low] Landing / Gallery

**Gap:** The landing wordmark loses the brand's accent 'y' and its negative tracking, even though the titlebar wordmark gets both right.

**Evidence:** landing-{light,dark}.png: "nfty" is a single flat FgBrush word 30px tall. LandingView.axaml:15 is `TextBlock Text="nfty" FontSize="30" FontWeight="Bold"` with no Runs and no LetterSpacing. Mockup landing.html `.lwordmark{font-size:30px; font-weight:700; letter-spacing:-.02em}` and `.lwordmark b{color:var(--accent-text)}`. The zoomed gallery titlebar crop confirms the shell already does this correctly (`<Run Text="nft"/><Run Text="y" Foreground="{DynamicResource AccentTextBrush}"/>`, MainWindow.axaml:33), so the landing is simply inconsistent with its own shell.

**Fix:** LandingView.axaml:15 — split into `<Run Text="nft"/><Run Text="y" Foreground="{DynamicResource AccentTextBrush}"/>` and add `LetterSpacing="-0.6"` (mockup −.02em × 30px).

## Suggested order

1. Slice 1 — Icon system. Add Themes/Icons.axaml with StreamGeometry resources traced from the mockups' SVG paths plus a Path.ico style set (13/14/18px). Sweep every View and MainWindow, replacing emoji, arrows, ＋, ⚄, ✏, 🔒, ✕, →, —, ▢ with Path.ico. This alone removes the colour emoji and the tofu boxes and touches every screen.
2. Slice 2 — Kill the off-palette colours. Add ToggleButton and ProgressBar ControlThemes to Controls.axaml (removes #0078d7 from both wizards); add Button:disabled opacity/surface overrides plus a Button.soon dashed class (removes the Fluent grey slabs from Landing, Explorer, Editor and Ingredient detail); add ScrimBrush to Tokens.axaml and replace MainWindow's #88000000; retoken CookBookDetailViewModel.SegmentColorFor. After this the app contains no colour outside Tokens.axaml.
3. Slice 3 — Type scale. Add TextBlock.slbl / .pane-h / .plabel / .tagline / .sub-h to Styles.axaml and replace every bare Classes="muted" heading and every inline FontSize/LetterSpacing across LandingView, ExplorerView, IngredientEditorView, New*View, CookBookDetailView, RecipeDetailView, IngredientDetailView, SetBrowserView. Set RenderOptions.TextRenderingMode=Antialias on the Window style in the same pass.
4. Slice 4 — Shell chrome. MainWindow titlebar gradient + hairline + brandtile accent-line border; move the Explorer crumbs into the titlebar and add the kroot/lockflag chips; mono status bar with validity dot and counts, Button.zoomctl and Button.helpbtn styles; drop the inner Border.frame double-chrome.
5. Slice 5 — Explorer pane grammar. Full-bleed panes on BgBrush with 1px LineBrush dividers and 41px pane-h headers, 286/*/336 column widths, tree type-icon column, 13px twisty, continuous branch guide, toolbar Grid with the lock right-aligned, search rebuilt as a flexing pill with magnifier + ⌘K kbd chip.
6. Slice 6 — Fix the visibly broken layout. Star/Auto table columns + TextTrimming in IngredientDetailView (stops the OVERALL and "compc" clipping), 10px cell gap in RecipeDetailView's layer table (stops "1bg"), reflowing tile-row chunking in SetBrowserView (stops the sliced 4th tile).
7. Slice 7 — Landing. Left-align at 272px, 35px primary / 30px two-up secondary button tiers, Button.soon on Kitchen and .set, borderless learn link with accent run, accent 'y' wordmark, rrow recents with rico tiles and rpath, dashed rzero empty state.
8. Slice 8 — Wizards. Kicker / m-h / m-sub / per-field plabel labels / derived id strip / foot band with kbd hints and labelled Create buttons across all three New*View files; 32px inputs and 20px stacked steppers in Controls.axaml.
9. Slice 9 — Detail views. CookBook identity card + two-column layout with the combination-space breakdown + cookbar; Recipe rhero with overlaid dice, factor chips, total and stats; Ingredient hero with coloured kind run, single-line subtitle and rarity bars; colorways hue band with cwtop.
10. Slice 10 — Ingredient editor rebuild to the mockup's 3-pane shell: horizontal 30px icon toolstrip with active state, value ramp and swatch; vcard filmstrip with dashed add button; checkerboard canvas backdrop with the 320px art tile; segmented Static⇄Dynamic control; dual-handle gradient range sliders with cv readouts and nin end inputs; vlock row; compact quantize steppers; blip preview overlaid on the canvas.
11. Slice 11 — Help sheet: the full 780px three-column reference with header, glyph gutters, key/colour columns and DNA footer band.
12. Slice 12 — Final sweep: fchip kind badges with wash + dot, slider ring handles, and a re-render of all 24 frames to diff against the mockups.
