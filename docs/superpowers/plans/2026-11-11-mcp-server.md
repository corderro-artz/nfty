# nfty MCP Server Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `nfty-mcp`, a local stdio MCP server exposing 16 tools (inspect, validate, roll, cook, tune, export, publish) over the same library code the CLI runs.

**Architecture:** New `src/Nfty.Mcp` exe (packed as a .NET tool) on the official C# MCP SDK. Each tool class is a thin adapter: it resolves paths through a root sandbox, calls Core/Ticket/Model, and returns a text report plus structured content (and an image where relevant). The policy that currently lives in CLI handlers (Set collision guards, the seeded Menu roll, the book edits, the replace-write) moves into Core/Ticket first, so the CLI and the server share one copy.

**Tech Stack:** .NET 10, `ModelContextProtocol` 1.x (stdio, attribute tools), `Microsoft.Extensions.Hosting` (transitive), xunit.v3.

**Spec:** `docs/superpowers/specs/2026-11-11-mcp-server-design.md`

## Global Constraints

- `ModelContextProtocol` is referenced by `Nfty.Mcp` only. Its exact version is pinned in `Directory.Packages.props`, never in a csproj.
- `Nfty.Core`, `Nfty.Ticket` and `Nfty.Model` gain no package and never reference `Nfty.Mcp`. `AssemblyRulesTests` asserts this.
- `TreatWarningsAsErrors` and `GenerateDocumentationFile` apply: every public member is XML-documented.
- No raw `Image.Load`/`Identify`. Every decode goes through `Nfty.Core.Imaging.ImageFormats`, and `ImageFormatsTests` sweeps `src/`.
- US English in every tool name, description and message.
- Tests are xunit.v3 and named `Snake_case_sentences`. Fixtures are built in memory or with `DemoCookBook.WriteTo(tempDir)`, and filesystem tests use `Directory.CreateTempSubdirectory()`.
- Nothing is written to stdout except protocol frames. `Console.SetOut(Console.Error)` runs before the host is built.
- Secret sources are `env:NAME` or `file:PATH` only. `stdin`, `prompt` and bare values are refused with a sentence naming the allowed forms.
- Tool errors are `ErrorReport.Format(ex)` text with `isError: true`, never a stack trace or a protocol-level exception for an engine failure.
- Output paths must not exist (collision rules in Task 1). `tune_book` is the only in-place writer.
- Output is byte-identical to the CLI for the same inputs and seed, at any thread count.

## Review Focus

1. A path that escapes a root through `..`, a symlink or junction, or a relative path against a different working directory must be refused before any read. Task 2 adds `A_junction_inside_a_root_pointing_outside_is_refused`.
2. A library or SDK writing to `Console.Out` mid-tool must not corrupt the stream. Task 2 adds `A_tool_that_writes_to_console_leaves_the_protocol_stream_parseable` (the end-to-end test calls a probe tool that prints, then lists tools successfully).
3. `tune_book` on a book the app (or another agent) changed after the read must refuse, not overwrite. Task 6 adds `A_book_changed_after_reading_is_not_overwritten`.
4. A canceled `cook` must leave no partial Set, relying on `WriteRollback` in Core. Task 5 adds `A_canceled_cook_leaves_the_folder_as_it_was`.
5. Two concurrent writers to one destination: the second is refused, not interleaved. Task 5 adds `A_second_writer_to_the_same_folder_is_refused`.

---

### Task 1: Move CLI policy into Core and Ticket

**Files:**
- Create: `src/Nfty.Core/Output/SetOutputGuard.cs`
- Create: `src/Nfty.Ticket/Menu/MenuRolls.cs`
- Create: `src/Nfty.Core/Editing/BookTuning.cs`
- Create: `src/Nfty.Core/Formats/ReplaceWrite.cs`
- Modify: `src/Nfty.Cli/CommandFactory.cs` (remove `RequireNewSetDirectory`, `RequireNewModelFiles`, `SetCollision` (~l.951–1000) and `RollTicket` (~l.1201); call the new types)
- Modify: `src/Nfty.Cli/CommandFactory.Parity.cs`, `CommandFactory.Authoring.cs` (the weight, recipe-weight, chance, cookbook and rule handlers call `BookTuning`; `WriteReplacing` delegates to `ReplaceWrite`)
- Test: `tests/Nfty.Core.Tests/SetOutputGuardTests.cs`, `tests/Nfty.Ticket.Tests/MenuRollsTests.cs`, `tests/Nfty.Core.Tests/BookTuningTests.cs`

