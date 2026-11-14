# 0401 fuzz: O1 breaks on per-Frame/Source emit when when-conjuncts require disjoint aggregate channels

Status: closed (2026-11-14, fixed in the kernel emitter; see Fix)

Found by: agent-1 (`fuzz/agent-1`), `eng/fuzz.sh 10010000 8 default` (round offset `10070000`, `seed 10073227`), sv commit `b4ff10f`

## Promise broken
O1: Compiled kernels give the same values and digest as the reference interpreter, every frame (checked by `DiffTests.CompiledVersusReference.Compiled_kernels_equal_the_reference_on_packs_without_absorb`, check `noabsorb`).

## Smallest input

`values.sv.json`:
```json
{"schema":1,"nextLane":1,"rows":[
{"id":0,"name":"Health","class":"intrinsic","default":10},
{"id":0,"name":"C0","class":"channel"},
{"id":1,"name":"C1","class":"channel"}
]}
```

`rules.sv.json`:
```json
{"schema":1,"rows":[
{"id":1,"name":"flow","layer":"flow","then":[{"subtract":["Health","C0"]}]},
{"id":2,"name":"emit","layer":"emit","depthLimit":1,"when":{"and":[{"gt":[{"taken":["C0"]},0]},{"gt":[{"offered":["C1"]},0]}]},"then":[{"emit":{"to":"Self","channel":"C0","amount":1,"per":"Frame"}}]}
]}
```

`cards.sv.json`:
```json
{"schema":1,"rows":[]}
```

`templates.sv.json`:
```json
{"schema":1,"rows":[
{"id":1,"name":"base"}
]}
```

`scenario.json`:
```json
{"schema":1,"name":"shrink","frames":1,
"entities":[{"name":"E0","template":"base"}],
"sends":[
{"frame":0,"target":"E0","channel":"C0","amount":1},
{"frame":0,"target":"E0","channel":"C1","amount":1}
],
"kills":[],
"modifiers":[]}
```

## What happens
Command: `bash eng/fuzz.sh 10070000 1 default`
Original failing output (`fuzz-out/offset-10070000-default.log`, receipt `noabsorb-seed10073227.txt`):
`FAIL offset 10070000 (default):  Passed: 19  Failed: 1`
`1 of 300 seeds failed; first: seed 10073227: frame 8 E5.Health: expected 9, got 11`

Shrunk repro output (`ShrinkRepro` against the pack above):
`frame 0 E0.Health: expected 8, got 9`

Expected: Frame 0 `E0.Health` = `8` (`10` default minus sent `C0:1` minus `Self`-emitted `C0:1`); on original `seed 10073227`, frame 8 `E5.Health` = `9`. Per `contract/CONTRACT-v3.0.0.md` Clause 25, `contract/CONTRACT-v3.1.0-bricks.md` (reference is the oracle), and `src/Sv.Rules/Reference/Interpreter.cs` (`Units` lines 984-994 and `Aggregate` lines 1778-1797), `E0` receives both `C0:1` (`taken(["C0"]) == 1 > 0`) and `C1:1` (`offered(["C1"]) == 1 > 0`), so the `per: "Frame"` rule emits `C0:1` to `Self`.

Actual: Frame 0 `E0.Health` = `9` on the shrunk pack (`11` on `seed 10073227` frame 8 `E5.Health`). `KernelEmitter.AggregateChannels` (`src/Sv.Rules/Emit/Kernels.Presence.cs` lines 154-173) intersects (`Intersect`, line 168) the required aggregate channel sets across `And` conjuncts (`AggregateNeeds`). Intersecting disjoint channel sets (`{C0}` and `{C1}` here; `{C0, C2}` and `{C1, C4}` in `rule1016` of `seed 10073227`) returns `long[0]`, and `Presence` (`Kernels.Presence.cs` lines 85-93) emits `(present & 0x0000000000000000ul) != 0ul` (`seed10073227/Kernels.g.cs` line 417) around the `Self` emit call, making the emit dead code.

First differing frame: `8`, value: `E5.Health` (`seed 10073227`); `0`, value: `E0.Health` (shrunk repro)

