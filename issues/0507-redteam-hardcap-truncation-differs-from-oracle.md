# 0507 red team: under ArenaPolicy.HardCap the compiled Seal truncates past merge level 3; the reference keeps every record and the contract defines neither

Status: open (found by red team, not fixed)

Severity: high (compiled differs from the oracle; damage silently lost, only `CanonicalArena.DroppedOverflow` counts it)
Kind: design flaw (contract gap) and bug

Found by: red team (opus), branch `redteam/opus`, base sv `7f6bdb4`

## Promise broken
O1, and contract clause 22: "merged in canonical order in three levels until they fit ... Totals per (target, channel) are conserved at every level". Nothing says what happens when level 3 still does not fit.

## Smallest input
Reference pack, `HostOptions { Workers = 1, EntityCapacity = 256, Arena = ArenaPolicy.HardCap, CanonicalLimit = 3 }`, reference `InterpreterOptions.ArenaCapacity = 3`. Four `worked_example_player`, three `sniper`, three `rifleman`. Three frames: each sniper sends Physical `4 + frame` to every victim, each rifleman sends Physical 3 and Poison 2.

Test: `tests/Sv.Host.Tests/RedTeam/RedTeamHostMisuseTests.cs`, `A_hard_capped_canonical_arena_merges_like_the_reference` (limits 1,000,000, 40, 12, 8, 3).

## What happens
Command: `dotnet test tests/Sv.Host.Tests -c Release --filter "FullyQualifiedName~A_hard_capped_canonical"`

Expected: equal digests (as for limits 8, 12, 40, 1,000,000, which pass: merge levels 1-3 match).
Actual, limit 3: `overflow Truncated, digest compiled 01FC0B4AB33327D3, reference E9AD91EC2BE55DAD`.

`CanonicalArena.Degrade` drops every record past the limit after level 3 (`Seal.Truncate`); `Interpreter.Overflow` stops at level 3 and keeps them. Totals per (target, channel) are not conserved, against clause 22.

## Reproducible
1 of 1, default, `DOTNET_EnableAVX2=0`, `DOTNET_EnableHWIntrinsic=0`, `taskset -c 0,1`.

## Fix direction
Decide the level-4 rule in the contract (truncate in canonical order, or refuse the frame) and make the reference do the same; document it next to `Arena` in the checklist.
