# 0371 Host "allocate nothing on any thread" tests read 168 B at workers:1, intermittently, on 1aa315e too

Status: closed (2026-11-14: the tests now measure the property they name; see Resolution)

Found by P2 (branch v3/p2-perf) while verifying each perf commit on Linux 7.2, Ryzen 5 8500G, .NET SDK 10.0.403.

Tests (tests/Sv.Host.Tests/AllocationTests.cs, not edited):
- `Warm_frames_of_a_pack_that_gains_and_loses_cards_allocate_nothing_on_any_thread(workers: 1)`
- `Warm_frames_of_the_reference_pack_allocate_nothing_on_any_thread(workers: 1)`

Both assert `GC.GetTotalAllocatedBytes(true)` delta 0 over the minimum of three 32-frame windows after 200 warm frames.
Observed `Actual: 168` in some runs; the main-thread assertion passes, and the workers:7 cases pass.

Receipts on the pristine 1aa315e build (git archive, built in a scratch folder):
- `DOTNET_EnableAVX2=0 dotnet test tests/Sv.Host.Tests --no-build`: 1 failed (gains_and_loses_cards), twice in a row.
- `DOTNET_EnableHWIntrinsic=0 dotnet test tests/Sv.Host.Tests --no-build`: 2 failed (both tests), twice in a row.
- default: 1 failed in 1 of 4 runs.
On v3/p2-perf the same tests fail with the same 168 B in some default, AVX2=0 and HWIntrinsic=0 runs and pass in others.

P3 (2026-11-14, worktree branch from v3/m8-unity 6c219e2): a third intermittent allocation test,
`Sv.Laws.Tests.InputLogCapLaws.PerFrameByteCapBoundsBlocksPerFrameAndAllocatesNothingWarm`, failed in the first
`DOTNET_EnableHWIntrinsic=0` Laws run of 2 of 5 full suite sequences (default, AVX2=0, HWIntrinsic=0, one after the other, with
other agents loading the box to load average 8-10). Isolated reruns: 7 of 7 green on the P3 trees, 5 of 5 green on pristine 6c219e2
(git archive, fresh build). The P3 commits do not touch InputLog; same class as the two Host tests (warm-allocation reads that depend
on JIT tier-up timing, suspected, not proven).

Not investigated: which thread allocates. With workers:1 sv starts no worker threads, so the 168 B comes from a runtime or
test-host thread (tiered-compilation background work is the first suspect). The Sv.Bench warm frames read 0 B on the main thread
in every P2 run and 0 B on all threads in most (6,192 B appears in about one process in ten, on both builds).

## 2026-11-14 (merge verification of P3 + 0400)

`AllocationLaws.WritesEpochMarksDigestAndRewindAllocateNothingWarm(policy: Dense)` failed once, in the first Laws run under `DOTNET_EnableAVX2=0` right after a rebuild, on a box with load average about 5 (a nice-19 `localsearch` indexer at 100 percent). It then passed 6 of 6 full Laws runs and 5 of 5 isolated runs under the same setting. Same family as the Host any-thread tests and `InputLogCapLaws...AllocatesNothingWarm`: first run after a build, tiering JIT allocation on a loaded machine. Not fixed; if it recurs on an idle box, look at JIT tiering (`TieredPGO=0`, `TieredCompilation` quick jit) in the test host rather than the engine.

## Resolution (2026-11-14)

Cause, found by elimination and tracing:
- Not tiered compilation: with `DOTNET_TieredCompilation=0` the Host test still failed 1 of 4 runs under `DOTNET_EnableHWIntrinsic=0` (default settings: 1 of 4).
- An EventPipe allocation trace (`GCSampledObjectAllocationHigh`) of the test host shows several non-sv threads (xunit message bus, vstest result channel) allocating while tests run. `GC.GetTotalAllocatedBytes(true)` counts them, so "any thread" measured the test host, not sv. Seen as 168, 224 and 504 B, at workers 1 and 7.

Fix:
- `Workers` records `GC.GetAllocatedBytesForCurrentThread()` on each worker at the end of its share of every `Run` (`FrameLoop.WorkerAllocatedBytes`, internal). The Host tests (renamed `..._allocate_nothing_on_any_sv_thread`) assert 0 B on the main thread and 0 B over the main thread plus every sv worker, minimum of three 32-frame windows.
- Laws `WarmBytes` and `InputLogCapLaws` take the minimum of three measured runs (a warm-path leak allocates on every run, a one-off runtime allocation does not).
- Negative control: a 16-byte allocation in `Phases.Decide` on `sv-worker` threads only makes both workers:7 Host tests fail (7,760 and 8,240 B) while workers:1 pass.
- Receipts: Host and Laws 6 of 6 green under each of default, AVX2=0 and HWIntrinsic=0 (36 runs); all other suites green; Unity gate 101/101 (Workers.cs compiles in Unity); B digest unchanged, B interleaved 11.14 -> 11.12 ms (no cost, loaded box).
