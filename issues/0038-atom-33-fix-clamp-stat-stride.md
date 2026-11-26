# Issue 0038 — Atom 33: fix — `Clamp.Stat` ignored the bound column's stride

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Filed as GitHub issue #38 (pending — see receipts). Labels: `ai`, `area:core`,
`kind:bug`.

## Problem

`BindOutput(stat, column, stride)` supports interleaved (AoS) external columns and
the broadcast honors the stride. `Clamp.Stat/StatMin/StatMax` resolve to the same
pointer but read it as a contiguous column (`MinStatColumn[slot]`, vector loads at
`+ slot`) — with stride 2 they read the neighbouring field's data as bounds. Silent
wrong clamps since atom 2. Found by atom 34's mirror tests (the mirror, written by
the stride-aware broadcast, disagreed with `Clamp.Stat`).

## Shape

- Construction rejects a strided external bound column with `ArgumentException`.
  A strided bound cannot be read without gathers (clause 4); the stride-correct
  route is the stat-bound mirror (atom 34, bus 0036 amendment 2) or a stride-1
  binding. Construction is cold, so the check is unconditional (not checked-only).
- No in-repo or sv-examples host binds bounds with stride > 1 (grep).

## Receipts

- New `ClampStrideTests`: all three stat clamps throw on stride-2 bindings and
  construct after rebinding stride 1.
- Tests default: 159 + 21 + 6 passed, 0 failed. SV_CHECKED: 161 + 21 + 6 passed.
- no-FMA grep clean.
