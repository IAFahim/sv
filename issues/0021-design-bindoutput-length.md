# Issue 0021 — Design: `BindOutput` column length is unvalidated (implemented by atom 19, [issues/0024](0024-atom-19-bind-column-length.md))

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Filed as GitHub issue #21 (open). Labels: `ai`, `area:core`, `kind:feat`.
Contract touchpoint: clause 12 (pointer stability) — any signature change is a bus
message first; the bus tree is absent on the working machine, so this stays a spec.

## Problem

`BindOutput(StatId, float*, stride)` takes no column length. `Recalculate` writes every
*live* slot through the bound pointer, and `Board.Create` silently rounds capacity up to
64 — so binding a 4-float column "works" until live count exceeds 4, then it is a native
out-of-bounds write. `SV_CHECKED` catches stale generations, never short columns. The
sv-owned columns (issue 0012) remove the trap for hosts that can use them; the external
bind path keeps it.

## Proposed shape (for the bus)

- Add `int columnLength = -1` (or a checked-only twin): `-1` preserves today's
  semantics verbatim; any positive length is checked against live-slot writes in
  `Recalculate` under `SV_CHECKED` only — zero release cost, no behavior change for
  existing callers.
- Alternative worth considering: forbid external binds on rounded capacity and steer
  hosts to sv-owned columns + `BindExternal` handles.

Held open until the bus merge point is reachable again; no implementation before ack.

## Receipt

- 2026-10-02: bus message 0032 sent to integrator (Reply-To 0029, rides the v1.0.4
  thread). Recommendation on top: optional `int columnLength = -1`, SV_CHECKED-only.
  Ruling awaited.
- 2026-10-04: implemented as atom 19 ([issues/0024](0024-atom-19-bind-column-length.md))
  under the user's in-session direction (bus message 0035); the recommended shape
  shipped verbatim — optional `columnLength: -1`, checked-build-only enforcement.
