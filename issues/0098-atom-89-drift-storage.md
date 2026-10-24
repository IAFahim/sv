# Issue 0098 — Atom 89 (roadmap 4.2): drift storage — lazy intrinsic segments `(v₀, t₀, rule)`

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) §4.2, law L7.
Labels: `ai`, `area:core`, `kind:feat`. Contract: none — pure addition; the
drift verbs that drive it are the v2.0 batch (owner decision 0046 accepted the
fold for v2.0; §8 sends that batch once Phase 4 receipts exist, the atom-83/0051
precedent). Unblocked by owner decisions 0044 (monotone clamp — drift requires
floor ≤ ceiling, guaranteed structurally by `ClampShift.Step`) and 0048
(receipts measured on the bus host, named below).

## Changes

- **`src/Sv/Drift.cs` — `DriftStore`** (new): one 32-byte segment per slot —
  the frozen `ClampShift` stored by value (blittable, no reconstruction — its
  constructor is private by 0078's law), `t₀`, `v₀`, active flag — in one
  zeroed `NativeMemory` block, fixed capacity, idempotent `Dispose`.
  - `Capture(slot, value, tick, rule)` — establish/replace a segment.
  - `Read(slot, tick)` — `rule.Repeat(tick − t₀).Apply(v₀)`: n frames of regen
    are one closed-form evaluation. Ticks ≤ t₀ read v₀.
  - `Write(slot, value, tick)` — the §4.1 step-2 convention: the write lands
    before that frame's drift step (segment becomes `(value, tick − 1)`); a
    later write in the same frame replaces it (last wins, matching eager).
  - `Rebase(slot, tick, rule)` — the §4.1 step-3 convention: v₀ folds
    `Repeat(tick − t₀)` (everything up to and including frame tick), then the
    rule swaps. Same-tick rebase swaps the rule only.
  - `Materialize(window, destination, tick)` — bulk read over the atom-79
    window vocabulary; range windows write every row, masked windows write
    enabled rows only (disabled rows untouched — clause 5 discipline), both
    guarded by the existing `Guard.ChunkRange`/`EnabledRange`.
  - Missing-segment access, writes at or before the segment start, rebase
    before the segment start, out-of-range slots, and use after dispose all
    throw; rules can only enter through `ClampShift.Step` or composition, so
    floor ≤ ceiling holds everywhere (crossed bounds are a v1.2-batch/eager-side
    matter — decision 0044 — and are irrelevant here because `ClampShift`
    rejects them at construction).
- Nothing drives it from the frame pipeline yet — 4.3 (board wiring: rebase on
  the changed mask, bound changes, renumber, rollback) is the next atom in the
  arc; this atom lands the algebra-correct store with the §4.1 segment
  convention pinned, per the owner's sanction of the drift arc (0044/0046/0048
  answered; "do the drift work these answers unblock").

## Tests (total 395)

- `KernelEager_MatchesDrift_AtEveryTick` — **the L7 anchor to production**: 64
  slots, per-row deltas, the real `Kernel.Run` with `Bounds.Constant` advances
  eagerly for 200 ticks while `Read` stays bit-equal at every tick.
- `WritesAndRebases_MatchEagerPerFrame` — 400 frames × 8 slots, random
  writes (step 2) and rule changes (step 3) mirrored eagerly; equality asserted
  every frame; slot 3 pinned to `Step(2, 0, 10)` settles at 10 through the
  whole 400-frame run.
- `Write_ComposesBeforeTheFrameDriftStep` / `Rebase_FoldsSettledHistory_AndSwapsRule`
  — the convention exactly: write-then-read at the same tick, settled history
  folding at rebase, same-tick rebase swapping only the rule, and the tick
  ordering rejections.
- `Materialize_RangeAndMasked_MatchRead` — 320 slots with varied t₀/rules;
  range materialization equals per-slot reads; masked windows write enabled
  rows only (sentinel survives elsewhere).
- `MissingSegment_Throws`, `Dispose_IsIdempotent_AndGuardsUse` — the guards.

## Receipts

Measured on the bus host per owner decision 0048: **i9-14900K, 24C/32T,
Arch Linux, .NET 10 Release** (cold-core p50 is the Phase-4-exit gate; this
atom adds no frame-path work, so there is nothing cold to measure yet — the
first cold/warm split lands with 4.3's frame wiring).

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **395/395 passed** (388 + 7 new
  `DriftTests`); 2× repeat stable.
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 395/395,
  0 warnings.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27 (eager path untouched).
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok,
  no movement (ff1m ~2.9 ms, wheel-expiry 85.5 µs Expire / 272.5 µs tick at
  262,974 pending, renumber1m 4.9 ms + 8.8 µs) — expected: `DriftStore` has no
  callers outside tests yet.
- FMA grep clean; no production comments (XML docs only); `contract/` and
  `StrictFloatOracle.cs` untouched; digest pins unchanged.

## Provenance

`main@e605c86` + this atom. Bus checked before and after; owner answers
0043–0049 relayed as bus message 0052, decisions archived from `inbox/user/`.
