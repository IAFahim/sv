# 0504 red team: compiled Seal keeps records the reference drops as malformed (unknown channel, source outside the world)

Status: open (found by red team, not fixed)

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
