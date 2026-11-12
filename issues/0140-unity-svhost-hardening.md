# Issue 0140 - U2: SvHost hardening (rows before Install, Unbind, destroyed rows, capacities, one host, digest cadence)

Status: closed 2026-11-12. Verified by an independent fresh clone of proto2/int at 4fcd3e3 (logs verify2-out). Remaining items live in 0149, 0150, 0151.

Branch `proto2/unity`. Owner of the change: Unity agent (phase 2).

| Trap (VERIFY.md risk 4, UNITY.md sharp edges) | Now | Test |
|---|---|---|
| Rows on the board before `Install` never scattered | `Install` adopts every entity whose `SvSlot` names a live board slot and binds every sink with `fillExisting: true`; two entities on one live slot throw at `Install` | `RowsThatExistBeforeInstallAreAdoptedAndScattered`, `TwoEntitiesClaimingOneLiveSlotFailAtInstall` |
| `Unbind` keeps `SvSlot`, does not cancel a same-frame `Bind` | `Unbind` cancels a pending `Bind` (O(1) slot -> pending index), frees the slot, and the next `SvBeginSystem` removes `SvSlot` + `SvSlotCleanup` | `UnbindRemovesSvSlotStopsTheFoldAndCancelsASameFrameBind` |
| Destroyed entity leaks its slot | new `SvSlotCleanup : ICleanupComponentData`, added with `SvSlot` to every bound row in `SvBeginSystem`; a husk (`SvSlotCleanup` without `SvSlot`) frees its slot next frame; a row destroyed before its first frame is dropped from the pending list | `DestroyedEntitiesFreeTheirSlotsWithoutUnbind` (50 spawn/destroy rounds, `LiveCount` flat) |
| `Enqueue` throws > 65,536 StatOps, scatter throws > 65,536 writes | the StatOp queue doubles (allocates only on growth); the scatter loops `TakeBroadcast` in `writeCapacity` chunks | `StatOpsAndStatWritesAboveTheCapacityAreChunkedNotThrown` (8-write buffer, 4-op queue, 300 rows) |
| One host per world, silently | `Install` throws "one SvHost per world" when the world already has an `SvHostRef` | `ASecondHostInOneWorldFailsAtInstall` |
| `DigestEvery` fixed 8, `Digest` stale | `Install(..., digestEvery: 8)`, settable, 1 = every tick; `DigestTick` = tick of the current `Digest` (-1 before the first) | `DigestEveryIsConfigurableAndDigestTickSaysWhenTheDigestWasTaken` |

Also new: `SvHost.InboxCell`, `EdgeWords`, `ConditionWords`, `IsPending`, `QueuedOps`, `OpCapacity`, `NativeBytes`.
A row whose `SvSlot` is removed by the user is treated as destroyed (its slot is freed).
A row bound without `SvSlotCleanup` in its archetype moves archetype once at its first frame; put both components in the archetype to avoid the move.

Receipt (Unity 7000.0.0a16, harness `c-harness` repointed to this worktree, `eng/unity-sync.sh check` ok, bench.lock + unity.lock held 16:10:11-16:11:22):
compile exit 0, error CS 0, warning CS 0, error BC 0; EditMode 26/26 (17 existing + 9 new), log `_work/sv-proto/p2-out/g1-*`.
Existing pins unchanged: fold scenario DA47AD9B5CD74335, NorthStar2 fold 0D945D8FD131DA2A, links A47434070C42E4AC, NorthStar2 Unity 939FBDEE2E7DEE50 / E5CE5FA31824A22E / 75035C43BBDC14AF.
