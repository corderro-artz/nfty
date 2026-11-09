# Viewer header spacing

The document icon touched the header divider because TitleCenter had no leading gap. The existing
brand-side gap was 9 DIP. A single leading margin on ViewerWindow's TitleCenter Grid now matches
that gap. Shared WindowFrame spacing and main-window breadcrumb padding are unchanged.

## Verification and result

- Existing ViewerChrome/ViewerWindow/WindowChrome/ChromeReserve/MinimumWindowFit selection:
  **39 passed, zero skipped**; builds emitted zero warnings/errors.
- Fresh production captures in both themes at default 1288×776 and minimum 960×540 show balanced
  9/9 DIP gaps, working titlebar/caption hit tests, and no text/control overlap.
- Four viewer RGBA comparisons change only a 76×12 pixel header region. Every pixel outside it is
  exact; both Rise frames are entirely exact. The two updated manual images are exact 1264×752
  crops of their current production captures, with loaded-assembly/source hashes recorded.
- No runtime math, renderer, archive format, viewport or footer geometry changes.
- The initial ignored harness corrected its own titlebar self-hit expectation; no product workaround
  or added low-impact implementation-mirroring test was introduced.

The final documentation pass also corrects the release READ-ME's overly broad portability claim:
optional file registrations and OS-saved credentials live outside the portable folder. The manual
now calls App-routed archives supported rather than implying that sealed/generated Sets are editable.
Existing tooling tests pass with three platform capability skips.

Evidence: artifacts/audit/header-spacing-2.4.13/ on the main checkout. Source, current images and
reports are included in the final main release commit. Publication is gated on all nine CI jobs
passing on that exact commit, package/native/output checks and matching uploaded asset digests.
The canonical publication record is [v2.4.13](https://github.com/corderro-artz/nfty/releases/tag/v2.4.13).
