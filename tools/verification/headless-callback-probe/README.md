# Headless worker dispatcher regression

Run from the repository root:

```powershell
dotnet run --project tools/verification/headless-callback-probe -c Release
dotnet run --project tools/verification/headless-callback-probe -c Release --no-build -- --dispose
```

Both commands must return zero and print `second application delegate entered=True; failure=none`.
The first build builds `Nfty.App` and its dependencies; the second reuses those binaries. The probe
is outside the solution and starts its own process. It intentionally uses the existing
`Nfty.App.Tests` friend assembly name to await the production tile's internal `PendingDecode` handle.
It does not reference or run the xUnit assembly.

The first isolated application starts a real `GalleryItem` thumbnail decode. Its loader blocks on
an explicit gate. The next application's rendering initializer opens that gate after Headless has
reset the global dispatcher and Skia has registered bitmap services, but before Headless creates
its compositor. The callback is therefore forced into the vulnerable interleaving without sleeps
or timing retries. `--dispose` disposes the tile before releasing the decode, covering a worker
that outlives its owner. The default case also checks that a still-live tile retains its original
dispatcher rather than discovering the next application's dispatcher on the worker.

Before the fix, both commands fail through `Dispatcher.VerifyAccess`, `DefaultRenderLoop.Add`,
and `HeadlessUnitTestSession.EnsureIsolatedApplication`; the second application's delegate never
enters. With the fix, it enters and verifies UI access. Each dispatch still uses Headless's default
per-test isolation. There is no retry, exception suppression, private reflection, or replacement
dispatcher.

Do not put this proof inside an `AvaloniaFact`: the assembly's runner owns an outer Headless
application, and resetting its dispatcher from a nested session invalidates that outer lifetime.
Ordinary App tests separately cover queued-book disposal and mesh completion without draining a
disposed owner's UI queue.
