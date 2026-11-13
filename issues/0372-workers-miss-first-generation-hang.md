# 0372 Workers.Run hangs when a worker thread starts after the first Run: the worker takes that generation as seen

Status: open (fix in the P3 branch, closes after a second run of the full suites on it)

Found by P3 (2026-11-14, worktree branch from v3/m8-unity 6c219e2) while running the suites with other agents loading the box
(load average 8-10).

Receipt:
- `DOTNET_EnableHWIntrinsic=0 dotnet test tests/Sv.Diff.Tests -c Release` hung for over 11 minutes (the same run takes 33-39 s).
- `dotnet-dump collect` + `clrstack -all` on the test host: the test thread
  (`BricksCompiledVersusReference.Digests_equal_the_reference_at_1_2_4_7_11_workers_on_both_storage_policies`, 4 workers) spun in
  `Workers.Run` waiting for `finished == threads.Length` from `FrameLoop.SealPhase`; all three `sv-worker` threads were parked in
  `Workers.Await` (`ManualResetEventSlim.Wait`).

Cause (src/Sv.Host/Workers.cs, unchanged since C2): each worker read `generation` as its first statement in `Loop`, on its own
thread. When the constructor returns and the caller's first `Run` increments `generation` before a new thread gets to that line,
the worker starts with `seen` equal to the new generation, never runs it and never counts it in `finished`, so `Run` spins forever.
More likely under load and slow JIT (HWIntrinsic=0), when thread start is late.

Fix: the constructor reads `generation` once before it starts the threads and passes it to `Loop(worker, born)`; a worker can no
longer treat an unseen generation as seen. No behaviour change otherwise (Unity runs phases through its job system, not this pool).
No test added: a regression would hang rather than fail.
