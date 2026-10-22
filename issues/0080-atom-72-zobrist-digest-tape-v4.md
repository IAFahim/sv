# Issue 0080 — Atom 72 (roadmap 5.2): Zobrist board digest + page partial sums, tape v4 (inputs + digest, bodies on demand)

Roadmap [issues/0058](issues/0058-roadmap-1m-entities-5us.md) phase 5, atom 5.2 —
L13. Labels: `ai`, `area:core`, `kind:feat`. Contract: none (tape format is
sv-internal, version-bumped 3 → 4).

## Changes

- **Board digest** (`src/Sv/Board.Stats.cs`): `Digest` = Σ
  `DigestKey(stat, slot, bits(Computed(set)))` over every live resolved
  (slot, stat) pair, where `DigestKey` mixes the three inputs through the
  64-bit murmur finalizer. The digest is defined over set assignments, not
  record contents — it inherits the set table's determinism guarantees and
  stays immune to host scribbles on bound columns.
- **Incremental maintenance**: `DigestMove(slot, stat, oldSet, newSet)` /
  `DigestKeyAdd` update `digest` and per-`(stat, 256-slot page)` partial sums
  at every block transition — the `Recalculate` set-change path, `ClearSlotStats`,
  and `Spawn`. Frame cost is O(Δ): unchanged slots touch nothing. The fold is
  hoisted above the drop in `Recalculate` so a freshly resolved set's computed
  value is read before `DropBlockRef` can free the old sets.
- **Deferred bulk spawns**: `Spawn` with `(long)count * statCount > 65536`
  sets `digestPagesDirty` instead of hashing 16M keys; `Digest` and
  `FirstDivergentPage` call `EnsureDigestCurrent()` → `RecomputeDigest()`
  (full walk over live words). This keeps the spawn1m receipt — digest-at-spawn
  measured 29 ms before the defer, 3.8 ms after.
- **`RecomputeDigest()`**: from-scratch walk, also the anchor after
  `LoadState` (restore) and `RenumberStats` (page indexing changes) — the
  incremental sums never survive those paths.
- **Page localization**: `FirstDivergentPage(other)` returns the flat
  `stat * PageCount + page` of the first differing page sum.
- **Tape v4** (`src/Sv.Compose/Tape.cs`): each frame is dt bits + axes count +
  word count + captured inputs + `FrameDigest` (XXH64 over dt bits, input bits,
  `board.Digest`). No bodies — `WriteBody`/`BodyLength` retained for on-demand
  divergence localization (`tape-digest` bench and RollbackTests still use
  them). `TapeWriter` drops its persistent body buffer; recording is now
  stackalloc-only per frame.
- **Playback v4** (`src/Sv.Compose/Compose.cs`): version check rejects v3
  tapes; per-frame truncation guards on header, inputs, and digest; on digest
  mismatch the exception carries recorded vs replayed digests plus a drift
  hint — if `RecomputeDigest()` (from scratch) also mismatches the recorded
  digest, the incremental digest drifted; if it matches, the boards genuinely
  diverged.
- `Sv.Compose` grants `InternalsVisibleTo Sv.Bench` (the new
  `tape-record` scenario drives `CaptureFrame` directly).

## Tests (`DigestTests`, 4 facts; total 322)

- `IncrementalDigest_MatchesFromScratch_UnderChurn` — the digest law
  (`Digest` == `RecomputeDigest()`) checked after every `Recalculate` across
  add/expire churn, a 3-slot spawn, a release, and save/load.
- `BulkSpawnDigest_IsDeferredThenCorrect` — 40,000-slot spawn at capacity
  65,536 crosses the defer threshold; the first `Digest` read finalizes and
  still satisfies the law; spawned column values verified through a bound
  output column (the spawn contract — spawn assigns the template block; it
  writes no modifier records, so `Stat()` stays at base).
- `IdenticalPrograms_DigestEqual` — two boards running the same program digest
  equal; one modifier diverges them.
- `PageDigests_LocalizeTheChangedSlot` — one slot's modifier on stat `resolve`
  makes `FirstDivergentPage` return exactly `resolve * PageCount + (slot >> 8)`
  in both directions.

All 318 pre-existing facts green unmodified.

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **322/322 passed** (318 + 4 new).
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 322/322.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27 (both modes).
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok;
  **tape-record 0.034 µs/frame (receipt ≤ 0.1 µs)**, spawn1m 3.8 ms ≤ 10 ms,
  ff1m p50 2,917 µs (Δ=128, board 151 MiB, snapshot 152 MiB) — unchanged from
  atom 71, digest maintenance costs nothing when Δ is small.
- FMA grep clean; no production comments.

## Provenance

`main@5dacb73` + this atom. Next: 5.3 — undo-log rollback (reclamation
deferred by horizon; 8-frame rollback + resim ≤ 50 µs at Δ=128, L12).
