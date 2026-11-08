# Audit handoff — 2026-11-08

Main checkout: `M:/Repositories/nfty`. Audited code `af84cf6`, README/release tree `6c07a38`.
Version 2.4.8 is tagged, pushed and published from main with eight verified assets. All nine hosted
CI jobs passed on `6c07a38`; local full solution 5,368 passes/one Linux-only skip and zero-warning
Release build. Output 470/470 exact; packaged Windows/Linux output 67/67 exact in all shapes.
Native Linux main and viewer coordinates are exactly centered. See AFTER-ACTION.md, REVIEW.md
and releases/2.4.8.json.

## Active 2.4.9 follow-ups

User requested a separate Cook options/footer cleanup, screenshots where needed, then a new main
commit/tag/push/release. No engine changes. Desktop reviewer owns Cook markup/hints/tests and the
.NET build slot; coordinator owns version/docs/final verification/releases. Tooling reviewer has
completed the Pillow13/Python3.11 pin update after isolated demo/tool regression proof, and is
preparing the relevant manual screenshot mapping. Two subagents maximum.

21 non-generated manual pages are corrected/strict-built and pending commit. README correction
was already committed separately. AFTER-ACTION.md is current locally and explicitly marks new
follow-ups pending. Do not represent 2.4.9 as released until its own checks finish.

## Cleanup and evidence

The merged `codex/ticket-audit-fixes` branch is deleted after verifying main ancestry and the
published 2.4.8 gates. All 25,780 ignored evidence files were copied to main artifacts/audit and
SHA-256 verified; inventory: artifacts/audit-preservation-sha256.json. The native archive tool
refused folder removal with “The task’s primary checkout cannot be removed.” Therefore
C:/Users/Corde/.codex/worktrees/ticket-audit-fixes/nfty remains detached at af84cf6. Do not bypass
that protection. All active development, commits and releases use M:/Repositories/nfty/main.
Other historical worktrees belong to other work and were not touched. Earlier candidate packages
must never replace final rebuilt assets. The release builder deletes .release each run.

Native Windows CUA was interrupted by physical Escape after actual demo/Cook/seed-reroll
verification. User later reauthorized desktop use; the attempted CUA completion summary was
aborted before dispatch. No further desktop input was sent. Linux native startup/centering and
Windows hidden startup checks passed independently. Avoid interfering with other running apps.

Read CLAUDE.md and AGENTS.md. Never regenerate tests/fixtures, edit docs/design/archive, or upgrade
ImageSharp beyond3.1.11. Preserve determinism, image ownership and fixed geometry. BACKLOG.md holds
intentional deferrals; completed external model-viewer validation is recorded there.

## Live verification checkpoint

Cook implementation is frozen:49focused passes,7newcases,18fixed-footer pixel comparisons,
production430×421DIPcard. Four actual50asset/50modelmanualscreenshots refreshed,520×509,
commoncrop(436,135,956,644), noresampling; strictmanualpasses. Noengineproductionfileschanged.
Pillow13/Python3.11pin applied andisolateddemo/toolproofpassed. Bothsubagentsfinished.

Firstfinal2.4.9build:0warnings/errors. Fullcapture run:alllibrarytargets passed,App2066pass,
1policyfailure,1Linuxskip. Guardcaught gate.Wait() innewworker-onlyscan test. Rootreplacedit
with boundedManualResetEventSlim.Wait(TimeSpan.FromSeconds(10));a mistakenParallelism-as-token
intermediatecompileattemptwascorrected. FocusedCook+UiThreadWaitrecheck10/10green. Corrected
complete solution run is active, execsession28669;log artifacts/audit/2.4.9-final-tests-corrected.log.
Expected5375passes/1skip, butdonotclaimuntilactualexit observed.

Fullcapture453PNGs:431baseline+22new;403commonframespixel-exact,20Cookchangesintentional,
6temporarypathlabels,2minorunchanged-scope rastervariations(2and4pixels;inspectmaxdelta).
Inventory artifacts/audit/2.4.9-frame-parity.json. Productionunchanged byboundedtestgatecorrection,
sofirstcapture remainsvalidvisualevidence;redrunisnotsuccessfullycompletedfullsuite.

2.4.9helpers alreadyprepared underM artifacts/audit/release-2.4.9:build-final.py,verify-packages.py,
linux-native-final.py. BuildhelperpublishesMmainandpreservesWin.releasebeforeLinuxcleanup.
VerifierneedsNFTY_CODE_ROOT=M:/Repositories/nfty andNFTY_RELEASE_PROOF=<thatMproofdir>.
Itretains2.4.8verificationseeds so67payloads cancompare2.4.8versus2.4.9. Versionprops2.4.9edited.
No2.4.9commits/tags/push/packages/releaseyet. Commitsecurity,UI+version,docs separatelyoncegreen;
finalbuildassetsfromcommittedmain,verify8shapes/native/crossreleaseparity,tag/pushmain andpublish.

APIHTMLisalreadyLIVEHTTP200athttps://www.vaporsoft.dev/nfty/docs/reference/api/,269generated
Ticket/Core/ModelpagesfromcompilerXML+reflection. READMEaddedlinkandreusablecommands.
notify-site tokenisabsent(auto30minpoll);forfinaldocsLIVErequirement rootcaninvokeexisting
DeploytoGitHubPages workflow_dispatch in corderro-artz/corderro-artz.github.io afterfinalpush.
Workflowdeploy.yml inspectedandsupportsdispatch. VerifyactualfourlivePNGhashesandnewmanualtext,
CI/latestreleasebadges/dependencyalerts. Do notmodifywebsitecode,secrets orpermissions.
