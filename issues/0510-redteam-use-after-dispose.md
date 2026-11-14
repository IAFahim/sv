# 0510 red team: calls on a disposed FrameLoop throw NullReferenceException or a misleading "unknown template"

Status: closed (2026-11-14, fixed by the lead; see Fix)

Severity: low (misleading diagnostic on misuse; no memory corruption seen)
Kind: design flaw (API footgun)

Found by: red team (opus), branch `redteam/opus`, base sv `7f6bdb4`

## Promise broken
Misuse fails with a clear error (README chapter 11 lists every `sv:` message a game may see).

## Smallest input
Reference pack, `HostOptions { Workers = 1, EntityCapacity = 256 }`: spawn, `Step()`, `Dispose()`, `Dispose()` (the second is safe), then `Send`, `Spawn`, `Step`, `Rewind(1)`.

Test: `tests/Sv.Host.Tests/RedTeam/RedTeamHostMisuseTests.cs`, `Calls_on_a_disposed_world_throw_ObjectDisposedException`.

## What happens
Command: `dotnet test tests/Sv.Host.Tests -c Release --filter "FullyQualifiedName~Calls_on_a_disposed_world"`

Expected: `ObjectDisposedException` from each call.
Actual: `Send: NullReferenceException; Spawn: ArgumentOutOfRangeException; Step: NullReferenceException; Rewind: InvalidOperationException`. `Spawn` says `sv: unknown template` because the disposed world has 0 templates.

## Reproducible
1 of 1, default, `DOTNET_EnableAVX2=0`, `DOTNET_EnableHWIntrinsic=0`, `taskset -c 0,1`.

## Fix direction
Keep a disposed flag in `FrameLoop` and throw `ObjectDisposedException` (netstandard-safe form) at the top of every public call.

## Fix (lead)

Every public `FrameLoop` call (`Send`, `Sender`, `Modify`, `Spawn`, `Kill`, `Step`, `Rewind`, `Resolve`) throws `ObjectDisposedException` after `Dispose`; a second `Dispose` stays safe.

Receipts: `bash eng/v3-verify.sh` -> `v3-verify: all green` (Laws 573, Scenario 80, Diff 48, Lego 120, Host 63 under default, AVX2 off and SIMD off, plus SV_CHECKED; Twin 32, NorthStar3 21, Web 6; Hello sample). Host tests 3 of 3 runs green under `taskset -c 0,1`. Unity 7000.0.0a16 gate with com.tl.unity: 98/98, 0 error CS, 0 warning CS, 0 error BC. Bench digests unchanged: B 6E7618BF0900D3F8, r 9CFD52083A9DA3FB, 0 B allocated.
