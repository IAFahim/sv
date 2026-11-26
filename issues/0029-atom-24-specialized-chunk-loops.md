# Issue 0029 — Atom 24: specialized chunk loops (clamp kind x delta mode x applied)

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Filed as GitHub issue #29 (pending — see receipts). Labels: `ai`, `area:simd`,
`kind:perf`.

## Problem

`Simd.ChunkW128`/`ChunkW256` switched on `clamp.Kind`, tested `MinColumn`/`MaxColumn`
nulls, `delta == null` and `applied != null` inside the per-vector loop body. All are
loop-invariant; the JIT does not unswitch them.

## Shape

- `Simd.Chunk.cs`: struct strategies `IBound` (None, Constant, Columns, ColumnMin,
  ColumnMax, Stats, StatMin, StatMax), `IDelta` (Uniform, Column), `IApplied` (None,
  Column), and a width tag (`Width128`/`Width256`). One dispatch picks the
  instantiation; the JIT emits a branch-free loop per combination.
- Plain instance interfaces with `struct` constraints — no `static abstract`, so the
  core stays C# 9 / Unity portable.
- Clamp math unchanged (same `ClampBoth`/`MinOnly`/`MaxOnly`/`BoundsOf`, same scalar
  tail via `Clamp.Apply`) — bit-identical, width-invariance tests unchanged and green.
- Rejected: trimming the `BoundsOf` negative-overflow select (relies on x86
  `cvttps2dq` = MinValue and ARM `fcvtzs` saturation — true, but not an API guarantee):
  only ~5% on statclamp, not worth weakening clause 16's explicit pinning.
- Not done here (contract change): an int shadow of stat-bound columns computed at
  broadcast. Tests and hosts may write the bound float column directly, and clause 10
  says `Clamp.Stat` resolves to that column — proposed on the bus instead.

## Receipts

- Bench (same machine as 0027): intrinsics-statclamp 31.4 -> 25.5 us (-19%);
  intrinsics-varying 19.4 -> 14.7 us (-24%); uniform 16.2 -> 16.8 (noise).
- Tests default: 144 + 16 + 6 passed, 0 failed. SV_CHECKED: 146 + 16 + 6 passed.
- no-FMA grep clean.
