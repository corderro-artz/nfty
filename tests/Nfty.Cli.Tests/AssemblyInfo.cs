using Xunit;

// KitchenCommandTests.Capture and CommandFactoryTests.RunAndCaptureStdout both redirect
// Console.Out and restore it afterwards, and AuthoringCommandsTests writes to it throughout.
// Console.Out is PROCESS-WIDE, so cross-class parallel execution lets those interleave: one class
// captures Console.Out while another class's StringWriter is installed, then "restores" that
// writer after its owner has disposed it — and every later Console.WriteLine in the process throws
// ObjectDisposedException("Cannot write to a closed TextWriter") from inside an unrelated command.
//
// The race is load-dependent, so the suite passed on its own and failed when `dotnet test
// nfty.sln` ran the three assemblies at once. Serialising costs this assembly about a second.
// Nfty.Core.Tests disables parallelization for the same class of reason (a process-wide counter).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
