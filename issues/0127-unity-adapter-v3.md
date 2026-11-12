# Issue 0127 - V6-V9 in Unity: adapter v3 (link registry, scatter, fold, derive, digest, frame systems)

Agent C, branch `proto/c-unity` (merged `proto/int` 71e30f7). Plan steps V6, V7 (Unity half), V8, V9.

## What changed

- `packages/com.sv.unity` is adapter v3. Deleted: `SvChunk`, `SvRange`, `SvChanged`, `SvSystem`
  (`SvIntrinsicsSystem`), `UnityPopulation` (with `Reconcile`), and their 13 facade tests in
  `tests/Sv.Unity.Tests` (they tested only the deleted v2 code). `tests/Sv.Unity.Facade` stays:
  `src/Sv.Burst` compiles against its Burst attributes.
- `SvLinkRegistry`: stat, intrinsic (+ inbox), condition and kind links over `[TlState]` 4-byte
  columns; `Links.Validate` at install (throws on a fault), `Links.Hash` exposed as `LinkHash`.
- `SvHost` (managed, main thread, tier 1): owns the `Board`, slot -> entity, the inbox (64 B
  aligned `long` per slot), condition state and edge words, event buffers, StatOp queue, stat-write
  buffer. `Install` puts `SvBeginSystem` first and `SvEndSystem` last in `SimulationSystemGroup`.
- Frame (plan 2.4): Begin = slot writes for new rows, expire, canonical `StatOps` + `Board.Apply`,
  `Recalculate`, `TakeBroadcast` -> `SvScatterJob` (untyped write through
  `EntityStorageInfoLookup`), `SvFoldJob` (parallel `IJobChunk`, `chunk.DidChange` filter on delta,
  value, bound and slot versions checked before any read-write access; `Fold.Word` from `src/Sv`
  on each 64-row half; inbox `Take` only on ticks with a send; condition bits on changed rows only,
  hysteresis via `Condition`, gate written with `chunk.SetComponentEnabled`). tl kinds run between.
  End = `SvDeriveJob` per condition channel (slot-ordered words, so events come out sorted; cap ->
  one Dense record, edge bits kept), `SvDigestJob` every `DigestEvery` ticks (default 8; per-thread
  partial sums, `Board.Digest + sum CellDigest.Key`).
- Template spawn: `SvHost.Spawn(manager, prefab, templateSlot, entities)` = `Instantiate` +
  `Board.Spawn`; one job writes `SvSlot` and slot -> entity next Begin.
- Every job calls `SvProbe.Mark()` (`[BurstDiscard]`). Untyped columns use
  `DynamicComponentTypeHandle`, so no generic jobs and no `RegisterGenericJobType` are needed.
- `docs/UNITY.md`: how to use sv from tl.unity kind jobs.
- Bench harness `tests/Sv.Editor/bench` (stand-in kind job, see below) and `summarize.py`.

## Gates

- Fresh clone (`c-run/fresh.sh`): `git clone -b proto/c-unity` at acc3791, new `-createProject`,
  `eng/unity-sync.sh sync` + `check` (matches), `com.sv.unity` by `file:`: compile 0 error CS,
  0 warning CS, 0 error BC; EditMode 11/11, PlayMode 1/1 (the tl hook test compiles out without tl).
- Harness with tl.unity origin/main aeb87e9 (worktree, unchanged): EditMode 12/12, PlayMode 1/1.
- `-nullable:enable` in the synced `Runtime/csc.rsp` takes effect (0 warning CS).
- .NET: `dotnet test sv.slnx`: Sv.Tests 476, Sv.Portable.Tests 360, NorthStar 27, Sv.Unity.Tests 1
  (the .NET twin), all green.

| Test | Proves |
|---|---|
| FoldEqualsTheScalarLawWithInboxSendsAndConsumerClears | 300 rows x 20 frames, random deltas and inbox sends = `IntrinsicLaw.Step`; deltas zero after the fold |
| TheChangeFilterSkipsUntouchedChunks... | a delta written without a version bump is not folded; filter off folds it |
| ScatterWritesStatBits... | sink bits = `Board.Stat`; the same frame's fold clamps to the new bound |
| EdgeBitsBecomeSortedEvents... / AboveTheCap... | sorted events, hysteresis, gate bit; Dense record, no partial delivery, mask kept |
| DigestIsChunkOrderInvariant... | digest = managed cell sum + board digest; reverse creation order + archetype move: same digest |
| TemplateSpawn... / LinkTableRejects... | spawn slots and `SvSlot`; `RemoteWithoutInbox` |
| WarmFramesAllocateNothing... | 200 warm frames with sends and StatOps: 0 B main thread, `SvProbe` managed runs 0 |
| FoldScenarioDigestEqualsTheDotNetPin | Unity (Burst) = .NET twin (`Fold.Word` on flat arrays) = **DA47AD9B5CD74335** over 40 ticks with sends and StatOps |
| ATlKindJobWritesTheTlStateDelta... | a real tl.unity generated kind job (zero-config `[TlState]` delta, `[TlKind]` Apply `-=`) feeds the fold; folded one frame later; 30 window ticks, 80 -> 20 |
| TheFrameSystemsRunInTheDefaultWorldPlayerLoop (PlayMode) | default world, player loop, sum-then-clamp, probes |

NorthStar2 digest equality with .NET: waits for agent D's pin (ask the manager).

## Bench

BENCH_TABLE

## Known limits and findings

- All inbox sends to one slot serialize (V0: ~10 ns per send). Shipped as a documented limit
  (manager decision); the per-worker fallback is not built.
- Core finding sent to the manager: `Board` memory and Apply/Recalculate time grow without bound
  under add/remove churn (.NET repro `c-out/repro`); the bench builds a fresh world per cell.
- Board.BindSink does not fill pre-existing rows; the adapter binds sinks before any row exists.
