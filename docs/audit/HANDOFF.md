# Final handoff

Active repository: M:/Repositories/nfty, main. The original nine two-pass audits and every approved
follow-up are implemented. AFTER-ACTION.md lists changes/results; REVIEW.md and
12-usability-followups.md track completion; 13-viewer-quality.md and 14-header-spacing.md explain
the final viewer work. Intentional optional work remains in BACKLOG.md, including AA/GPU; the user
explicitly chose CPU rendering and identical default views.

## Releases and verification

- 2.4.8/2.4.9: original full audit, centering, Cook cleanup and documentation/security closeout.
- 2.4.10: Cook destination safety and opt-in CookBook folder naming; main 5bd61f3.
- 2.4.11: footer animations and per-type Windows association/default evidence; main d6147c6.
- 2.4.12: CPU preview lighting, measured responsiveness, shared footer controls and current images;
  main dec282d. Default 90-frame hashes unchanged; opaque/scaled/theme/reset proof exact.
- 2.4.13: final 9 DIP viewer-header spacing and documentation patch. Canonical publication/CI/source
  status is the matching GitHub release. Do not publish if any exact-commit gate fails.

Each release has eight Windows/Linux packages. Through 2.4.12, all server digests, native startup/
centering and 67 cross-platform/cross-shape/cross-release output payloads are verified. Final 2.4.13
uses the same mandatory proof before publication. Recent Windows component total: 5,451 passes;
Linux App 2,133 and Model 679 passes with documented platform capability skips. Final header-specific
selection: 39 passes. Builds have zero warnings/errors; ImageSharp stays 3.1.11 and fixtures are frozen.

## Evidence and cleanup

Ignored evidence is preserved on M: with SHA inventories. Cook/Settings feature branches are deleted;
old ticket checkout archived. Preview evidence is preserved before its managed checkout archive and
branch deletion. The current primary Settings checkout cannot be removed by Codex; keep it clean,
detached at final main. Do not bypass that protection or touch unrelated historical worktrees.

## Documentation

The reusable XML/reflection generator produces 270 Ticket/Core/Model pages: run
python tools/api-docs/build.py --check and --lint separately. MkDocs builds HTML. Final site publication
uses the existing deploy.yml workflow in corderro-artz/corderro-artz.github.io; verify the actual live
version, eight current Cook/Rise/viewer image hashes and PreviewLighting API page afterward. The
optional SITE_DISPATCH_TOKEN is absent, so a green notification alone is not deployment proof.

Read CLAUDE.md/AGENTS.md before future edits. No fixture regeneration, archived-design edits, package
license upgrades, UserChoice writes or real default-app changes were used to make tests pass.
