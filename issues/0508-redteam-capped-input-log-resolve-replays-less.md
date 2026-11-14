# 0508 red team: with InputLogFrameLimit set, Resolve returns true and replays less input than was played

Status: closed (2026-11-14, fixed by the lead; see Fix)

Severity: medium (silent divergence after a late input; the cap is a host option documented only in issue 0363)
Kind: design flaw

Found by: red team (opus), branch `redteam/opus`, base sv `7f6bdb4`

## Promise broken
O8 and contract clause 27 (replay reproduces the straight run); documented limits fail with an error, never with silent wrong state.

## Smallest input
Reference pack, `HostOptions { Workers = 1, EntityCapacity = 256, InputLogFrameLimit = 2 }`. Five `player` in one chunk; every frame `Send(hero_i, -1, 0, Physical, 1 + frame)` to all five; six frames. Then `Late(2, {hero_0, Healing, 3})`, `Resolve()`. The straight run has the healing at frame 2 from the start.

Test: `tests/Sv.Host.Tests/RedTeam/RedTeamTimeTravelTests.cs`, `A_capped_input_log_never_lets_a_resolve_silently_replay_less_than_was_played`.

## What happens
Command: `dotnet test tests/Sv.Host.Tests -c Release --filter "FullyQualifiedName~A_capped_input_log"`

Expected: the straight run's digest `55197BA1DF00F8A0`, or `Resolve()` refuses (false or an exception).
Actual: `resolved True, dropped from log 19, digest B33A65E9B87AB773`.

The per-frame cap keeps 2 of 5 records per frame in the log; the replay after the rewind sends only those. The host keeps `InputLog.DroppedRecords`, but `Resolve` does not look at it. README chapter 12 does not list the cap.

## Reproducible
1 of 1, default, `DOTNET_EnableAVX2=0`, `DOTNET_EnableHWIntrinsic=0`, `taskset -c 0,1`.

## Fix direction
Mark capped frames in the log and make `Resolve` return false (or throw) when its replay window holds one; list the cap and its effect in README chapter 12.

## Fix (lead)

The input log remembers per frame how many records its cap dropped; `Resolve` throws `sv: cannot resolve: the input log cap (InputLogFrameLimit or InputLogFrameBytes) dropped N records in the frames a replay would need` instead of replaying less. README chapters 11 and 12 list it.

Receipts: `bash eng/v3-verify.sh` -> `v3-verify: all green` (Laws 573, Scenario 80, Diff 48, Lego 120, Host 63 under default, AVX2 off and SIMD off, plus SV_CHECKED; Twin 32, NorthStar3 21, Web 6; Hello sample). Host tests 3 of 3 runs green under `taskset -c 0,1`. Unity 7000.0.0a16 gate with com.tl.unity: 98/98, 0 error CS, 0 warning CS, 0 error BC. Bench digests unchanged: B 6E7618BF0900D3F8, r 9CFD52083A9DA3FB, 0 B allocated.
