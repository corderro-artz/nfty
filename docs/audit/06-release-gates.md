# Release verification — 2.4.8

Date: 2026-11-08. Status: approved App implementation and code verification complete; merge and publication remain on hold. Final assets require rebuilding.

The user approved finishing the release gates and merging/publishing from main only if no unintended regressions are found. Old Set/archive compatibility is explicitly not required for this in-development project. App implementation is now verified; Desktop and later reviews remain not started at the requested stop boundary.

## Additional determinism correction

Extracted Windows/Linux package tests exercised demo extraction, seeded generation with models, extension, Menu creation and seeded Menu rendering. All four packaging shapes agreed within each OS. Across systems, PNG/model/DNA results agreed, but indented JSON contained CRLF on Windows and LF on Linux; Set-index metadata lengths consequently differed.

The shared `Json.Options` now specifies canonical LF. Three focused tests failed before the change and pass after it: exact nested metadata bytes, synchronous archive manifest bytes and asynchronous archive manifest bytes. This intentionally changes Windows JSON whitespace and corresponding cache lengths, while preserving parsed values and generated art/model identities. It changes no RNG, mesh or color arithmetic. Frozen fixtures were not regenerated.

## Gates

- [x] Completed Ticket/Core/Model/CLI checkpoint: zero-warning build, 5,228 passed/one platform skip, sampled output and culture/thread proof.
- [x] Initial four Windows and four Linux x64 package builds completed and their actual CLIs exercised. Detected the cross-platform newline issue above rather than accepting semantic equality as byte identity.
- [x] Packaged Windows portable app opened the demo and Cook dialog; seed reroll changed the displayed value. Other Windows shapes reached the native input loop and remained running through their startup deadline.
- [x] H01 demonstrated with an actual late application callback, corrected by retaining owner dispatchers and disposing unpublished results. Controlled live/disposed cases fail on the old App and pass on the fixed App; per-test isolation and package pins remain unchanged. See the final App report.
- [x] Fresh Release solution build/test after canonical LF and version bump: zero warnings/errors; 5,231 passed, zero failed, one platform skip (App 1,928 passed).
- [ ] Rebuild all eight assets from the committed candidate; verify extracted CLIs and cross-platform byte identity.
- [x] Compare the existing broad output sample: 470 files, exactly 264 raw matches, 201 LF-only JSON changes and five correctly updated cache-size records; no unexpected differences.
- [x] Verify canonical Windows/Linux output directly from updated CLIs: all 67 payloads match byte-for-byte, including model/image/Menu payloads and the Set index.
- [x] Initial Linux package native startup: all four shapes remained alive at the smoke deadline with empty application stderr under a private Xvfb display. A first xvfb-run wrapper cleanup failure and auto-display socket failure were diagnosed as smoke infrastructure errors, not counted as product passes; direct explicit-display runs succeeded.
- [ ] Merge the verified candidate into main, tag, push and publish; verify GitHub release assets.

The earlier H01 investigation is historical. The actual application callback is now reproduced and corrected: workers retain their owner dispatcher, and abandoned results have explicit disposal/completion. Both controlled interleavings pass; the final full suite passes without retries or isolation changes. See [App verification](05-app-verification.md) and `artifacts/audit/h01-final-investigation.md`; the upstream framework reset gap itself is unchanged.

App findings A01–A21, XML and simplification work are implemented and verified. Final Release gate: 5,335 passed, zero failed, one Linux-only skip; zero build warnings/errors. Existing packaged binaries predate these fixes and cannot certify or publish the final checkpoint. Code merge readiness is separate from rebuilding and publishing release assets.

Latest user direction: more usage acquired; continue approved App fixes and reporting, with **no merge until done**. Existing packaged binaries predate those fixes and must not be published as the final release.
