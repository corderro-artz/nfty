using Xunit;

// ArchiveReadDisposalTests and GeneratorRollOneDisposalTests assert leak-freedom via ImageSharp's
// process-wide MemoryDiagnostics.TotalUndisposedAllocationCount (see those files for why). That
// counter is shared by the whole test process, so it must not move for reasons unrelated to the
// test under assertion — cross-class parallel execution would let another test's image
// allocate/dispose land inside the snapshot window and produce a false positive or negative.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
