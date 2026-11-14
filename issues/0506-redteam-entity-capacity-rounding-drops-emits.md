# 0506 red team: EntityCapacity that is not a multiple of 64 lets extra entities live whose emits are silently dropped

Status: closed (2026-11-14, fixed by the lead; see Fix)

Severity: critical (silent wrong state; the README's own Unity example uses `EntityCapacity = 100_000`)
Kind: bug

Found by: red team (opus), branch `redteam/opus`, base sv `7f6bdb4`

## Promise broken
README chapter 11: "`sv: the world is full` - more entities than `EntityCapacity`". Every live entity follows the same rules.

## Smallest input
Reference pack, `HostOptions { Workers = 1, EntityCapacity = 100 }`. Spawn `player` 128 times, step, `Send(slot, -1, 0, Physical, 80)` to slot 0 and slot 127, step twice. `LowHealth` enters on both; its edge rule `panic_charge` emits `GiveCharges` 1 to self.

Test: `tests/Sv.Host.Tests/RedTeam/RedTeamHostMisuseTests.cs`, `An_entity_capacity_that_is_not_a_multiple_of_64_is_a_hard_limit_or_the_extra_entities_behave_like_the_rest`.

## What happens
Command: `dotnet test tests/Sv.Host.Tests -c Release --filter "FullyQualifiedName~An_entity_capacity"`

Expected: the 101st spawn throws `sv: the world is full`, or slot 127 gets its charge like slot 0.
Actual: `spawned 128 with EntityCapacity 100; slot 0 LowHealth True panic charges 1; slot 127 LowHealth True panic charges 0`.

`World` rounds the capacity up to whole chunks (`chunkCapacity = ceil(100/64) = 2`), so `Acquire` hands out slots up to 127. The host passes the unrounded `options.EntityCapacity` as `capacity` to the kernels; every generated emit is guarded by `(ulong)to < (ulong)capacity` and `SourceChunks.Read/Present` return the fallback for `slot >= capacity`. Entities in slots 100..127 are live, take damage and raise events, but their emits (edge, self, to-source thorns) vanish and reads of them as `source.X` return the fallback. With `EntityCapacity = 100_000` (README chapter 6) the same holds for slots 100,000..100,031.

## Reproducible
1 of 1, default, `DOTNET_EnableAVX2=0`, `DOTNET_EnableHWIntrinsic=0`, `taskset -c 0,1`.

## Fix direction
Make `World.Acquire` refuse slots at or above `EntityCapacity` (full world), or round `EntityCapacity` up to whole chunks before it reaches `FrameContext.Capacity` and the reference.

## Fix (lead)

The kernels' slot bound (`FrameContext.Capacity`) is now the world's slot space (`ChunkCapacity * 64`), so every slot `World.Acquire` hands out gets its emits and source reads. `EntityCapacity` is a chunk-granular budget (each template fills its own 64-entity chunks); the checklist says so.

Receipts: `bash eng/v3-verify.sh` -> `v3-verify: all green` (Laws 573, Scenario 80, Diff 48, Lego 120, Host 63 under default, AVX2 off and SIMD off, plus SV_CHECKED; Twin 32, NorthStar3 21, Web 6; Hello sample). Host tests 3 of 3 runs green under `taskset -c 0,1`. Unity 7000.0.0a16 gate with com.tl.unity: 98/98, 0 error CS, 0 warning CS, 0 error BC. Bench digests unchanged: B 6E7618BF0900D3F8, r 9CFD52083A9DA3FB, 0 B allocated.
