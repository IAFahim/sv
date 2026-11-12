# 0300 overflow merge keeps records whose merged amount is zero

Status: closed

Found by `tests/Sv.Laws.Tests` (branch v3/t1-laws), test
`InputLaws.OverflowMergeOutputIsStillCanonicalWithNoZeroAmounts` (5 of 5 seeds fail).

Contract v3.0.0 clause 21: "Zero amounts are not stored." Clause 22: the overflow merge produces canonical records.
`Seal.Check` (SV_CHECKED) rejects `Amount == 0` too.

Inputs: 3,000 random records (amounts clamped to -1000..1000, hits 0..3, 16 chunks, every target live), sealed by
`CanonicalArena(16, 3, limit, ArenaPolicy.HardCap)` with the limit below the exact record count, so `Degrade` runs.

Expected: every canonical record has `Amount != 0` and `Hits > 0`, strictly ascending.

Got (examples, `seed` is the test seed):
- seed 1, limit 2578, level MergedSources: record 931 = target 379, source NoSource, kind 2, channel 0, Direct, depth 0, amount 0, hits 1.
- seed 2, limit 1929, level MergedKinds: record 0 = target 0, source NoSource, kind 0, channel 0, Reflected, depth 1, amount 0, hits 1.
- seed 4242, limit 2600, level MergedSources: record 362 = target 149, kind 2, channel 0, Triggered, depth 3, amount 0, hits 1.

Cause: when the terms of a merged group cancel (for example +5 and -5 from two sources), the group is written as
`new Effect(key, Saturate32(0), 1)` without a zero check:
- `src/Sv.Core/Seal.cs:374` (`CoalesceByKey`, levels 1 and 2),
- `src/Sv.Core/Seal.cs:406` (`CoalesceByChannel`, level 3).

Effects: a zero record still counts against the limit (a later level or `Truncated` can trigger when it need not);
a SV_CHECKED build throws in `CanonicalArena.End`; rules that count records or hits ("every damage you take,
heal 5" per record or per hit) see a hit that carried nothing. Totals per (target, channel) are still conserved
(`OverflowMergeConservesTotalsPerTargetAndChannelAtEveryLevel` passes).

Suggested fix (manager decides): skip a merged group whose total is 0 in both coalesce functions (do not write it,
do not advance `written`).

Fixed 2026-11-13: both coalesce functions skip a merged group whose saturated total is 0 (src/Sv.Core/Seal.cs). Sv.Laws.Tests: 334 of 334 pass on net10.0 default.
