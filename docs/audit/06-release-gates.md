# Release verification — 2.4.8

Date: 2026-11-07. Status: candidate; final checks and main merge/publication pending.

The user approved finishing the release gates and merging/publishing from main only if no unintended regressions are found. Old Set/archive compatibility is explicitly not required for this in-development project. Remaining App implementation and later audits stay in the tracker for the next usage refresh.

## Additional determinism correction

Extracted Windows/Linux package tests exercised demo extraction, seeded generation with models, extension, Menu creation and seeded Menu rendering. All four packaging shapes agreed within each OS. Across systems, PNG/model/DNA results agreed, but indented JSON contained CRLF on Windows and LF on Linux; Set-index metadata lengths consequently differed.

The shared `Json.Options` now specifies canonical LF. Three focused tests failed before the change and pass after it: exact nested metadata bytes, synchronous archive manifest bytes and asynchronous archive manifest bytes. This intentionally changes Windows JSON whitespace and corresponding cache lengths, while preserving parsed values and generated art/model identities. It changes no RNG, mesh or color arithmetic. Frozen fixtures were not regenerated.

## Gates

- [x] Completed Ticket/Core/Model/CLI checkpoint: zero-warning build, 5,228 passed/one platform skip, sampled output and culture/thread proof.
- [x] Initial four Windows and four Linux x64 package builds completed and their actual CLIs exercised. Detected the cross-platform newline issue above rather than accepting semantic equality as byte identity.
- [x] Packaged Windows portable app opened the demo and Cook dialog; seed reroll changed the displayed value. Other Windows shapes reached the native input loop and remained running through their startup deadline.
- [x] H01 investigated against installed Avalonia 12.1.1: a controlled worker can claim the global UI dispatcher between the headless reset and compositor setup, reproducing the exact failure before the test delegate starts. Original competing callback unidentified. No production/harness workaround, isolation weakening, hidden retries or dependency upgrade was applied.
- [ ] Fresh Release solution build/test after canonical LF and version bump.
- [ ] Rebuild all eight assets from the committed candidate; verify extracted CLIs and cross-platform byte identity.
- [ ] Verify native packaged startup and compare the existing broad output sample, allowing only canonical whitespace/cache-length corrections.
- [ ] Merge the verified candidate into main, tag, push and publish; verify GitHub release assets.

H01 remains a known intermittent test-framework limitation, not a repaired defect. A clean run is required for this candidate, and any failures must remain visible. The similar failure was already observed on the unmodified earlier baseline; it is not evidence of a new product regression. See `artifacts/audit/release-2.4.8/harness-gate.md` for exact binaries, mechanism, controlled proof and limits.

Existing App findings A01–A21 remain unimplemented. The release will include the completed library/CLI fixes and this additional LF correction; it does not claim to complete the App audit implementation.
