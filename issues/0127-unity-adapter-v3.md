# Issue 0127 - V6-V9 in Unity: adapter v3 (link registry, scatter, fold, derive, digest, frame systems)

Status: closed 2026-11-12. Verified by an independent fresh clone of proto2/int at 4fcd3e3 (logs verify2-out). Remaining items live in 0149, 0150, 0151.

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

- Fresh clone (`c-run/fresh.sh`): `git clone -b proto/c-unity` at eaf8a4d (after merging proto/int
  with d-frame), new `-createProject`, `eng/unity-sync.sh sync` + `check` (matches), `com.sv.unity`
  by `file:`: compile 0 error CS, 0 warning CS, 0 error BC; EditMode 14/14, PlayMode 1/1 (the tl hook
  test compiles out without tl). The later fold-loop commit 02a5c4b ran in the harness only.
- Harness with tl.unity origin/main aeb87e9 (worktree, unchanged) at 02a5c4b: 0/0/0, EditMode 15/15,
  PlayMode 1/1.
- `-nullable:enable` in the synced `Runtime/csc.rsp` takes effect (0 warning CS).
- .NET after the merge: `dotnet test sv.slnx`: Sv.Tests 495, Sv.Portable.Tests 379, NorthStar 27,
  NorthStar2 9, Sv.Unity.Tests 1 (the .NET twin), all green.

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

| SvNorthStar2PinTests (3) | D's fold scenario **0D945D8FD131DA2A** on the Unity sv core build (managed `Kernel.Fold`) and through the Burst fold job on 4,099 entities; D's link table hash **A47434070C42E4AC** |

