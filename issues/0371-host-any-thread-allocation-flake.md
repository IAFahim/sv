# 0371 Host "allocate nothing on any thread" tests read 168 B at workers:1, intermittently, on 1aa315e too

Status: open

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

Not investigated: which thread allocates. With workers:1 sv starts no worker threads, so the 168 B comes from a runtime or
test-host thread (tiered-compilation background work is the first suspect). The Sv.Bench warm frames read 0 B on the main thread
in every P2 run and 0 B on all threads in most (6,192 B appears in about one process in ten, on both builds).
