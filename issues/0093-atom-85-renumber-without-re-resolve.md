# Issue 0093 — Atom 85 (roadmap 3.3): renumber without re-resolve — reconcile costs O(moved rows), no spike

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) phase 3, atom 3.3
(F16). Labels: `ai`, `area:core`, `kind:feat`. Contract: none — the clause-5
substance (mask windows at any offset) predates this atom at the board level
(issue 0090, atom 79); the seam-side slice is deferred, below.

## Changes

- **`RenumberSlots` stops dirtying the world** (`src/Sv/Board.Stats.cs`): the
  `ClearDirty` + mark-every-live-slot sweep is deleted. Pending dirty rows
  are **remapped through the map** (`RemapDirty`: copy out every touched
  slot's stat-mask row, clear the sources, re-place at the targets — targets
  are distinct by the existing bijection check, so no aliasing). Clean pairs
  stay clean: set ids are slot-independent and modifier records are
  pool-global, so a resolved (slot, stat) pair that moves with its slot
  needs nothing. The next `Recalculate` resolves only what was already
  pending — the no-spike half of the receipt.
- **`RenumberStats` becomes O(moved + dead) instead of O(capacity ×
  stats)**:
  - Only slots whose target differs are processed (`moved` pairs); identity
    maps early out without touching a single page.
  - Slot pages are **copy-on-write**: a page is copied only when a moved row
    enters or leaves it; untouched pages are shared between the old and new
    page arrays, and only copied pages are freed.
  - After all moves, the slots **not in the image** (`~newLive`) are reset to
    `{Block=-1, Head=-1}` — this, not a per-move source clear, is what keeps
    a moved slot's old index clean: clearing inline would wipe a destination
    written earlier in the same permutation (the swap X↔Y case — half of
    every swapped pair lost its record chain; caught by the oracle suites).
  - The **digest is maintained incrementally per moved slot** (old key out at
    `pageDigests[stat, slot>>8]`, new key in at `[stat, next>>8]`) instead of
    a full `RecomputeDigest` walk — `EnsureDigestCurrent` flushes any
    deferred bulk-spawn digest first. Double-applying a permutation restores
    the original digest bit-exactly (pinned by test).
  - Owned columns and mirrors move only the moved rows over a copy (dead
    targets zeroed), not clear-then-rewrite-capacity.
  - The wheel needs no rebuild: it holds pool-global record indices, and
    `RenumberSlots` invalidates the undo window before any of this (both
    pre-existing).
- **`renumber1m` bench scenario** (`benchmarks/Sv.Bench/Renumber1mBench.cs`):
  2^20 entities, 16 stats, template-spawned to full, 1024 holes churned, then
  per iteration: 64 add/remove pairs leave pending dirty, a deterministic map
  swaps 4096 slot pairs (8192 moved rows), `RenumberSlots` + `Recalculate`,
  each phase timed. Verification compares both tracked columns **after** the
  renumbering frames against expectation arrays permuted through the same
  map (digests are not comparable across permutations — keys embed slot
  indices). Gates: renumber p50 ≤ 20 ms, post-renumber Recalculate p50
  ≤ 1 ms, alloc 0 B.
- **Deferred slice, deliberately**: the S6 64-slot padding drop (the seam —
  `src/Sv.Frent/Population.cs:72` pads every archetype to 64 and
  `ChunkSeam` requires 64-aligned enabled windows) is the clause-5 wording
  item queued in the v1.2 batch (bus 0043; sv's clause-7 item of the same
  batch is bus message 0051). It lands as its own atom after the batch folds.

## Tests (total 381)

- `RenumberSlots_PendingDirty_MatchesTwinThatNeverRenumbered` — pending
  dirty state survives a compacting renumber: values equal a twin board that
  applied the same edits without renumbering.
- `RenumberSlots_IdentityMap_PreservesPendingDirty` — identity map: digest
  unchanged, pending dirty still resolves on the next Recalculate.
- `RenumberSlots_RoundTrip_RestoresValuesAndDigest` — a permutation applied
  twice restores values and the digest bit-exactly (pins the incremental
  digest arithmetic).
- All 378 pre-existing facts green unmodified — including the churn-renumber
  oracle suites, which caught the inline source-clear bug above.

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **381/381 passed** (378 + 3);
  3× repeat stable.
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 381/381.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27.
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok:
  **renumber1m RenumberSlots p50 ≈ 5.2 ms at 1,048,576 rows with 8,192
  moved/frame (receipt ≤ 20 ms), post-renumber Recalculate p50 ≈ 9 µs (the
  no-spike receipt — the pre-atom behavior re-resolved every live pair,
  ~1 s)**; ff1m 2,974 µs p50, keyframe 293 µs, rollback-resim 349 µs,
  bulkpass 1.23×, spawn1m 4.8 ms unchanged; alloc 0 B everywhere.
- FMA grep clean; no production comments; `contract/` and
  `StrictFloatOracle.cs` untouched.

## Provenance

`main@5e92d3f` + this atom. Built by the builder agent through the
implementation (design: copy-on-write pages, incremental digest, RemapDirty,
the bench), finished by the lead (the source-clear/image-reset fix, the
bench's post-frame verification, receipts) after the builder hit a usage
limit. Next: the hardening atom from the 77–83 audit, then the seam slice of
3.3 when the v1.2 batch folds.
