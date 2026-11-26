# Issue 0095 — Atom 87 (bench): wheel-expiry microbench scenario — the issue 0091 pickup

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Labels: `ai`, `area:core`, `kind:feat`. Contract: none.
Roadmap: issue 0091's deferred receipt ("the dedicated expiry microbench scenario
is deferred to the next session (pickup point)").

## Changes

- **`benchmarks/Sv.Bench/WheelExpiryBench.cs`** (new) plus one wiring line in
  `Program.cs`. Steady state holds ~262k pending expiries — the ff1m shape from
  issue 0091 — at 2^20 entities: every tick adds 1,024 modifiers with delays
  uniform in [1, 512], so both wheel levels stay populated (delays ≤ 256 insert
  into L1, 257–512 into L2) and every L1 wrap cascades a full L2 bucket.
  A tick is `ExpireModifiers` + 1,024 `AddModifier` + `Recalculate` — step 3 and
  step 2 of the composed frame, in order.
- **Verify pins O(due), not just throughput**: a 1,024-slot ring histogram
  predicts the *exact* due count per tick (delays ≤ 512 < ring size, so no
  aliasing), and integer add/remove accounting predicts the *exact* pending
  count. Both are checked every verify; any wheel entry lost, double-fired, or
  left behind fails the scenario.
- Gates: Expire p50 ≤ 500 µs, whole-tick p50 ≤ 1500 µs, alloc 0 B (both
  enforced by the harness and the scenario). A regression to O(pending) — a
  per-tick scan of 262k entries — cannot pass the Expire gate.

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **384/384 passed** (no new tests;
  this atom is bench-only; 2× repeat stable).
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 384/384,
  0 warnings.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27.
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok.
  **wheel-expiry 92.5–98.3 µs Expire, 290–293 µs whole tick, 262,414–262,998
  pending, 1,024 due/tick** (two full runs; steady-state pending sits at
  Due × mean delay = 1,024 × 256.5 as designed). The interim ff1m evidence
  (8 due/frame at unchanged p50) now has its dedicated scenario: expiry cost
  is bounded by the due count, not the 262k pending.
- FMA grep clean; no production code touched (`src/` diff is empty);
  `contract/` and `StrictFloatOracle.cs` untouched; digest pins unchanged.

## Provenance

`main@f904a78` + this atom. Bus checked before (`inbox empty`) and after.