**Interfaces:**
- Produces:
  - `public static class SetOutputGuard { void RequireNewSet(string directory, bool pack); void RequireNewModelFiles(string directory, IAssetModeler modeler, int count); }`. Both throw `InvalidOperationException` with today's exact CLI sentence.
  - `public static class MenuRolls { AssetTicket FromSeed(PreparedMenu menu, string seed, int maxRerolls); }`. Same RNG construction and the same failure sentence as `RollTicket`.
  - `public abstract record TuneOp` with sealed records:
    - `VariantWeight(string Recipe, string Layer, string Variant, double Weight)`
    - `RecipeWeight(string Recipe, double Weight)`
    - `AbsentChance(string Recipe, string Layer, double Percent)`
    - `TargetSupply(int? Value)`
    - `AddRule(string Recipe, IncompatibilityRule Rule)`
    - `RemoveRule(string Recipe, int At)` (1-based, as `remove rule --at`)
  - `public static class BookTuning { LoadedCookBook Apply(LoadedCookBook book, IReadOnlyList<TuneOp> ops); }`. Applies in order and returns a new graph that reuses every image. It throws `KeyNotFoundException` / `ArgumentOutOfRangeException` with the CLI's sentences. It does not validate; the caller runs `Validator.Validate`.
  - `public static class ReplaceWrite { void Write(string path, Action<string> writeTemp, string? expectedSha256 = null); }`. Writes a sibling temp file, then replaces. When `expectedSha256` is non-null and the current file's hash differs, it throws `InvalidOperationException("'{path}' changed since it was read; nothing was written.")` before writing.

- [ ] **Step 1: Write failing tests**
  - `SetOutputGuardTests`:
    - `A_folder_holding_set_json_is_refused_with_the_extend_hint`: the message contains "Use extend to grow that Set".
    - `A_kitchen_folder_with_unrelated_files_is_accepted`.
  - `MenuRollsTests.Same_seed_same_ticket_as_the_generators_first_asset`: build the demo Menu, compare `FromSeed(prepared, "s", 64).Dna` with `Generator.Generate(book, new(1, "s")).Assets[0].Dna`.
  - `BookTuningTests` (demo book):
    - `Variant_weight_changes_only_that_variant`
    - `Ops_apply_in_order`: two weights on the same variant, the last wins.
    - `An_unknown_recipe_names_it`
    - `Remove_rule_at_out_of_range_names_the_count`
  - `ReplaceWrite_refuses_when_the_hash_moved`
- [ ] **Step 2: Run** `dotnet test tests/Nfty.Core.Tests --filter "FullyQualifiedName~SetOutputGuardTests|FullyQualifiedName~BookTuningTests"` and `dotnet test tests/Nfty.Ticket.Tests --filter FullyQualifiedName~MenuRollsTests`. Expected: these fail to build because the types don't exist yet.
- [ ] **Step 3: Implement the four types** by moving the CLI bodies verbatim, keeping their message wording, then switch the CLI handlers to call them.
- [ ] **Step 4: Run** `dotnet test nfty.sln`. Expected: all pass, Cli.Tests unchanged.
- [ ] **Step 5: Commit** `refactor(core): move set-output guards, menu seed roll and book tuning out of the CLI`

### Task 2: Server scaffold, sandbox, error and stdout discipline

**Files:**
- Create: `src/Nfty.Mcp/Nfty.Mcp.csproj`
  - `OutputType` Exe, `PackAsTool` true, `ToolCommandName` `nfty-mcp`, `PackageId` `Nfty.Mcp`, `PackageType` `McpServer`.
  - References Core, Ticket and Model.
  - Links `..\Nfty.Cli\ErrorReport.cs` and `..\Nfty.Cli\Passphrase.cs`.
