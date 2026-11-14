# 0502 red team: game sends beyond the raw arena capacity are silently dropped under ArenaPolicy.Grow

Status: closed (2026-11-14, fixed by the lead; see Fix)

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

## Fix (lead)

With one external writer and `ArenaPolicy.Grow`, `FrameLoop.Send` grows the raw arena (doubling, `RawArena.GrowFor`) and retries when the writer runs out of blocks, so every send arrives. With several `ExternalWriters` (threads) or a hard cap, a full arena still makes `Send` return false, and the new `FrameLoop.ExternalDropped` counts those records across frames. README chapter 11 and the checklist (`RawCapacity` row) say so.

Receipts: `bash eng/v3-verify.sh` -> `v3-verify: all green` (Laws 573, Scenario 80, Diff 48, Lego 120, Host 63 under default, AVX2 off and SIMD off, plus SV_CHECKED; Twin 32, NorthStar3 21, Web 6; Hello sample). Host tests 3 of 3 runs green under `taskset -c 0,1`. Unity 7000.0.0a16 gate with com.tl.unity: 98/98, 0 error CS, 0 warning CS, 0 error BC. Bench digests unchanged: B 6E7618BF0900D3F8, r 9CFD52083A9DA3FB, 0 B allocated.