| SvNorthStar2UnityTests (workers 1 and 11) | D's NorthStar2 in Unity with Burst: the same typed timelines baked in the editor with tl.unity's vendored `LibraryBake`/`LibraryAssembler` (3,192 B), loaded with `TlLibrary.FromBytes`, 1,000 entities driven by the tl.unity systems under `TlSimulationGroup` (`[TlState]` HpDelta/Outgoing/ArmorPulse written by tl's generated kind jobs), the sv half on the Unity sv core build with D's exact frame order: per-tick digests **939FBDEE2E7DEE50, E5CE5FA31824A22E, 75035C43BBDC14AF** at `JobWorkerCount` 1 and 11; tl kind-tick managed runs 0. No tl change. |

The NorthStar2 leg exchanges the three effect columns between entities and the sv arrays on the main thread
(test glue); it proves tl.unity kinds + Unity sv core = .NET bit for bit. The adapter's own fold job is
pinned separately by the fold scenario above.

## Bench

Settings: Unity 7000.0.0a16 batch, CoreCLR 10.0.10, 11 workers, Burst on, safety checks, job debugger
and leak detection off (logged per process), every `SvProbe` and stand-in probe managed-run count 0.
3 fresh processes (not 5, usage limits), 32 warm ticks, 6 rounds x 32 ticks, round 0 dropped; median
of process medians, total range shown. A fresh world per cell (see the Board churn finding). Iw = 2
(hp with stat ceiling, inbox and a 30/40 % condition; mana with constant bounds), digest every 8 ticks
(the digest column is the per-tick average), M rate = 0.05 x W x T x 2/300 StatOps per tick on the
main thread, ~316 B entities (288 B filler), 48 per chunk. sv bits at merge 66dd851 + 02a5c4b.
*kinds = a Burst stand-in kind job (5 connections per timeline: W delta accumulates combined per row,
5 - W reads, cross-row writes through `SvInboxWriter.Send`), NOT tl's generated job; the real tl job
is exercised by the hook test only. "sv total" = sv-main + scatter + fold + derive + digest.
main B = `GC.GetAllocatedBytesForCurrentThread` delta over the 6 timed rounds (192 ticks); all B =
process-wide `GC.GetTotalAllocatedBytes(true)` over the same window, also nonzero in the kinds-only
null window, so unattributed.

| shape | W | cross | window | n | sv-main (apply/recalc) | scatter | fold | kinds* | derive | digest | sv total | total [range] | main B | all B | probes |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| P1 | 2.5 | 0% | null(kinds-only) | 3 | 0.000 (0.00/0.00) | 0.000 | 0.000 | 2.131 | 0.000 | 0.000 | 0.000 | 2.131 [2.120-2.161] | 0 | 0 | ok |
| P1 | 1.0 | 0% | sv+kinds | 3 | 0.100 (0.06/0.03) | 0.037 | 2.150 | 2.102 | 0.013 | 0.104 | 2.404 | 4.511 [4.507-4.567] | 0 | 856 | ok |
| P1 | 1.0 | 10% | sv+kinds | 3 | 0.104 (0.06/0.03) | 0.038 | 2.224 | 2.481 | 0.030 | 0.109 | 2.505 | 4.980 [4.961-5.021] | 0 | 856 | ok |
| P1 | 1.0 | 50% | sv+kinds | 3 | 0.098 (0.05/0.03) | 0.038 | 2.058 | 3.275 | 0.043 | 0.114 | 2.351 | 5.630 [5.575-5.650] | 0 | 0 | ok |
| P1 | 2.5 | 0% | sv+kinds | 3 | 0.201 (0.11/0.06) | 0.067 | 2.138 | 2.164 | 0.013 | 0.108 | 2.527 | 4.699 [4.669-4.719] | 0 | 0 | ok |
| P1 | 2.5 | 10% | sv+kinds | 3 | 0.203 (0.11/0.06) | 0.067 | 2.227 | 2.756 | 0.035 | 0.111 | 2.643 | 5.394 [5.387-5.508] | 0 | 0 | ok |
| P1 | 2.5 | 10%-hot1 | sv+kinds | 3 | 0.201 (0.11/0.06) | 0.069 | 2.200 | 3.850 | 0.029 | 0.106 | 2.605 | 6.475 [6.420-6.519] | 0 | 0 | ok |
| P1 | 2.5 | 50% | sv+kinds | 3 | 0.204 (0.11/0.06) | 0.071 | 2.108 | 3.970 | 0.048 | 0.117 | 2.548 | 6.519 [6.493-6.559] | 0 | 200 | ok |
| P1 | 4.0 | 0% | sv+kinds | 3 | 0.301 (0.16/0.09) | 0.096 | 2.148 | 2.180 | 0.012 | 0.109 | 2.666 | 4.849 [4.836-4.945] | 0 | 0 | ok |
| P1 | 4.0 | 10% | sv+kinds | 3 | 0.306 (0.18/0.09) | 0.101 | 2.243 | 3.054 | 0.036 | 0.110 | 2.796 | 5.850 [5.817-5.871] | 0 | 0 | ok |
| P1 | 4.0 | 50% | sv+kinds | 3 | 0.307 (0.17/0.09) | 0.103 | 2.157 | 4.327 | 0.047 | 0.114 | 2.728 | 7.078 [7.038-7.136] | 0 | 0 | ok |
| P2 | 2.5 | 0% | null(kinds-only) | 3 | 0.000 (0.00/0.00) | 0.000 | 0.000 | 1.293 | 0.000 | 0.000 | 0.000 | 1.293 [1.290-1.296] | 0 | 11456 | ok |
| P2 | 1.0 | 0% | sv+kinds | 3 | 0.085 (0.05/0.02) | 0.026 | 0.310 | 1.441 | 0.010 | 0.024 | 0.455 | 1.891 [1.882-1.905] | 0 | 0 | ok |
| P2 | 1.0 | 10% | sv+kinds | 3 | 0.086 (0.05/0.02) | 0.028 | 0.435 | 1.656 | 0.017 | 0.028 | 0.594 | 2.262 [2.245-2.285] | 0 | 0 | ok |
| P2 | 1.0 | 50% | sv+kinds | 3 | 0.086 (0.05/0.02) | 0.027 | 0.493 | 2.336 | 0.020 | 0.029 | 0.655 | 3.008 [2.991-3.021] | 0 | 0 | ok |
| P2 | 2.5 | 0% | sv+kinds | 3 | 0.185 (0.11/0.05) | 0.057 | 0.449 | 1.311 | 0.010 | 0.026 | 0.727 | 2.050 [2.027-2.051] | 0 | 0 | ok |
| P2 | 2.5 | 10% | sv+kinds | 3 | 0.198 (0.11/0.05) | 0.060 | 0.523 | 1.828 | 0.018 | 0.028 | 0.827 | 2.658 [2.627-2.670] | 0 | 0 | ok |
| P2 | 2.5 | 50% | sv+kinds | 3 | 0.192 (0.11/0.05) | 0.056 | 0.522 | 2.780 | 0.025 | 0.030 | 0.825 | 3.617 [3.600-3.637] | 0 | 0 | ok |
| P2 | 4.0 | 0% | sv+kinds | 3 | 0.294 (0.17/0.09) | 0.083 | 0.331 | 1.424 | 0.010 | 0.025 | 0.743 | 2.193 [2.148-2.196] | 0 | 0 | ok |
| P2 | 4.0 | 10% | sv+kinds | 3 | 0.294 (0.17/0.08) | 0.080 | 0.488 | 2.111 | 0.018 | 0.029 | 0.909 | 3.032 [3.000-3.046] | 0 | 0 | ok |
| P2 | 4.0 | 50% | sv+kinds | 3 | 0.300 (0.17/0.09) | 0.081 | 0.518 | 3.398 | 0.025 | 0.029 | 0.953 | 4.368 [4.315-4.390] | 0 | 0 | ok |
| P3 | 2.5 | 0% | null(kinds-only) | 3 | 0.000 (0.00/0.00) | 0.000 | 0.000 | 1.670 | 0.000 | 0.000 | 0.000 | 1.670 [1.653-1.694] | 0 | 0 | ok |
| P3 | 1.0 | 0% | sv+kinds | 3 | 0.280 (0.17/0.09) | 0.023 | 0.035 | 1.844 | 0.007 | 0.003 | 0.348 | 2.178 [2.138-2.214] | 0 | 11280 | ok |
| P3 | 1.0 | 10% | sv+kinds | 3 | 0.259 (0.15/0.09) | 0.022 | 0.041 | 2.083 | 0.008 | 0.003 | 0.333 | 2.401 [2.366-2.412] | 0 | 11456 | ok |
| P3 | 1.0 | 50% | sv+kinds | 3 | 0.286 (0.17/0.09) | 0.025 | 0.042 | 2.953 | 0.009 | 0.004 | 0.366 | 3.303 [3.275-3.343] | 0 | 0 | ok |
| P3 | 2.5 | 0% | sv+kinds | 3 | 1.062 (0.58/0.46) | 0.035 | 0.083 | 1.681 | 0.039 | 0.004 | 1.223 | 2.900 [2.838-2.925] | 0 | 0 | ok |
| P3 | 2.5 | 10% | sv+kinds | 3 | 1.020 (0.61/0.39) | 0.036 | 0.083 | 2.271 | 0.038 | 0.004 | 1.181 | 3.470 [3.427-3.483] | 0 | 0 | ok |
| P3 | 2.5 | 50% | sv+kinds | 3 | 0.944 (0.44/0.43) | 0.030 | 0.081 | 3.687 | 0.035 | 0.004 | 1.094 | 4.728 [4.669-4.834] | 0 | 0 | ok |
| P3 | 4.0 | 0% | sv+kinds | 3 | 2.169 (1.10/1.03) | 0.043 | 0.041 | 1.815 | 0.007 | 0.003 | 2.263 | 4.047 [3.896-4.170] | 0 | 0 | ok |
| P3 | 4.0 | 10% | sv+kinds | 3 | 2.064 (1.13/0.82) | 0.049 | 0.048 | 2.681 | 0.010 | 0.004 | 2.175 | 4.852 [4.653-4.979] | 0 | 0 | ok |
| P3 | 4.0 | 50% | sv+kinds | 3 | 1.630 (0.77/0.81) | 0.038 | 0.048 | 4.486 | 0.011 | 0.003 | 1.730 | 6.230 [6.195-6.277] | 0 | 0 | ok |

- Gate (sv <= 8.3 ms per tick, cross-row <= 10 %): met on every cell; worst sv total 2.80 ms (P1, W 4,
  10 %). 60 Hz (4.2 ms): sv alone passes every cell; sv + stand-in kinds passes P2 and P3 at <= 10 %.
- P1 fold with the condition lane 2.06-2.24 ms (plan projection 1.6; V7 kill line 3.5 not tripped).
  Without conditions 1.60 ms (`-svNoCondition 1`). The general `Fold.Word` loop (per-row null tests of
  the bound pointers) ran at 3.8 ms; per-strategy loops in the job (stat ceiling + constant floor,
  constant both) gave 1.6 ms with identical bits (law test, twin pin, fold pin all still equal).
- Hot target (all 10 % cross-row writes to slot 0, P1 W 2.5): kinds 3.85 ms vs 2.76 ms random,
  total 6.48 ms: the serialised-atomic limit, documented, no fallback built.
- Setup at P1 (world, 1M entities, 1M `Bind`, first frame): 125-155 ms; board 13.2 MB at 1M rows.


## Known limits and findings

- All inbox sends to one slot serialize (V0: ~10 ns per send). Shipped as a documented limit
  (manager decision); the per-worker fallback is not built.
- Core finding: `Board` memory and Apply/Recalculate time grew under add/remove churn; agent D fixed
  it (SetTable free list, 593da64, merged). The bench still builds a fresh world per cell.
- Board.BindSink does not fill pre-existing rows; the adapter binds sinks before any row exists.
