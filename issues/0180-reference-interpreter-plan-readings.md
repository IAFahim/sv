# 0180 reference interpreter: plan contradictions and the readings taken

Status: open (owner or senior confirms each reading; code follows the reading today)

Found while writing `src/Sv.Rules/Pack` and `src/Sv.Rules/Reference` (branch v3/b1-reference).

1. Shape "Multi" (plan 5.2). The formula says `* product Multi` and the example "heals twice" is `Multi 2`;
   the same section and 12.2 say Shape equals `StatMath.Fold`, which multiplies by `(1 + v)`. Reading: StatMath
   wins (one algebra), so "heals twice" is `multi: 1.0` (packs/reference rule 4108).
2. Card field (plan 6.1 vs 13.4). 6.1 writes `"card": "sniper_ward"` (a card row), 13.4 writes
   `"card": "SniperWard"` (an intrinsic). Reader accepts a card name first, then an intrinsic name.
3. Overflow level 1 (plan 4.4) says "amounts add, hits add", which does not conserve the total when hits > 1.
   Reading: merged record = (sum of amount x hits, hits 1). Levels 2 and 3 do not say origin or depth: taken as
   Direct and the maximum depth.
4. Depth limit (plan 5.4) "does not fire on a record whose depth is at its limit" contradicts reflection's default
   limit 0 reflecting depth-0 records. Reading: a rule fires iff `depth <= depthLimit`; a record it sends has
   `depth + 1`, and the compiler rejects `depthLimit + 1 > 3` for sends to others.
5. When a cell folds (5.5 "once per changed cell" vs 13.5 folding every touched row with delta 0). Reading: fold
   iff delta != 0 or a stat bounding that intrinsic changed this frame. The emitter must match.
6. When events are re-checked (5.1 "pending event re-check" undefined). Reading: an entity is re-checked iff it was
   spawned, an intrinsic folded to a new value, a stat changed, or a channel total was non-zero this or last frame.
7. Per Source / Frame emits: which records form the unit, and its depth, are unstated. Reading: the survivors with
   `depth <= depthLimit`, grouped by source (or all); no survivors, no fire; emitted depth = maximum + 1.
8. Same-frame self emits and "state-only gates" (5.4). Reading: a gate applies to a self emit iff its condition reads
   no record field except `record.channel`, no source cell and no aggregate (`PackPlan.GateIsChannelState`).

Receipts: `dotnet build src/Sv.Rules/Sv.Rules.csproj -c Release` green on net10.0 and netstandard2.1. Nothing run
(this wave has no tests by owner order).
