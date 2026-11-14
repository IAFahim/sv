# 0501 red team: a game rollback by hand, then Late + Resolve, replays every spawn, kill and stat op twice

Status: open (found by red team, not fixed)

Severity: critical (silent wrong state on valid input; `Resolve` returns true)
Kind: bug

Found by: red team (opus), branch `redteam/opus`, base sv `7f6bdb4`

## Promise broken
O8: rewind and replay of the same inputs gives the same digests (contract clause 27: "replaying the log from there reproduces every digest of the straight run").

## Smallest input
Reference pack (`packs/reference`, generated `Sv.Kernels.Reference`), `HostOptions { Workers = 1, EntityCapacity = 256 }`, defaults otherwise (ring 32, 2 frames per epoch).

1. Frame 0: `Spawn(player)`, `Spawn(rifleman)`. Every frame: `Send(hero, rifleman, Rifle, Physical, 3)`. Frame 1: `Modify(StatOp.Add(hero, MaxHealth, Added, 50, source 7))`. Step 8 frames.
2. `Rewind(1)` (frame 0), then play exactly the same inputs for frames 0..7 again (a rollback-netcode re-simulation).
3. `Late(1, {hero <- rifleman, Physical, 7})`, `Resolve()`.

Test: `tests/Sv.Host.Tests/RedTeam/RedTeamTimeTravelTests.cs`, `A_game_rollback_by_hand_then_a_late_input_resolves_to_the_straight_run` (cases `lateFrame` 0, 1, 3).

## What happens
Command: `dotnet test tests/Sv.Host.Tests -c Release --filter "FullyQualifiedName~A_game_rollback_by_hand_then_a_late_input"`

Expected: the state of a straight run that had the late record at frame 1: `ReplayFaults` 0, MaxHealth 150, digest `4532914C2DCFB691`.
Actual (lateFrame 0 and 1): `replay faults 2, MaxHealth 200, digest 05ED0980A929A545`. `Resolve()` returned true. lateFrame 3 passes (its replay window starts after the frames that hold the commands).

Cause (by reading): `FrameLoop.Rewind` never truncates the input log (`InputLog.Truncate` has no caller). Re-stepping a rewound frame calls `log.Spawn`/`log.Kill`/`log.AppendStatOps`, and `InputLog.Enter` keeps the frame slot when `slot->Frame == frame`, so the commands and stat ops are appended to the old ones. Records are not doubled because `RecordScanned` releases the frame's segments first. The next `Resolve` replays both copies: two spawns (the second gets a new slot, a fault) and the stat op twice.

## Reproducible
2 of 2 failing cases in every mode: default, `DOTNET_EnableAVX2=0`, `DOTNET_EnableHWIntrinsic=0`, `taskset -c 0,1`.

## Fix direction
A game-initiated `Rewind(e)` must drop the logged commands and stat ops of frames at or after `BeginOf(e)` (or `Enter` must replace a frame's commands on the first write after a rewind); `Resolve` keeps the log because it rewinds with `replaying = true`.

## Not checked
Kills (same code path as spawns), Unity adapter's `SvWorld.Rewind`.
