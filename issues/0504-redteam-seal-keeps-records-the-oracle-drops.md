# 0504 red team: compiled Seal keeps records the reference drops as malformed (unknown channel, source outside the world)

Status: closed (2026-11-14, fixed by the lead; see Fix)

Severity: high (compiled differs from the oracle; silent)
Kind: bug

Found by: red team (opus), branch `redteam/opus`, base sv `7f6bdb4`

## Promise broken
O1: compiled kernels give the same values and digest as the reference interpreter. Malformed input must be rejected, never silently accepted.

## Smallest input
Pack (`RedTeamPacks.CatchAll` in `tests/Sv.Host.Tests/RedTeam/RedTeamHostMisuseTests.cs`):

```json
{"schema":1,"nextLane":3,"rows":[
{"id":0,"name":"Health","class":"intrinsic","default":100},
{"id":1,"name":"Echo","class":"intrinsic","default":0},
{"id":2,"name":"Kind","class":"intrinsic","default":0,"role":"kind"},
{"id":0,"name":"Physical","class":"channel"},
{"id":1,"name":"Ping","class":"channel","into":"Echo"},
{"id":0,"name":"None","class":"kind"}]}
```
```json
{"schema":1,"rows":[
{"id":1,"name":"health_flow","layer":"flow","then":[{"subtract":["Health","Physical"]}]},
{"id":2,"name":"echo_every_record","layer":"emit","then":[{"emit":{"to":"Self","channel":"Ping","amount":1,"per":"Record"}}]}]}
```
One template `unit`, no cards, `EntityCapacity` 256. Two units; frame 1: `Send(unit, other, 0, channel 2 | 64 | 200, 5)`, or `Send(unit, source 300 | 4000000, 0, Physical, 5)`. Compared with `Sv.Rules.Reference.Interpreter` fed the same records (`RedTeamDuo`).

Tests: `A_record_on_a_channel_the_pack_does_not_have_is_treated_alike_by_compiled_and_reference` (2, 64, 200), `A_record_whose_source_is_outside_the_world_is_treated_alike_by_compiled_and_reference` (300, 4000000).

## What happens
Command: `dotnet test tests/Sv.Host.Tests -c Release --filter "FullyQualifiedName~A_record_"`

Expected (reference): the record is dropped in Seal (`faults.DroppedMalformed`, `Interpreter.Seal`: channel index < 0, or source neither `NoSource` nor a slot): `Echo` 0.
Actual: `Echo compiled 1, reference 0; digest compiled 3A418E98E68A5054, reference 3EB41343DBAD63FE` (channels); `Echo compiled 1, reference 0; digest compiled 7C9D737AA33D95EC, reference 3EB41343DBAD63FE` (sources; compiled Health also takes the 5).

`Route.Deliver` checks only the target; an emit rule without a channel condition fires on the unknown record. By reading (not tested): `FrameLoop.Send` and `EffectWriter.Send` cast `kind` and `channel` to `byte`, so channel 256 silently becomes channel 0 and kind 257 becomes kind 1.

## Reproducible
5 of 5 cases, default, `DOTNET_EnableAVX2=0`, `DOTNET_EnableHWIntrinsic=0`, `taskset -c 0,1`.

## Fix direction
Drop (and count) in `Seal.Count` every record whose channel is not a pack channel or whose source is neither `NoSource` nor below the capacity, as the reference does; validate kind and channel range in `Send` instead of truncating.

## Fix (lead)

Seal now drops, like the reference, every record whose channel is not a pack channel or whose source is neither `NoSource` nor inside the world (`Route.Malformed`, counted in `RouteFaults`). The pack's channel ids travel in `HostPack.ChannelIds` (generated `PackData.g.cs` passes them only when they are not 0..n-1, so every committed generated file is unchanged). `FrameLoop.Send` throws `ArgumentOutOfRangeException` for a kind or channel outside 0..255 instead of truncating it to a byte.

Receipts: `bash eng/v3-verify.sh` -> `v3-verify: all green` (Laws 573, Scenario 80, Diff 48, Lego 120, Host 63 under default, AVX2 off and SIMD off, plus SV_CHECKED; Twin 32, NorthStar3 21, Web 6; Hello sample). Host tests 3 of 3 runs green under `taskset -c 0,1`. Unity 7000.0.0a16 gate with com.tl.unity: 98/98, 0 error CS, 0 warning CS, 0 error BC. Bench digests unchanged: B 6E7618BF0900D3F8, r 9CFD52083A9DA3FB, 0 B allocated.

Cost: Seal now reads a 4-word channel mask and compares the source per record. Interleaved runs against main on a loaded machine (a fuzz campaign at about 700% CPU was running) gave Seal B 2.35-2.59 ms vs 2.20-2.46 ms on main, about +0.2 ms; totals were too noisy to read (main itself measured 8.1-9.3 ms against 7.7 ms quiet). Re-measure on a quiet machine; B is expected near 7.9 ms, inside the 8.3 ms gate.

Pinned re-measure (same session, both builds `taskset -c 2-11` while a 2-CPU fuzz campaign held CPUs 0-1, 5 interleaved processes each, `v3-lego-b --ms 1000`): Seal median 2.31 ms on main vs 2.46 ms with the fix (+0.15 ms); frame total median 8.89 vs 8.93 ms (within noise). Absolute numbers are higher than the quiet 12-CPU 7.7 ms baseline because only 10 CPUs were used.