- Create: `src/Nfty.Mcp/Program.cs`, `ServerOptions.cs`, `Workspace.cs`, `ToolRun.cs`, `WriteLocks.cs`
- Modify: `Directory.Packages.props` (add `ModelContextProtocol`, latest stable 1.x at execution time, exact version), `nfty.sln` (add the two projects)
- Create: `tests/Nfty.Mcp.Tests/Nfty.Mcp.Tests.csproj`, `WorkspaceTests.cs`, `ServerProcessTests.cs`, `McpHarness.cs`
- Modify: `tests/Nfty.Core.Tests/AssemblyRulesTests.cs` (Core/Ticket/Model never reference `Nfty.Mcp` or `ModelContextProtocol`)

**Interfaces:**
- Produces:
  - `public sealed record ServerOptions(IReadOnlyList<string> Roots, Parallelism Threads)`, plus `static ServerOptions Parse(string[] args, string cwd, string? envThreads)`. It accepts repeatable `--root` and `--threads n|max`, and an unknown argument throws.
  - `public sealed class Workspace`:
    - `Workspace(IEnumerable<string> roots)`
    - `void AddClientRoots(IEnumerable<Uri> roots)`
    - `string Existing(string path)`: the file or folder must exist.
    - `string NewPath(string path)`: the parent must exist and the target must not.
    - `string Destination(string path)`: may exist, for `extend_set`/`tune_book`.
    - Each resolves relative paths against the first root, resolves every link (`FileSystemInfo.ResolveLinkTarget(returnFinalTarget: true)` on each existing ancestor), and refuses `"'{path}' is outside the folders this server may use: {roots}."`
  - `public static class ToolRun { Task<CallToolResult> Run(Func<CancellationToken, Task<CallToolResult>> body, CancellationToken ct); }`. It maps every non-cancel exception to `CallToolResult { IsError = true, Content = [text ErrorReport.Format(ex)] }`.
  - `public sealed class WriteLocks { IDisposable Acquire(string fullPath); }`. It throws `InvalidOperationException("'{path}' is already being written by another nfty tool call.")`.
  - `public static class Secrets { string Read(string source, string option); }`. It wraps `Passphrase.Read` and refuses `stdin`/`prompt` first: "Over MCP a secret comes from env:NAME or file:PATH; stdin carries the protocol and nothing can be prompted."
  - `McpHarness` (test): starts the built `Nfty.Mcp` with `StdioClientTransport` and `--root <temp>`, and exposes `McpClient Client`.

- [ ] **Step 1: Write failing tests**
  - `WorkspaceTests`:
    - `A_relative_path_resolves_under_the_first_root`
    - `Dot_dot_out_of_the_root_is_refused`
    - `An_absolute_path_outside_every_root_is_refused`
    - `A_junction_inside_a_root_pointing_outside_is_refused`: Windows uses `cmd /c mklink /J`, Linux uses `Directory.CreateSymbolicLink`.
    - `NewPath_refuses_an_existing_target`
  - `Secrets_refuse_stdin_and_prompt`
  - `ServerProcessTests`:
    - `The_server_lists_its_tools_and_names_itself`: server name "nfty", version = assembly informational version.
    - `A_tool_that_writes_to_console_leaves_the_protocol_stream_parseable`: a `DEBUG`-only `console_probe` tool prints to `Console.Out`, then `ListToolsAsync` still succeeds.
- [ ] **Step 2: Run** `dotnet test tests/Nfty.Mcp.Tests`. Expected: build fails.
- [ ] **Step 3: Implement the scaffold.**
  - `Program`: `Console.SetOut(Console.Error)`, then `Host.CreateApplicationBuilder`.
  - Logging goes to stderr only (`LogToStandardErrorThreshold = Trace`).
  - `.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()`, with singletons `Workspace`, `WriteLocks` and `ServerOptions`.
  - On initialize, add the client's roots when it declares the capability.
- [ ] **Step 4: Run tests.** Expected: PASS on Windows; the junction test also runs on the CI ubuntu leg in Task 8.
- [ ] **Step 5: Commit** `feat(mcp): stdio server scaffold with root sandbox and stderr-only logging`

### Task 3: Read tools — `inspect`, `validate`, `stats`, `set_item`

