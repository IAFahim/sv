# Issue 0043 — Atom 38: state save/load — deterministic rollback (`SvState`)

Filed as GitHub issue #43 (pending — see receipts). Labels: `ai`, `area:core`,
`area:compose`, `kind:feat`, `contract:proposed`. Contract: bus 0036 amendment 5
(new clause) — vendored contract stays v1.0.3 until ack.

## Problem

sv already proves whole-run determinism (tape). The next step games need is
rollback (GGPO-style netcode, undo, speculative simulation): snapshot a frame,
continue, rewind, resimulate. All sv state is native and owned, so it can be saved
exactly — but there was no API.

## Shape

- `SvState`: a caller-owned, reusable native buffer; grows, never shrinks; warm
  save/load allocate no managed bytes. `Length`/`Bytes` expose it (e.g. to ship over
  a network).
- `Board.SaveState/LoadState`: slot space (live count, free list, live words);
  insertion-id counter; dirty queue; changed masks + touched list; sv-owned output
  columns; mirrors (presence + content); expiry heap; every touched page (metadata;
  modifier blocks with exact counts, capacities, entries, ids, sources); the full set
  table (buckets, per-set arrays, member arrays, content arena, dirty queue) — set
  identities survive, so post-load behavior is bit-identical, not just value-equal.
  Bindings are not state (host columns are the host's to save).
- `InputState.SaveState/LoadState` (axes, buttons, edges, ring, cursors) and
  `EventStreams.SaveState/LoadState` (both double-buffer halves, all cursors; channels
  are cold setup and must match).
- `Game.SaveState/LoadState` (Sv.Compose): board + input + streams + timeline ids and
  clocks + applied + every tl effect column in one buffer.
- Shape headers (magic + capacity/schema/writer shape) reject a state saved from a
  different shape with `InvalidDataException`; trailing bytes are rejected too.

## Receipts

- `StateRollbackTests`: 40 frames of churn (acquire/release, timed+sourced
  modifiers, RemoveSource, expiry, derived stat, mirror), save, 60 more frames of
  per-frame digests (outputs of every row, mirrors, changed masks, live words,
  pending expiries, set count) — loading into the same board **and into a fresh
  board with a different 25-frame history** replays all 60 digests exactly.
  Mutation check: skipping the column restore fails it ("frame 40 diverged").
  Shape mismatch rejected; 200 warm save+load pairs allocate 0 B; event streams
  round-trip mid-frame (pending writes + partly drained published frame) over two
  swaps; input round-trips edges, ring and tick.
- `RollbackTests` (Sv.Compose): a composed host (events -> timed modifiers ->
  expiry -> stat-clamped intrinsics -> struct-maker emits) saved at frame 12;
  frames 13-30 tape digests (+ host mana) reproduced exactly after two separate
  rollbacks.
- Cost (scratch harness): 100k x 3 stats with 25k timed modifiers -> 9.2 MiB state,
  save 1.13 ms, load 0.93 ms.
- Tests default: 177 + 22 + 6 passed, 0 failed. SV_CHECKED: 179 + 22 + 6 passed.
- no-FMA grep clean.
