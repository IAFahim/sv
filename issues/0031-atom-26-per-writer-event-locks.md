# Issue 0031 — Atom 26: per-writer padded event locks, word-batched emit

Filed as GitHub issue #31 (pending — see receipts). Labels: `ai`, `area:events`,
`kind:perf`.

## Problem

`EventStreams` has per-writer arenas so threads can write in parallel (atom 3), but
every `Append` took one **global** CAS spin lock, and all writers' `writeOffset`
ints shared one cache line. Parallel writers serialized and ping-ponged lines:
8 threads on 8 distinct writers ran 50x slower in aggregate than one thread.

## Shape

- One spin lock per writer, each on its own 64-byte line (`AlignedAlloc`), with that
  writer's write offset in the same line — no cross-writer sharing on the append path.
  Concurrent appends to the *same* writer stay safe (atom 3's promise); their
  interleave stays scheduler-dependent as documented.
- Growth happens under the writer's own lock (old separate `Grow` merged in).
- `EmitFrom*` build up to 64 events per mask word in a `stackalloc` batch (payloads
  up to 256 B; larger fall back per event) and append the batch in one lock
  acquisition. The maker runs outside any lock. Event order unchanged (bit order,
  writer-index drain).
- Not here (contract, clause 17): the single-thread emit cost is dominated by the
  `UnmanagedCallersOnly` gate (~15k events/frame at ~30 ns each); a struct-maker
  overload measured 449 -> 74 us (6x) in a throwaway prototype — proposed on the bus.

## Receipts

- Scratch harness (32-core Ryzen, 1M `Write`s per thread, one writer per thread),
  aggregate events/s old -> new: 1 thread 135 -> 155 M; 2 threads 26 -> 280 M;
  4 threads 10.5 -> 425 M; 8 threads 2.7 -> 793 M (~290x).
- New `EventsParallelTests`: 8 writers x 20k drain in writer-index then append
  order; 4 threads sharing one writer lose nothing and keep per-thread order.
- Bench `events-emit` (single thread): 466 -> 458 us (unchanged, as expected).
- Tests default: 148 + 16 + 6 passed, 0 failed. SV_CHECKED: 150 + 16 + 6 passed.
- no-FMA grep clean.
