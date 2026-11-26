# Issue 0035 — Atom 30: cross-host / cross-ISA determinism proof (baked digest + CI matrix)

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Filed as GitHub issue #35 (pending — see receipts). Labels: `ai`, `area:ci`,
`kind:test`.

## Problem

Clauses 1, 2 and 16 exist so that x64 and ARM, and every vector width, produce
bit-identical results — but nothing proved it. Width-invariance tests compare paths
inside one process on one machine; the north-star leg runs on whatever box runs it.

## Shape

- `CrossHostDigestTests`: a self-contained scenario (own xorshift, no `System.Random`)
  over 150 frames — acquire/release churn, add/remove modifiers of all three kinds on
  three stats, `Recalculate`, per-row-delta intrinsics under a two-sided `Clamp.Stat`
  (NaN-free but with negative/fractional bounds through the clause-16 conversion),
  masked `RunEnabledChunk` over random words under `Clamp.StatMax`, `EmitFromNonZero`
  on a second writer, swap + drain. Every frame mixes events, live words, every stat
  output and every intrinsic of every live slot into one 64-bit digest, baked as a
  constant.
- `.github/workflows/determinism.yml`: the full suite (default + `SV_CHECKED`) on
  ubuntu x64 (default, `DOTNET_EnableAVX2=0`, `DOTNET_EnableHWIntrinsic=0`),
  ubuntu-24.04-arm (AdvSIMD, `fcvtzs`), macOS (Apple arm64) and Windows x64. The
  baked constant makes each leg an independent proof — no cross-job compare needed.

## Receipts

- Local (Ryzen x64): digest `43C10FAD8972E593` with AVX2 (masked run-walk path),
  with `DOTNET_EnableAVX2=0` (128-bit chunk path, scalar partial-word walk) and with
  `DOTNET_EnableHWIntrinsic=0` (all scalar) — identical.
- Full suite green under both forced ISA levels: 153 + 21 + 6 each.
- ARM / macOS / Windows legs run on the next push (CI); not executable on this box.
- Tests default: 153 + 21 + 6 passed, 0 failed. SV_CHECKED: 155 + 21 + 6 passed.
- no-FMA grep clean.