## Seen again (same `AggregateChannels` intersection bug, both disjoint `{}` and non-empty strict-subset intersections)
- `eng/fuzz.sh 10090000 8 default`, round `10130000`, `seed 10139075` (`bricks-values` 1/160 and `bricks-workers` 5/160 across 1/2/4/7/11 workers): `frame 9 E2.Flag0: expected 90, got 81` (receipts `fuzz-out/orig-10130000-default/bricks-values-seed10139075.txt` and `bricks-workers-seed10139075.txt`). `rule1013` (`per: "Frame"`) has `and(gt(offered(["C2","C0"]),12), gt(taken(["C2","C3"]),8))`; `AggregateChannels` intersects `{C2, C0}` and `{C2, C3}` to `{C2}` (`(present & 0x0000000000000004ul) != 0ul` at `seed10139075/Kernels.g.cs` line 493), so frame 9 sending `C0` and `C3` without `C2` skips `SelfRule1013` (`C3:9`).
- `eng/fuzz.sh 10090000 8 default`, round `10140000`, `seed 10140247` (`values` 1/300): `frame 3 E3.Health: expected -200, got -197` (receipt `fuzz-out/orig-10140000-default/values-seed10140247.txt`). `rule1008` (`per: "Source"`) has `and(gt(offered(["C1","C4"]),2), gt(taken(["C3","C4"]),6))`; `AggregateChannels` intersects `{C1, C4}` and `{C3, C4}` to `{C4}` (`(present & 0x0000000000000010ul) != 0ul` at `seed10140247/Kernels.g.cs` line 412), so frame 3 sending `C1` and `C3` without `C4` skips `SelfRule1008` (`C2:3`).
- `eng/fuzz.sh 10170000 8 default`, round `10190000`, `seed 10195004` (`cardmodifiers` 2/40, first): `frame 2 E0.Health: expected 153, got 146` (`fuzz-out/orig-10190000-default/cardmodifiers-seed10195004.txt`; `rule1008` `{C2} ∩ {C0}` -> `0x0ul` at `Kernels.g.cs` lines 383/390).
- `eng/fuzz.sh 10170000 8 default`, round `10190000`, `seed 10195030` (`cardmodifiers` 2/40, second): `frame 10 E1.Health: expected 78, got 83` (`fuzz-out/orig-10190000-default/cardmodifiers-seed10195030.txt`; `rule1008` `{C2} ∩ {C1}` -> `0x0ul` at `Kernels.g.cs` line 410).
- `eng/fuzz.sh 10170000 8 default`, round `10200000`, `seed 10200063` (`values` 1/300): `frame 1 E1.Budget: expected 2812, got 10` (`fuzz-out/orig-10200000-default/values-seed10200063.txt`; `rule1011` `{C1} ∩ {C3, C4}` -> `0x0ul` at `Kernels.g.cs` lines 405/412).
- `eng/fuzz.sh 10170000 8 default`, round `10230000`, `seed 10230060` (`values` 1/300): `frame 1 E3.Health: expected -1999999971, got -999999928` (`fuzz-out/orig-10230000-default/values-seed10230060.txt`; `rule1012` `{C1, C4} ∩ {C3, C4}` -> `{C4}` `0x10ul` at `Kernels.g.cs` line 436).
- `eng/fuzz.sh 10170000 8 default`, round `10230000`, `seed 10239160` (`bricks-values` 1/160 and `bricks-workers` 5/160): `frame 10 E0.Health: expected 58, got 63` (`fuzz-out/orig-10230000-default/bricks-values-seed10239160.txt`; `rule1009` `{C0, C4} ∩ {C1, C2}` -> `0x0ul` at `Kernels.g.cs` lines 533/540).

## Reproducible
5 of 5 runs on `10070000` (`2 of 2` in `default`, `1 of 1` in `noavx2`, `1 of 1` in `scalar`, `1 of 1` in `contended`), and all 8 seeds (`10073227`, `10139075`, `10140247`, `10195004`, `10195030`, `10200063`, `10230060`, `10239160`) produce byte-identical failure receipts across all 4 modes (`default / noavx2 / scalar / contended`) in the `10000000`–`10240000` campaign.

## Not checked
- Direct `ShrinkRepro` runs under `DOTNET_EnableAVX2=0`, `DOTNET_EnableHWIntrinsic=0`, or `taskset -c 0,1` (confirmed via `eng/fuzz.sh` across all 4 modes).
- `docs/FUZZING.md` section 4 harnesses (`O7`–`O10`) were not built yet because section 3 is not clean (`6` of `25` rounds fail on `0401`).

## Fix (lead)

Cause confirmed: a `per: "Frame"`/`"Source"` emit reads each `taken`/`offered` conjunct over its own records, so the rule can fire only if each conjunct's channel set has one present channel. `KernelEmitter.AggregateChannels` intersected the sets into one `(present & mask) != 0ul` guard, which is wrong whenever the sets differ (disjoint sets gave mask 0, a dead emit). The other intersections (`ExprTools.ChannelsOf`, `PackPlan.Allowed`, `RecordChannels`) constrain one record's channel, where intersection is right; they are unchanged.

- `src/Sv.Rules/Emit/Kernels.Presence.cs`: `AggregateChannels` returns one channel set per aggregate conjunct and `CallGuard` ANDs one presence test per set; per-record emits keep their single guard (`RecordChannels`). `packages/com.sv.engine` synced.
- Regression: `tests/Sv.Diff.Tests/HandwrittenTests.cs` `An_aggregate_emit_fires_when_each_conjunct_sees_its_own_channel` (6 cases: disjoint and overlapping sets, `Frame` and `Source`, plus two controls). 4 of 6 failed before the fix (`expected 8, got 9`), 6 of 6 pass after.
- No shipped pack used the shape: Hello, Lego, NorthStar3 and Reference regenerate byte-identical with `sv pack --host unity`, so no digest, PackHash or Unity pin moved.

Receipts: `bash eng/v3-verify.sh` -> `v3-verify: all green`; `eng/fuzz.sh <o> 1 default` for the six failing rounds 10070000, 10130000, 10140000, 10190000, 10200000, 10230000 -> `Passed: 26` each.
