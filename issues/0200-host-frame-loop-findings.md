# 0200 host frame loop: rewind gap, kernel call surface, readings taken

Status: closed (2026-11-14, decided by the lead dev under the owner's standing delegation ("think like a senior dev and take shots", 2026-11-14))

Found while writing `src/Sv.Host` and `src/Sv.Pack` (branch v3/c2-host).

1. **Rewind loses the records in flight.** Plan 9.3 logs only external records; reflected, triggered and edge-emitted
   records "are recreated by replay". They are recreated only from the frame before them. `Rewind(e)` lands at the
   start of epoch e, and the derived records sent by the last frame of epoch e-1 (and the event re-check rows) are in
   no journal and no log. Implemented: at each epoch mark the host copies the raw arena (derived records only at that
   point) and the pending re-check rows into a per-epoch carry (`FrameLoop.Carry`), and `Rewind` sends them back.
   Cost: one copy of next-frame derived records per epoch.
2. **Kernel call surface (for C1).** Plan 13.5 shows `Decide/Commit/LowHealthEdges` for one pack. The host needs a
   pack-independent surface, `Sv.Host.IPackKernels` (net10, so generated Unity code cannot implement it). `sv pack`
   emits `HostKernels.g.cs`, a struct that forwards to static members of the generated `Kernels` and `Layout`:
   `Layout.PackHash`, `Layout.CompiledAgainst`, `Kernels.ScratchLanes/DeltaLanes/EventCount/LanesWrittenPerChunk`,
   `DeltaTarget(d)`, `FloorStat(d)`, `CeilingStat(d)`, `Bounds(world, chunk, d, int* floor, int* ceiling)`,
   `EventId(ordinal)`, `Forwards(template)`, `Forward(world, slot, channel)`,
   `Decide(world, chunk, Effect*, count, int* scratch, ref EffectWriter)` (delta lanes first in scratch, int, stored
   saturated; returns rows with a non-zero channel total), `Events(world, ref LaneWriter, chunk, recheck, scratch,
   ulong* edges)` (writes levels, one edge mask per event ordinal, no sends), `EdgeEmits(world, chunk, edges, ref
   EffectWriter)` (sends only, so it can rerun after the arena grows). The host does the fold itself (Net/CommitFold).
   `sv pack` reaches the emitter as `Sv.Rules.Emit.PackEmitter.Emit(PackPlan, string) -> (file, text) pairs`.
3. **Fold rows.** A cell folds iff its delta is non-zero, or a stat its floor or ceiling reads changed this frame
   (issue 0180 item 5), from `StatMachine.ChangedPairs`.
4. **Re-check rows.** recheck = spawned since last step | touched last frame | stat changed | folded to a new value |
   touched this frame, masked by live rows (issue 0180 item 6).
5. **Slots in scenarios.** The reference interpreter takes explicit slots; the world picks them. `SlotPlanner` repeats
   `World.Acquire` (chunks per template, lowest free row) and never reuses a killed slot; the compiled runner reports a
   differing slot as a failure.
6. **Edge emits with `Origin.Direct`** would be logged as external input and doubled on replay. Generated edge emits
   must use Triggered or Reflected.

Receipts: `dotnet build src/Sv.Pack/Sv.Pack.csproj -c Release` (Sv.Core and Sv.Rules both frameworks, Sv.Host, Sv.Pack)
and `dotnet build benchmarks/Sv.Bench/Sv.Bench.csproj -c Release`: 0 warnings, 0 errors. `sv run packs/reference
packs/reference/scenarios` on the reference interpreter: worked example health 95, S2 health 75 next frame, digest
E0F445186E2C3672 (not pinned). No tests run (owner order).

## Decision (2026-11-14)

Item 1 (per-epoch carry of in-flight derived records) accepted: it is the only way Rewind lands on a frame start that replays identically, and the rewind laws pin it. Item 2 is implemented (`IPackKernels`). Readings 3-6 confirmed; `Origin.Direct` stays forbidden for generated edge emits.
