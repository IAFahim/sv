# Issue 0153 - Anything on anyone: per-entity sets, immunity, targeted damage as data, reading and changing other entities

Status: open

Labels: `ai`, `area:unity`, `area:authoring`, `area:core`, `open`, `owner-question`.

Owner question (2026-11-12): a player or any object should be able to have health, mana, storage, double damage, immunity to
health or to anything; sets differ per entity; hitting something must route the damage to it; reading another entity's health
and changing its belongings must work.

## What the code does today (checked in src and packages/com.sv.unity)

- Stats and intrinsics are declared once in the schema (fixed at build, limits 4 dense intrinsics, 8 stat columns, stat index
  below 64). The Board holds every schema stat for every row.
- In Unity a row takes part in an intrinsic only if its archetype carries that column: the fold checks
  `chunk.Has(Value)` and `chunk.Has(Delta)`, the scatter checks `chunk.Has(handle)`. So per-entity sets work by which
  `[TlState]` columns an entity is baked with. There is no capability check ("may X have Y"): any row can carry any column.
- No immunity concept exists (grep finds none). `ModifierKind.Cap` and `Multi` exist; `Multi 0` on a stat zeroes it.
- Targeted damage: `links.Send<Outgoing, Target>(hp)` exists. The programmer writes the effect column and sets `Target`
  (a slot). Not available as designer data.
- Reading another row: a normal `[TlState]` column, read through `ComponentLookup` on the main thread or in a job (tl's
  `AnchorLookup` pattern). The value is the one from the last fold (frozen reads).
- Changing another row: modifiers with `host.Enqueue(StatOp.Add(otherSlot, ...))` on the main thread only (issue 0146 for
  Burst); intrinsics only through integer sends (floats never cross rows). A direct write into another row's value column is
  not supported and would bypass the fold.
- Belongings, storage, inventory: not an sv concept. sv has stats, intrinsics, events.

## Wanted

1. Immunity as a first-class stat pattern in the stat sheet (0145): `immune` flags or per-type `taken` multipliers with a
   documented read in the damage path. Verify that a tl `Damage.Apply` can read a stat column (tl's `Evaluate` can, as
   `Walk` reads `WalkStat`; `Apply` is unverified).
2. Optional per-entity allow-lists in the sheet (which entity profiles carry which columns) so a bad bake fails loudly
   instead of silently skipping a row.
3. Targeted damage as data: a clip with a target selector that fills `Target` and `Outgoing` without a programmer
   (0152 question 4).
4. Cross-entity reads and modifier changes from Burst with defined timing (read: last fold; write: next fold) documented in
   the designer guide.
5. A decision where belongings live (tl state, a separate library) and how they connect to stats (item equips become
   modifiers with source ids).

## Done when

A scene where player A has Hp and Mana, player B has Hp only, a shield makes A immune to damage for five seconds, A hits B
through data only, and B's Hp is read by A's code, all with pinned digests at 1 and 11 workers.


Owner clarification 2026-11-12: re-scoped by issue 0154. The premise of hand-written components and per-value C# in this file is wrong; read 0154 first.
