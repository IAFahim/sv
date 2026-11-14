# 0502 red team: game sends beyond the raw arena capacity are silently dropped under ArenaPolicy.Grow

Status: open (found by red team, not fixed)

Severity: critical (silent loss of game input with default options)
Kind: bug

Found by: red team (opus), branch `redteam/opus`, base sv `7f6bdb4`

## Promise broken
Decision D10 / README chapter 5: `Arena` Grow "grows once and reruns"; the checklist says "`Grow` on servers and editor". Documented limits must fail with an error, never with silent wrong state.

## Smallest input
Reference pack, `HostOptions { Workers = 1, EntityCapacity = 256, Arena = ArenaPolicy.Grow }` (default `RawCapacity` 65,536). One `player`. Between two steps, 200,000 times `Send(hero, -1, 0, GiveCharges, 1)`. `GiveCharges` has `into: ImmuneCharges` and no bounds, so `ImmuneCharges` counts the records that arrived.

Test: `tests/Sv.Host.Tests/RedTeam/RedTeamHostMisuseTests.cs`, `Sends_beyond_the_raw_capacity_under_the_grow_policy_all_arrive` (Shared 200,000 and Dense 1,000,000).

## What happens
Command: `dotnet test tests/Sv.Host.Tests -c Release --filter "FullyQualifiedName~Sends_beyond_the_raw_capacity"`

Expected: `ImmuneCharges` 200,000 (and 1,000,000), or an exception.
Actual: `accepted 131072, ImmuneCharges 131072 of 200000, raw capacity 131072, grows 0, missed 0` (Dense: `131072 of 1000000`).

The only signal is the `bool` returned by `Send`, which no README or sample code checks. `RawArena.Grows` stays 0 and `Missed` is reset to 0 by the next frame, so after `Step` there is no trace. Grow only covers the Decide and edge layers (`RawArena.EndLayer`); the external writer's `EffectWriter.Open` just sets `Exhausted`. The input log logs only what was sealed, so replay is consistent with the loss.

## Reproducible
2 of 2 cases in default, `DOTNET_EnableAVX2=0`, `DOTNET_EnableHWIntrinsic=0`, `taskset -c 0,1`.

## Fix direction
Under `Grow`, let an exhausted external writer grow the arena between frames (segments are append-only, existing block pointers stay valid), or throw `sv: the raw arena is full` instead of returning false; keep `Missed` readable after `Step`.
