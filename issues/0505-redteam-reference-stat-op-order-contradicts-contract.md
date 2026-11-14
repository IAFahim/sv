# 0505 red team: reference interpreter applies Add before RemoveSource of one source in one frame; contract and compiled do the opposite

Status: open (found by red team, not fixed)

Severity: critical (oracle and engine disagree on the most common buff pattern: refresh = remove and re-add a source)
Kind: bug (in the oracle)

Found by: red team (opus), branch `redteam/opus`, base sv `7f6bdb4`

## Promise broken
O1 (compiled == reference). Contract v3.0.1 clause 7 amended: "Canonical order of stat operations is by (slot, operation, source, stat, kind, value, expiry), with RemoveSource before Add, so a source's removal never removes what the same batch adds." v3.2.0 reaffirms it.

## Smallest input
```json
{"schema":1,"nextLane":2,"rows":[
{"id":0,"name":"Speed","class":"stat","default":5},
{"id":1,"name":"Health","class":"intrinsic","default":100},
{"id":0,"name":"Hit","class":"channel"}]}
```
No rules, no cards, template `hero`. Scenario modifiers:
```json
[{"frame":0,"entity":"Hero","stat":"Speed","shape":"Added","value":3,"source":42},
 {"frame":1,"entity":"Hero","stat":"Speed","source":42,"op":"remove"},
 {"frame":1,"entity":"Hero","stat":"Speed","shape":"Added","value":4,"source":42}]
```
Tests: `tests/Sv.Diff.Tests/RedTeam/RedTeamStatOpTests.cs` (`Refreshing_a_buff_...`, `Removing_and_adding_one_source_in_the_first_frame_keeps_the_add`).

## What happens
Command: `dotnet test tests/Sv.Diff.Tests -c Release --filter "FullyQualifiedName~RedTeamStatOp"`

Expected (contract): Speed 9 (refresh), 15 (same-frame remove + add).
Actual: `Refresh: reference Speed 5, compiled Speed 9`; `SameFrame: reference Speed 5, compiled Speed 15`.

Cause: `Sv.Rules.Reference.StatOpKind` is `Add = 0, Remove = 1` and `RefStatOp.Compare` orders by (slot, handle, kind, ...), so the reference adds then removes everything of the source. `Sv.StatOpKind` is `RemoveSource = 0, Add = 1` and `StatOps.Compare` orders by (slot, op, source, ...), as the contract says. The extreme storm test found this 30 times (lego and reference packs) before its generator was changed to keep removes and adds in different frames. Everything that runs the reference (`sv run`, scenario tests, `Sv.Web` designer) shows a refreshed buff vanishing while the game keeps it.

## Reproducible
2 of 2, default, `DOTNET_EnableAVX2=0`, `DOTNET_EnableHWIntrinsic=0`, `taskset -c 0,1`.

## Fix direction
Order the reference's stat ops Remove before Add (compare the op so Remove sorts first) and add a same-frame remove + add case to the diff generator.
