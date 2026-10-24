# Issue 0103 — Atom 94 (roadmap 3.3): enabled windows at any offset — the 64-slot padding is retired

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) 3.3 (seam half), fixes S6.
Labels: `ai`, `area:core`, `kind:feat`. Contract: v1.2 clause-5 wording ("windows
at any offset"), drafted in `issues/0097`, implemented ahead of the fold per
owner instruction (bus 0053). The F16 half of 3.3 (renumber without
re-resolve) already landed — `RenumberSlots` remaps only pending dirty state
and the renumber1m receipt shows no next-frame spike — so this atom is the
seam.

## Changes

- **The alignment requirement is gone from the whole masked surface.** A
  window's words are anchored at its own `start` — bit i of word w is row
  `start + w*64 + i` at *any* `start` — and the atom-93 `validBits` bound
  already pins the tail word to the caller's real row count. Nothing in the
  kernel needs a 64-aligned base, so the adapters stop demanding one:
  - `ChunkSeam.RunEnabledChunk` (both overloads) and
    `Population.ApplyEnabled` accept any offset inside the chunk; the
    multiword paths compose full words + the tail-bounded single word.
  - `SvChunk.RunIntrinsics` (Unity) always takes the direct two-word window
    anchored at the chunk's slot base — any alignment, `wordCount` derived
    from `chunk.Count`, tail bounded. The per-batch `ChunkEntityEnumerator`
    fallback is deleted (the misaligned case it existed for is now the direct
    path); non-contiguous chunks still throw — reconcile first.
- **Both reconciles pack contiguously** — the `(next + 63) & ~63` padding is
  deleted from `Sv.Frent.Population.Reconcile` and
  `UnityPopulation.Reconcile`. Up to 63 slots × archetypes of board capacity
  are no longer reserved for alignment (S6); Unity's 128-entity chunks map
  without alignment games.
- **Fixed, pre-existing bench OOB**: `EnabledRunwalkBench` walked
  `offset < Rows` over a 100,000-row buffer — 100,000 is not a word multiple,
  so the last window (offset 99,968) touched 32 rows past the value, applied
  and reference buffers in Verify and Frame alike. All three loops now stop at
  `offset + 64 <= Rows` (rows past the last whole word are identically
  untouched by every path, so the scenario's arithmetic is unchanged).

## Tests (Sv.Tests total 410; Unity facade 7; NorthStar 27)

- `EnabledWindow_UnalignedOffset_FastPath` (new, Frent): a 135-row chunk,
  window at offset **5** — three words, full + `0x0F0F…` pattern + a 2-row
  tail — values and applied verified row by row on the fast path
  (`FastPathChunks == 1`).
- `Reconcile_RenumbersPerArchetype_Packed` (was `…_WithAlignment`): two
  40-row archetypes pack to blocks {0, 40} with zero holes below the top of
  the last block (order-agnostic — equal sizes, Frent's archetype enumeration
  order is registration-dependent).
- `EnabledMask_PartialChunk_TailBounded_MatchesOracle` (new, Unity facade): a
  70-row chunk through the `v128` mask — full word + 6-row tail, disabled
  tail rows untouched, applied exact.
- `EnabledMask_MisalignedChunk_BatchPath` renamed `…_DirectPath` — the batch
  fallback no longer exists; same expectations.
- The alignment-throw guard test now passes for the reason that remains
  (offset 1 with 1 word fails the exact-word-count check, not alignment).

## Receipts

Measured on the bus host (decision 0048): i9-14900K, 24C/32T, .NET 10 Release.

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors; SV_CHECKED build
  + full retest green (410 Sv.Tests, 7 Unity facade, 27 NorthStar; 2× repeat
  stable).
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok.
  Roadmap receipt **"reconcile at FF-1M ≤ 20 ms, no spike on the next
  frame"**: `renumber1m` **5,421 µs** RenumberSlots (1,048,576 rows, 8,192
  moved) + **9.8 µs** next-frame Recalculate — under budget by 3.7×, no
  spike. The Frent/Unity world-walk half of reconcile is host-side and not
  part of this bench.
- `enabled-runwalk` 2,283 M rows/s (within run noise of atom 93's 2,339; the
  loop fix removes one of 1,563 windows).
- S6 memory: at FF-1M's 16-archetype shape, up to ~1,008 slot-slots of board
  capacity per reconcile are no longer reserved (padding lived inside
  capacity, so steady-state bytes are unchanged — the receipt is the hole
  elimination, not a byte delta; recorded honestly).
- FMA grep clean; no production comments; `contract/` and
  `StrictFloatOracle.cs` untouched.

## Provenance

`main@e86097e` + this atom. Bus checked before and after (inbox empty).
