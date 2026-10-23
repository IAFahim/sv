# Issue 0089 — Atom 81 (roadmap 6.5b): the persistent worker pool — bulk passes scale with cores

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) phase 6, atom 6.5,
second slice (§5.6). Labels: `ai`, `area:core`, `kind:feat`. Contract:
none.

## Changes

- New `Sv.Bulk` — a persistent pool of `ProcessorCount − 1` background
  worker threads (created lazily on first use, one-time managed
  allocation; zero on the warm path), synchronized with a sense-reversing
  spin barrier. A busy flag rejects reentrant calls. Core pinning is
  deferred (affinity p/invoke is libc-shaped; the pool's scaling does
  not depend on it) — noted here so the follow-up has a home.
- `Bulk.Run(value, window, delta, bounds, applied = default)` — the bulk
  twin of `Kernel.Run`: the caller partitions the window into
  `participants` contiguous slices (equal row counts for range windows,
  whole-word ranges for masked windows — sub-windows are exact `Window`
  values, and `Kernel.Run`'s guards re-validate each), publishes the job
  through the barrier, executes partition 0 itself, and rejoins at the
  completion barrier. Determinism is L9 by construction: fixed disjoint
  partitions, each pure — the result is identical to the sequential run
  regardless of scheduling.
- One core → the pool has no extra threads and `Bulk.Run` is the
  sequential `Kernel.Run`.

## Tests

- New `BulkTests`: parallel `Bulk.Run` equals sequential `Kernel.Run`
  for random range and masked windows (values and `applied`, several
  repeats); reentrancy throws; after warm-up the caller thread allocates
  zero managed bytes.

## Bench

- New `bulkpass` scenario: a 32M-row pass with per-row bound columns
  (the statclamp shape — five streams per row), sequential `Kernel.Run`
  vs `Bulk.Run` measured **interleaved** so both see the same
  cache/DRAM state. L9 equality is the hard gate; the speedup gate
  trips only on pool pathologies (no-regression), because on this
  hardware the honest ratio is modest — see receipts.

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **348/348 passed** (345 + 3
  new); 3× repeat stable.
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 348/348.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27.
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios
  ok, alloc 0 B; `bulkpass` seq 18.1 ms → bulk 14.2 ms, **1.28×** on 12
  cores.
- **The scaling receipt is killed on this hardware class, honestly.**
  Measured across sizes (64 MiB L3-resident: 0.9× batch-measured; 128
  MiB and 256 MiB DRAM-bound, two or five streams: 1.07–1.3×): one
  core already near-saturates DRAM for stream-shaped kernels, so
  "bulk passes scale with cores" does not hold here regardless of
  stream count. A small probe process once showed 3.3× — traced to its
  sequential run degrading 3× on unlucky buffer address layout, not a
  real pool gain. What survives: the pool is correct (L9 equality
  gated every run), deadlock-free, exception-safe, and delivers a
  consistent 1.1–1.3×; real scaling awaits compute-bound kernels
  (6.2 Burst, §4.11 curves) or memory systems with per-core bandwidth
  ceilings.
- **The SpinWait sleep cascade** (found by the bench at 0.1×): waiting
  workers' `SpinWait.SpinOnce` crosses its internal threshold during
  the ~50–100 µs the caller spends on its own partition and calls
  `Sleep(1)` — every round paid one or two scheduler ticks (bimodal
  1.1/2.2 ms per call). The hot wait is now raw `Thread.SpinWait(64)`
  with a periodic `Yield` (never sleeps), and workers park on a
  Monitor after a 5 ms idle grace — consecutive bulk calls stay
  microsecond-scale, idle cores are not burned.
- FMA grep clean; no production comments.

## Provenance

`main@856f706` + this atom.

Bus note: `/home/i/GitHub/agent-bus` does not exist on this host
(`/home/os`), so the pre/post-atom bus checks could not run; recorded
here per AGENTS.md discipline.
