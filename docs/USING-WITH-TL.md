# Using sv with tl

sv holds the numbers a game simulates (stats, intrinsics such as hp) and tl plays the timelines that
change them. Neither library references the other: the glue owns two tables and calls both in a fixed
frame order. A complete, tested example is `tests/Sv.NorthStar2` (tl 4.0, .NET 10).

## 1. Columns and links

Every column is listed once as a `ColumnDef(nameHash, role, schemaId, consumer)` and every use by a kind
once as a `Link(kind, column, mode, scope)`. `Links.Validate` returns the first `LinkFault`;
`Links.Hash` is part of state identity (pin it beside `Schema.ContentHash`).

| Role | Writer | Example |
|---|---|---|
| StatOutput | sv (Recalculate -> sink or bound column) | max_hp, armor |
| Intrinsic | sv fold only | hp |
| Delta | tl kinds (`+=`), consumed and zeroed by the fold | `[TlState] HpDelta { int Value; }` |
| Inbox | any worker, atomic 64-bit add, consumed by the same fold | `InboxBuffer` |
| Effect / Signal | tl kind, row-local, consumed once | `Outgoing`, pulse columns |
| Condition | sv fold (hysteresis bits) | hp below 30 %, re-arm at 40 % |
| Sense | an external module, integer, frozen like stats | grid fields |

On tl's side a column is a plain `[TlState]` struct; a kind is a `[TlKind]` struct with `Combine` and
`Apply`; a track feeds it with `[Feeds]` (every tick) or `[Pulse]` (clip Start/End).

## 2. What a kind does per mode

| Mode | The kind | sv's part |
|---|---|---|
| R read | loads a store column | stores were written before the step and are frozen in it |
| K check | uses a condition bit or compares a frozen value | fold computes the bits |
| A accumulate | `+=` into a row-local effect column | its single consumer clears it |
| D delta | `+=` into a delta column (`Apply(in int total, ref HpDelta d) => d.Value += total`) | the next fold sums, clamps once, zeroes |
| D remote | tl kinds cannot address other rows: write the amount to a row-local int effect, then an sv send pass calls `Inbox.Send(inbox, target, amount)` and clears it | atomic, order-free |
| M modifier | pulse at clip Start/End into a pulse column | turned into `StatOp.Add` / `StatOp.RemoveSource` (source `0x40000000 | timeline << 8 | slot`) for the next frame |
| E event | nothing (edge bits are the signal) or a signal column | `EventDerive` emits events after the jobs |

## 3. Frame order (contract v2.1.0 clause 7)

1. Commands: journal -> `StatOp`s, inbox sends (every external integer write enters the inbox).
2. Expiry: `Board.ExpireModifiers(tick)`.
3. Apply: `StatOps.Canonicalize` + `Board.Apply` (pulse StatOps of the previous frame included).
4. `Board.Recalculate`.
5. Scatter: `TakeBroadcast` into the host's stat columns (`BindSink(stat, true)` once at setup fills
   existing rows; the .NET alternative is `BindOutput`). Stores are frozen from here.
6. Fold: `Kernel.Fold(value, window, delta, bounds, changed, inbox)` per worker partition, then
   condition bits and edge counts (`EventDerive.AddCount`).
7. tl: `world.Tick(players)` and the generated `TlAuto_<Assembly>.Run(world, players, true, workers)`,
   then the send pass.
8. Derive: `EventDerive.Derive(...)` from edge words; pulses -> StatOps for t+1.
9. `EventStreams.Swap`, digest, tape (the tape stores causes: the journal; replay rebuilds the rest).

Deltas written in step 7 of frame t are folded in step 6 of frame t+1 (one frame lag).

## 4. Fold semantics: sum, then clamp once

`after = clamp(before + saturate32(delta + inbox), min(floor, ceiling), ceiling)`; delta and inbox become
zero. All contributions of a frame are summed first. Heal +10 and hit -20 on hp 95/100 give **85**
(sequential clamping would give 80). Order-dependent rules (shield first, then hp) stay on the sparse,
ordered path (`ClampShift.Then`). The result depends only on the multiset of contributions, so worker
count and arrival order cannot change a bit (NorthStar2: 1, 2, 4, 7, 11 workers and 100 shuffles equal).

**Floats never cross rows.** Remote writes are integers into the inbox; float accumulation is row-local.
A stat used as a bound converts through `Clamp.BoundOf`, never `(int)`.

## 5. Events: cap and Dense

A channel has a build-time capacity, default **16,384** (65,536 tripped the plan's 0.5 ms sort kill rule:
0.50-0.68 ms; 16,384 sorts in 0.08-0.11 ms). If a frame's edges are at most the capacity, every event is
delivered in ascending slot order. Above it, exactly one `DenseEvent {Channel, Tick, Count}` is delivered
and no per-row events; the edge words stay readable until the next fold, so every handler must also
accept the bit words (mask form). A 1M-edge death wave costs ~0.01 ms and 0 B.

## 6. Limits found (2026-11-12)

- A modifier added and removed (same slot, source) in one batch stays: remove sorts before add (re-trigger
  refresh). Remove a clip modifier in a later frame than its add.
- Before issue 0122 the set table never reused freed sets under steady churn (~70 B per change, unbounded);
  fixed, memory now flat.
- No public `Bounds` over a host float stat column: a sink host keeps an int bound column filled with
  `Clamp.BoundOf` at scatter.
- tl kinds cannot write another row; use the effect column + send pass of section 2.
- Frame rollback over dense columns is off by default; `Rollback` refills a bound sink from the restored board.
- frame1m numbers (issue 0125) use an sv stand-in for the kinds, not tl.
