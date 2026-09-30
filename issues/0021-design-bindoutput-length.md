# Issue 0021 — Design: `BindOutput` column length is unvalidated (open — pending bus)

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
