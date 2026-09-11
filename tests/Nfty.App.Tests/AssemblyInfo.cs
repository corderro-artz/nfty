using Xunit;

// Nfty.App.Tests asserts leak-freedom in two places through ImageSharp's process-wide
// MemoryDiagnostics.TotalUndisposedAllocationCount - IngredientEditorReferencesTests
// .Nothing_the_panel_allocates_outlives_the_editor and SessionLifecycleTests
// .Closing_a_cookbook_frees_every_decoded_variant_image. That counter belongs to the whole test
// process and moves ONLY on an explicit allocate or dispose (probed: a leaked Image<Rgba32> left to
// the finalizer keeps the count up across GC.Collect + WaitForPendingFinalizers), so the only thing
// that can move it inside a snapshot window is another test - and with cross-class parallelization
// on, any class that opens or closes a CookBook will.
//
// It failed on CI rather than locally and it failed DOWNWARD (expected 3199, got 3192): seven
// images another class disposed landed inside the window. A leak would have read high; a count that
// reads low is proof the window is not the test's own. Adding two image-churning editor classes in
// 1.0.0 is what made it likely enough to show.
//
// Nfty.Core.Tests and Nfty.Cli.Tests already serialize for the same class of reason - a
// process-wide counter there, process-wide Console.Out here. This assembly was the one of the three
// that needed it and did not say so.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
