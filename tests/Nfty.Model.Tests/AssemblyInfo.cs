using Xunit;

// RisePerfTests and RasterPerfTests both call Perf.Enable()/Reset()/Snapshot()/Disable() around the
// path they measure — Perf's Buckets dictionary (src/Nfty.Core/Diagnostics/Perf.cs) is one
// process-wide static, not per-test state, so a Reset() from one class mid-run wipes scopes another
// class already recorded. Cross-class parallel execution reproduced this deterministically (2 of 4
// theory cases failing every run, Riser.TryBuild missing from the snapshot because RasterPerfTests'
// own Reset() landed between it and StackDepth.Combine). Same shape of bug, same fix, as
// tests/Nfty.Core.Tests/AssemblyInfo.cs (ImageSharp's process-wide MemoryDiagnostics counter).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