**Files:**
- Create: `src/Nfty.Mcp/Tools/ReadTools.cs`
- Test: `tests/Nfty.Mcp.Tests/ReadToolsTests.cs`

**Interfaces:**
- Consumes: `Workspace.Existing`, `ToolRun.Run`.
- Produces (all `[McpServerTool(ReadOnly = true, Idempotent = true)]`; the text content is the CLI's exact report, and `structuredContent` is the record):
  - `inspect(string path, bool voxel = false) -> InspectResult(string Kind, string Report)`. It dispatches exactly as the CLI's `inspect` handler (`CommandFactory.cs:184–450`): `.ktn`, `.mnu`, a Set folder, a `.set`, a bundle, then the authored kinds. `voxel` on `.ktn` is refused with the CLI sentence.
  - `validate(string path) -> ValidateResult(bool Valid, IReadOnlyList<string> Problems)`.
  - `stats(string cookbook) -> StatsResult(string Report, string UniqueDna, string Certainty)`, from `CollectionReport.Render` and `SpaceText.Describe`.
  - `set_item(string set, int number, string? cookbook = null, bool image = true) -> SetItemResult(int Number, string Dna, IReadOnlyList<TraitRow> Traits, string Odds, bool OddsExact)`, with `record TraitRow(string Trait, string Value, string Share)`.
    - `Odds` comes from `SelectionOdds` over `ArchivePeek.CookBookTree` when `cookbook` is given and its hash matches `set.json`. Otherwise it is the product estimate, prefixed `~`.
    - The image is the asset PNG, returned as image content when it is ≤ 1 MiB. Otherwise the text names its path.

- [ ] **Step 1: Write failing tests** against the demo book cooked to a temp Set (24 assets, seed "t"):
  - `Inspect_text_equals_the_cli_report`: capture the CLI by invoking `Nfty.Cli`'s `CommandFactory` root with redirected `Console.Out`.
  - `Validate_reports_a_broken_book_problems_verbatim`
  - `Stats_unique_dna_is_7155000_exact`
  - `Set_item_odds_are_exact_with_the_source_book_and_estimated_without`
  - `Set_item_out_of_range_names_the_set_size`
- [ ] **Step 2: Run** `dotnet test tests/Nfty.Mcp.Tests --filter FullyQualifiedName~ReadToolsTests`. Expected: FAIL.
- [ ] **Step 3: Implement.** Where the CLI handler formats inline, move that formatting into a Core `*Report` method first, so the CLI and the server print one string.
- [ ] **Step 4: Run tests.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(mcp): inspect, validate, stats and set_item tools`

### Task 4: Roll and render — `preview`, `roll`, `rise_model`

**Files:**
- Create: `src/Nfty.Mcp/Tools/RollTools.cs`, `src/Nfty.Mcp/ImageReply.cs`
- Test: `tests/Nfty.Mcp.Tests/RollToolsTests.cs`

**Interfaces:**
- Consumes: `MenuRolls.FromSeed` (Task 1), `Workspace`.
- Produces:
  - `ImageReply.Png(Image<Rgba32> img, string? outPath)`: returns image content when the encoded size is ≤ 1 MiB. When `outPath` is given it also writes the file there through `Workspace.NewPath`. It never resamples.
  - `preview(string archive, string? seed = null, string? variant = null, string? color = null, string[]? only = null, string[]? with = null, string? @out = null)`:
    - For `.rcp`/`.cbk` (`recipe` required for a `.cbk`) it uses `StackRoll.ForRecipe`; for `.igt` it uses `VariantPreview.Render`.
    - The other form's arguments are refused, never ignored, with the CLI's sentences (`CommandFactory.cs:579–760`).
  - `roll(string menu, string? seed = null, string? ticket = null, int maxRerolls = GenerateOptions.DefaultMaxRerolls, bool image = true, string? @out = null) -> RollResult(string Ticket, string Dna, string Recipe, IReadOnlyList<LayerPick> Layers)`, with `record LayerPick(string Layer, string? Variant, double? H, double? S)`. Exactly one of `seed`/`ticket` is allowed, using the `serve` validator sentences.
  - `rise_model(string source, string @out, string? variant = null, string? seed = null, string format = "glb", string pivot = "bottom-center", double voxelSize = 1) -> ModelResult(string Path, int Triangles, long Bytes)`. The source dispatch is `.png`/`.igt`/`.rcp`, as in `CommandFactory.Model.cs`. It uses `ModelTokens` for the strings and `ModelVoxelSize` for the range. `--front/--back` fill is NOT exposed, because it sculpts depth.
- [ ] **Step 1: Write failing tests:**
  - `Roll_seed_ticket_renders_identical_bytes_to_cli_serve`
  - `Roll_with_seed_and_ticket_is_refused`
  - `A_ticket_with_a_forged_dna_is_refused`
  - `Preview_rejects_variant_on_a_recipe`
  - `Large_images_are_written_and_named_not_inlined`: a 1024×1024 synthetic `.igt`.
  - `Rise_model_glb_equals_cli_model_bytes`
- [ ] **Step 2–4:** Run (FAIL), implement, run (PASS) with `--filter FullyQualifiedName~RollToolsTests`.
- [ ] **Step 5: Commit** `feat(mcp): preview, roll and rise_model tools`

### Task 5: Cook tools — `cook`, `extend_set`, `write_menu`

**Files:**
- Create: `src/Nfty.Mcp/Tools/CookTools.cs`, `src/Nfty.Mcp/ProgressBridge.cs`
- Test: `tests/Nfty.Mcp.Tests/CookToolsTests.cs`

**Interfaces:**
- Consumes: `SetOutputGuard` (Task 1), `WriteLocks`, `Workspace.NewPath`/`Destination`, `ServerOptions.Threads`.
- Produces:
  - `ProgressBridge.For(IProgress<ProgressNotificationValue>? p, string label) -> IProgress<GenerationProgress>` (and the `WriteProgress` overload). It reports `Progress = done`, `Total = count` and `Message = "{label} {done}/{total}"`.
  - `cook(string cookbook, string @out, int count, string seed, string? recipe = null, bool unlimited = false, bool pack = false, bool models = false, string modelFormat = "glb", string pivot = "bottom-center", double voxelSize = 1, int maxRerolls = default) -> CookResult(string Folder, int Assets, string? Packed, IReadOnlyList<int> SkippedModels)`.
    - It mirrors `generate` (`CommandFactory.cs:920–950`) using `Generator.GenerateAsync` and `SetWriter.WriteAsync` with progress and the request's cancellation token.
    - Model options without `models = true` are refused with the CLI sentence.
  - `extend_set(string cookbook, string set, int to, string seed = "nfty-extend", bool unlimited = false, bool pack = false, int maxRerolls = default) -> ExtendResult(int Added, int Total, string? ProvenanceWarning)`. The `SetProvenance.Warning` text is returned as a field, never as an error.
  - `write_menu(string cookbook, string @out, string art = "atlas") -> MenuResult(int Recipes, int Layers, int Variants, string Art, int Cells)`, reopened from the written file as the CLI does.
- [ ] **Step 1: Write failing tests:**
  - `Cook_matches_cli_generate_byte_for_byte`: hash every file under both folders, demo book, 30 assets, seed "parity", at threads 1 and max.
  - `Cook_into_an_existing_set_is_refused_with_the_extend_hint`
  - `A_canceled_cook_leaves_the_folder_as_it_was`: cancel after the first progress report; the folder listing equals the one before.
  - `A_second_writer_to_the_same_folder_is_refused`: hold `WriteLocks.Acquire`, then call `cook`.
  - `Cook_reports_progress_in_increasing_order`
  - `Extend_returns_the_provenance_warning_for_an_edited_book`
  - `Write_menu_counts_equal_cli_menu_output`
- [ ] **Step 2–4:** Run (FAIL), implement, run (PASS) with `--filter FullyQualifiedName~CookToolsTests`.
- [ ] **Step 5: Commit** `feat(mcp): cook, extend_set and write_menu with progress and cancellation`

### Task 6: `tune_book`

**Files:**
- Create: `src/Nfty.Mcp/Tools/TuneTools.cs`
- Test: `tests/Nfty.Mcp.Tests/TuneToolsTests.cs`

**Interfaces:**
- Consumes: `BookTuning.Apply`, `TuneOp` records, `ReplaceWrite.Write(path, write, expectedSha256)` (Task 1).
- Produces:
  - `tune_book(string cookbook, TuneOpDto[] ops, bool dryRun = false) -> TuneResult(bool Written, IReadOnlyList<string> Problems, string DnaBefore, string DnaAfter, string? TargetFit)`, marked `[McpServerTool(Destructive = true, Idempotent = false)]`.
  - `TuneOpDto`: a flat JSON object whose `op` is one of `variant_weight | recipe_weight | absent_chance | target_supply | add_rule | remove_rule`, plus the fields of the matching `TuneOp`. Rule targets are `{layer, variant}` pairs and the kind is `never|always`, as `add rule` takes them. An unknown `op` or a missing field names the op index: "ops[2] (variant_weight) needs 'layer'."
  - Flow:
    1. Read the book and keep its SHA-256.
    2. Apply the ops.
    3. Run `Validator.Validate`. Any problem means nothing is written and the problems are returned.
    4. Compute DNA space before and after (`UniqueSpace` → `SpaceText.Describe`).
    5. When `TargetSupply` is set, compute `TargetFit` using the supply rail's wording.
    6. Unless `dryRun`, call `ReplaceWrite.Write` with the kept hash.
- [ ] **Step 1: Write failing tests:**
  - `Dry_run_writes_nothing_and_reports_both_spaces`
  - `An_invalid_batch_writes_nothing_and_lists_problems`: weight 0 on the only variant.
  - `A_batch_is_all_or_nothing`: the 2nd op names an unknown layer and the file bytes are unchanged.
  - `A_book_changed_after_reading_is_not_overwritten`: the test hook `TuneTools.BeforeWrite` rewrites the file mid-call.
  - `A_tuned_book_cooks_the_new_weights`: variant weight 1000 → it appears in more than 90% of 200 cooked assets.
- [ ] **Step 2–4:** Run (FAIL), implement, run (PASS) with `--filter FullyQualifiedName~TuneToolsTests`.
- [ ] **Step 5: Commit** `feat(mcp): tune_book atomic numeric edits with dry run`

### Task 7: Publish — `export_plan`, `export_set`, `share_check`, `ipfs_status`, `upload_set`

**Files:**
- Create: `src/Nfty.Mcp/Tools/PublishTools.cs`, `src/Nfty.Mcp/ExportArgs.cs`
- Test: `tests/Nfty.Mcp.Tests/PublishToolsTests.cs`

**Interfaces:**
- Consumes: `Secrets.Read` (Task 2), `WriteLocks`, `ProgressBridge` (Task 5).
- Produces:
  - `record ExportArgs` mirrors the CLI `export` options (`CommandFactory.Publish.cs:184–400`): preset, the four content axes, shape, models, sheet with cols and rows, watermark with corner, the marketplace fields, `artOnly`, and `book`.
    - `ExportOptions ToOptions()` throws the CLI's sentences for conflicts.
    - `seal` is a `bool`, with `keySource` a string such as `env:NAME` or `file:PATH`.
  - `export_plan(string set, ExportArgs args) -> PlanResult(IReadOnlyList<string> Parts, string Size, IReadOnlyList<string> Warnings)`. It uses `SetExporter.Plan`, `Parts()`, `SizeText()` and the CLI-worded `Warnings` (names flags, since an agent maps them to fields; the tool description says so).
  - `export_set(string set, string @out, ExportArgs args) -> ExportResult(string Path, int Files, long Bytes, IReadOnlyList<string> Warnings)`. It uses `SetExporter.ExportAsync` with progress and cancellation. Exporting a `.tin` is refused by Core.
  - `share_check(string set) -> ShareCheckResult(bool Passes, IReadOnlyList<string> Problems)`, from `ShareCheck.CheckAsync`.
  - `ipfs_status(string provider = "kubo", string? api = null, string? keySource = null) -> IpfsStatusResult(bool Reachable, string Message)`, marked `[McpServerTool(ReadOnly = true, OpenWorld = true)]`.
  - `upload_set(string set, string @out, ExportArgs args, string provider = "kubo", string? api = null, string? keySource = null) -> UploadResult(string ImagesCid, string? ModelsCid, string MetadataCid, string ContractUri, string Folder)`, marked `[McpServerTool(OpenWorld = true, Destructive = false)]`.
    - It refuses first with `MarketplaceUpload.Problem(options)`, then calls `MarketplaceUpload.RunAsync`.
    - The description says plainly that pinned content is public and can't be recalled once others fetch it.
- [ ] **Step 1: Write failing tests:**
  - `Plan_parts_equal_what_export_writes`
  - `Export_matches_cli_export_byte_for_byte`: the marketplace preset with base URIs.
  - `Seal_with_a_bare_value_key_is_refused_and_names_the_forms`
  - `Seal_with_env_key_writes_a_tin`
  - `Upload_of_a_sealed_export_is_refused_before_any_network_call`: a fake `IPinningService` counts calls; `upload_set` takes an internal service factory seam.
  - `Upload_uses_the_fake_pin_service_and_returns_the_three_cids`
  - `Share_check_reports_the_rule_sentences_verbatim`
- [ ] **Step 2–4:** Run (FAIL), implement, run (PASS) with `--filter FullyQualifiedName~PublishToolsTests`.
- [ ] **Step 5: Commit** `feat(mcp): export, share check and IPFS upload tools`

### Task 8: Package, document, CI, release

**Files:**
- Create: `src/Nfty.Mcp/.mcp/server.json` (Registry schema: name `io.github.corderro-artz/nfty`, the NuGet package `Nfty.Mcp`, transport stdio, a `--root` argument hint, version `${version}` stamped by build)
- Create: `src/Nfty.Mcp/README.md` (the NuGet readme: install, client config for Claude Code / Claude Desktop / VS Code, roots, the tool table)
- Create: `docs/manual/embedding/mcp.md`
- Modify: `mkdocs.yml` (nav under Embedding), `.github/workflows/ci.yml` (add `Nfty.Mcp.Tests` to `build-and-test` and to the `app-linux` ubuntu leg), `tools/release/build.py` (also `dotnet pack src/Nfty.Mcp -c Release -o .release` → `Nfty.Mcp.<ver>.nupkg`), `CLAUDE.md` (Project layout: one `Nfty.Mcp` entry stating the tool rules: thin adapters, one copy of policy, stdout discipline, secret sources)
- Test: `tests/Nfty.Mcp.Tests/PackagingTests.cs`

- [ ] **Step 1: Write failing tests:**
  - `Server_json_version_equals_the_assembly_version`
  - `Every_tool_has_a_description_and_every_parameter_too`: reflection over `[McpServerTool]` methods.
  - `Every_tool_named_in_the_manual_exists_and_none_is_missing`: parse the `mcp.md` table.
- [ ] **Step 2: Run** `dotnet test tests/Nfty.Mcp.Tests --filter FullyQualifiedName~PackagingTests`. Expected: FAIL.
- [ ] **Step 3: Write the files above.** The manual page follows the Diátaxis task shape: one intro sentence, prerequisites, client config snippets, what each tool does, and the "left out" list with reasons, copied from the spec.
- [ ] **Step 4: Verify**
  - `dotnet test nfty.sln` passes.
  - `python -m mkdocs build --strict` passes.
  - `dotnet pack src/Nfty.Mcp -c Release` produces a nupkg containing `.mcp/server.json` and `tools/net10.0/any/nfty-mcp.dll`.
  - Run locally with `dotnet tool install --add-source <dir> Nfty.Mcp -g` and register in Claude Code (`claude mcp add nfty -- nfty-mcp --root <kitchen>`). Drive `stats` → `tune_book` (dry run) → `cook` → `set_item` → `export_plan` on the demo book. Record what was seen.
- [ ] **Step 5: Commit** `feat(mcp): package as dotnet tool, manual page, CI and release artifact`
- [ ] **Step 6 (author, manual):** `dotnet nuget push .release/Nfty.Mcp.<ver>.nupkg`, then publish `server.json` to the MCP Registry with `mcp-publisher`. Publishing is outward-facing, so the author runs it; it is not automated.
