# Issue 0024 — Atom 19: checked-only `BindOutput`/`BindExternal` column length (implements 0021)

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Filed as GitHub issue #24 (pending — see receipts). Labels: `ai`, `area:core`,
`kind:feat`.

## Problem

Implements the recommended shape of [issues/0021](0021-design-bindoutput-length.md):
`BindOutput`/`BindExternal` took no column length, so binding a column shorter
than the live count is a native out-of-bounds write; `SV_CHECKED` caught stale
generations, never short columns.

## Shape

- `BindOutput(StatId, float*, int stride = 1, int columnLength = -1)` and the
  `BindExternal` twin: `-1` preserves today's semantics verbatim; any length
  >= 0 is validated against `slot * stride` on every broadcast write in
  `SV_CHECKED` builds only (`Guard.BoundColumnFit`, `[Conditional]` — zero
  release cost, per clause-2-adjacent guard idiom).
- Validation of the parameter itself (`< -1` rejected) runs in both modes.
- Scope: the broadcast write path in `Recalculate`. Clamp-bound reads keep the
  clause-12 pointer doctrine (issue 0021's write path was the corruption vector).

## Receipts

- 2026-10-04: user directed in-session to implement the pending 0021/0022
  recommendations (bus message 0035 records the ruling).
- Build: 0 warnings, 0 errors (Release).
- Tests default: 133 + 16 + 6 passed, 0 failed. SV_CHECKED: 135 + 16 + 6
  passed, 0 failed (`BoundColumnLengthTests`, 7 facts, dual-mode).
- no-FMA grep clean.
