# 0511 Hard-capped arena: merge levels 2 and 3 differed between the compiled arena and the reference

Status: closed (2026-11-14, fixed by the lead in the same change that found it)

Found by: the lead's new differential check `A_hard_capped_arena_merges_and_cuts_like_the_reference` (`tests/Sv.Diff.Tests/DiffTests.cs`), added because the red team's 0507 test covered only the reference pack. On 300 random packs with `ArenaPolicy.HardCap`: `CanonicalLimit` 1,000,000, 200 and 64 had 0 failures; 24 had 2 (seed 144: `frame 4 E0.Health: expected 32, got 6`); 12 had 9; 6 had 24 (seed 2: `frame 1 E0.Card0: expected -1, got 0`); 3 had 84.

## Promise broken
O1 (compiled == reference) under the hard cap, and contract clause 22: when canonical records exceed the capacity "they are merged in canonical order in three levels until they fit: equal Key; then across sources per (target, kind, channel); then across kinds. Totals per (target, channel) are conserved at every level".

## Cause
Clause 22 leaves three details open, and the two engines read them differently:

| Detail | Compiled (`Seal.Degrade`) | Reference (`Interpreter.MergeLevel`) |
|---|---|---|
| Level 2 grouping | equal key after removing the source, so records with different origin or depth never merged | per (target, kind, channel), as clause 22 says |
| Origin and depth of a merged record | highest origin, deepest depth | origin forced to Direct, deepest depth |
| A record alone in its group | kept as it was (amount and hits) | flattened to (amount x hits, hits 1), which changes per-hit rules such as absorb |

## Reading (decided, recorded here like 0180 and 0350)
1. Level 2 merges per (target, kind, channel), ignoring origin and depth (clause 22 text).
2. A merged record keeps the highest origin and the deepest depth of its group (the reading already recorded for the overflow merge, `sv-v3-architecture-and-process.md` A9).
3. A record that merges with nothing is not merged: it keeps its amount and hits; only its key is coarsened (no source at level 2, no kind at level 3).
4. After level 3, records past the limit are cut in canonical order and counted (0507).

## Fix
- `Seal.Degrade` uses the lineage-ignoring coalesce (`CoalesceByChannel`) at levels 2 and 3 (the key still holds the kind at level 2), so level 2 groups per (target, kind, channel).
- `Interpreter.MergeLevel` keeps the highest origin of a merged group and leaves a group of one as it was.
- The differential suite now runs every main seed with the hard cap at 3, 6 and 24 records and with a capacity that is not a multiple of 64 (`tightcapacity`), and the pack generator now removes modifiers in the frame they were added and refreshes a source (remove and add in one frame): the shapes behind 0505, 0506 and 0507 are in every fuzz round.

## Receipts
`dotnet test tests/Sv.Diff.Tests -c Release`: 50 of 50 (hardcap 300 seeds at limits 3, 6, 24: 0 failed; tightcapacity 300: 0 failed). Red-team hard-cap tests (`Sv.Host.Tests`, limits 3 to 1,000,000): 8 of 8. Only hard-capped worlds that overflow are affected; the default `Grow` arena and every pinned digest are unchanged.
