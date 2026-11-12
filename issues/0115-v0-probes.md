# Issue 0115 - V0 probes (c, d, e, g, h): Unity 7 facts for plan-sv

Agent C (sv-unity), branch `proto/c-unity` from sv main a0963c8. Probes only, no engine change.

## Settings (every Unity number)

- Unity 7000.0.0a16 editor, batch (`-batchmode -nographics -quit -executeMethod`), CoreCLR 10.0.10,
  `-job-worker-count 11` (JobWorkerCount read back 11). Packages resolve to the built-in
  **Entities 7.0.0, Collections 7.0.0, Burst 2.0.0** (a 6.7.0 manifest pin is overridden; the plan's
  "Entities 6.7.0" is 7.0.0 on this editor).
- Burst on, `EnableBurstSafetyChecks=false`, `JobDebuggerEnabled=false`, `NativeLeakDetection=Disabled`,
  logged per process. Every job calls a `[BurstDiscard]` probe; every line below read `burstProbe=1`.
  Jobs use `CompileSynchronously = true`, so the first launch is already Burst.
- AMD Ryzen 5 8500G (6C/12T), 31 GB. Untracked harness `D:/GitHub/_work/sv-proto/c-harness`
  (fresh `-createProject` + the three packages); sources in `tests/Sv.Editor/v0`, driven by
  `tests/Sv.Editor/v0/run-v0.sh`, locks via bench.lock then unity.lock. 0 error CS, 0 warning CS,
  0 error BC in every compile and run log.
- Perf: 3 fresh processes; per process 6 rounds of 32 ticks (4 for 1M scatter), round 0 dropped,
  median of rounds; the table gives the median of the 3 process medians and the process range.
  Each tick = `SystemHandle.Update` that schedules and completes the job.
- Archetype: SvSlot, 2 intrinsics (int value, int delta, float stat ceiling each), one enableable
  tag, a 288 B filler: ~316 B per entity, 48 per chunk (close to tl's 320 B / 49).
- Orphaned asset-import workers of another project (`probe-clean`, started 2026-11-06) were alive and
  idle during all runs; not mine, not touched.

## (e) tl.unity facts (read from origin/main aeb87e9, generator `Tools~/Tl.Gen.Unity`)

| Fact | Answer | Evidence |
|---|---|---|
| B3: one `[TlState]` struct = one component type per world | **True** | `UnityStateEmitter` adds `IComponentData` to the partial struct itself; the auto system's columns are `Direct` (`ComponentName` returns the element type). Kinds from other assemblies feed the same type |
| Caveat to B3 | legacy `[TimelineSystem]` `Span<T>` columns get a per-system wrapper `<System>_<Column>Data { T Value }` (e.g. `Vitals_HealthData`), so the same plain struct in two declared systems is two component types | `UnityKindJobEmitter.ComponentName`, `UnitySystemReader` (Direct=false). sv columns must be `[TlState]` / zero-config |
| RW pointers only for live chunks | Yes. The job returns before any column access unless some kind has `Active & Enabled` bits in the chunk (or the chunk has a `TimelineEvent` buffer). Then it takes `GetRequiredComponentDataPtrRW` for **every writable column** of that chunk and RO for read-only ones, regardless of which kind is live | `TlUnity.Movement.g.cs` prologue |
| Does that bump the chunk change version | **Yes, even without a write** (probe below). So the fold's DidChange filter sees every writable column of every live chunk: false positives, never misses |
| Apply stores or accumulates | Neither by itself: entries are combined with the kind's `Combine` in registers, then `Kind.Apply(in total, ref column[row])` runs once per (row, kind) that had an entry; the user body decides `=` or `+=`. Rows without entries get no call (only an optional `Idle`) | `TlUnity.Vitals.g.cs` lines 115-118; `AutoKinds.cs` |
| Kind-to-column metadata baked | **Not in the baked library** (`game.tlib` kind table: name, value/entry bytes, capacity, accepts, mode, order). It exists at runtime in generated code: `KindRegistry` `KindDescriptor.Columns` (`Type[]`, no access mode) | `Core/KindSection.cs`, `TlUnity.Kinds.g.cs` `Register` |

**B10, API names that compiled and ran (Entities 7.0.0):** `state.GetComponentLookup<T>(bool readOnly)`,
`lookup.Update(ref state)`, `lookup[entity] = value`, `lookup.SetComponentEnabled(entity, bool)`,
`chunk.SetComponentEnabled(ref handle, index, bool)`, `chunk.GetEnabledMask(ref handle)[index] = bool`,
`chunk.GetEnableableBits(ref handle)` (v128), `chunk.DidChange(ref handle, uint version)`,
`chunk.GetRequiredComponentDataPtrRW/RO(ref handle)`, `EntityManager.GetComponentTypeHandle<T>(bool)`
plus `chunk.GetComponentDataPtrRW` on the main thread, `world.Unmanaged.GetUnsafeSystemRef<T>(handle)`,
`SystemHandle.Update(WorldUnmanaged)`.

**Change-version probe** (100,000 rows, 2,084 chunks, fold filtered on delta and stat versions, writer then fold each tick; `receipt/h-e-first.txt`):

| Tick | Writer | fold chunks visited | flag DidChange | stat DidChange |
|---|---|---:|---:|---:|
| 1 | none (creation) | 2084 | 2084 | 2084 |
| 2, 3 | none | 0 | 0 | 0 |
| 4 | `GetRequiredComponentDataPtrRW` on even chunks, no write | 1042 | 0 | 0 |
| 6 | `GetRequiredComponentDataPtrRO` on all | 0 | 0 | 0 |
| 8 | `ComponentLookup` write of 100 stats | 3 | 0 | 3 |
| 10 | `lookup.SetComponentEnabled` x100 | 0 | 3 | 0 |
| 12 | `chunk.SetComponentEnabled`, 1/4 of chunks | 0 | 521 | 0 |
| 14 | `EnabledMask[i] =`, 1/4 of chunks | 0 | 521 | 0 |

The fold's own RW access never makes its next tick see changes (ticks 2-3, and every "none" tick).
Enable-bit writes bump only the enableable component's version. A lookup write bumps only the
written chunks (the scatter -> fold re-clamp path works).

## (c) Touch and fold over the fold's bytes

| Rows | Job | ms median [process range] | Note |
|---|---|---|---|
| 1M | touch, RO, 6 columns (24 B/row) | 0.765 [0.757-0.784] | 33 GB/s: B5 holds |
| 1M | fold Iw=2, all chunks | 1.383 [1.374-1.389] | plan projected 1.6 |
| 1M | fold Iw=1, all chunks | 0.794 [0.779-0.839] | |
| 1M | fold Iw=2, DidChange filter, all changed | 1.367 [1.352-1.376] | filter costs nothing |
| 1M | fold Iw=2, DidChange filter, none changed | 0.116 [0.104-0.121] | 21,846 chunks, ~5 ns/chunk |
| 1M | delta writer (stand-in kind), all rows | 0.628 [0.618-0.628] | |
| 4M | touch | 3.311 [3.223-3.674] | 29-31 GB/s |
| 4M | fold Iw=2, all chunks | 5.739 [5.705-5.859] | |
| 4M | fold Iw=1, all chunks | 3.275 [3.273-3.823] | |
| 4M | fold Iw=2, filter, all changed | 5.743 [5.655-5.760] | |
| 4M | fold Iw=2, filter, none changed | 0.394 [0.333-0.398] | |

V7 kill rule (fold above 3.5 ms at P1, Iw=2): not tripped (1.38 ms).

## (d) Component-lookup write (scatter), 1M-row world, one Burst `IJob`

| Writes | entity order ms | random order ms |
|---:|---|---|
| 128 | 0.0044 [0.0034-0.0054] | 0.0045 [0.0042-0.0052] |
| 10,000 | 0.0848 [0.0829-0.0866] (8.5 ns/write) | 0.2451 [0.2451-0.2823] (25 ns/write) |
| 1,048,576 | 10.25 [9.59-11.06] (10 ns/write) | 46.19 [43.49-46.71] (44 ns/write) |

128 writes cost ~4 us, mostly schedule + complete. Plan kill rule (scatter above 0.5 ms at 10,000
writes): not tripped. Sorting the sink by slot helps only while slot order follows chunk order.

## (g) `Interlocked.Add` on `long*` in Burst (`IJobParallelFor`, batch 4096, 1M-slot inbox, 64 B aligned)

| Sends | atomic, random targets | plain `+=`, random (racy baseline) | atomic, one hot target |
|---:|---|---|---|
| 500,000 | 0.381 [0.378-0.393] | 0.380 [0.378-0.417] | 5.22 [5.17-5.24] |
| 1,048,576 | 0.754 [0.751-0.774] | 0.750 [0.749-0.757] | 10.97 [10.87-11.00] |
| 2,500,000 | 1.732 [1.723-1.786] | 1.733 [1.720-1.742] | 26.03 [25.98-26.22] |

Atomic sums are exact every time (the racy baseline loses adds at 2.5M). On random targets the
atomic costs nothing over a plain random read-modify-write: 0.7 ns wall per send (plan: 1.6). The
one-hot case is ~10.4 ns per send, serial. **V8 kill rule trips** for the adversarial one-target case
(above 3 ms already at 500k sends = 10 % cross-row at P1): plan step V8 must take its fallback
(per-worker partial sums partitioned by slot range, or a per-worker pre-sum of hot targets).

## (h) Stat-fold float bits, 1,048,576 random sets

Source: `tests/Sv.Editor/v0/StatFoldSets.cs`, a verbatim copy of `Board.Fold` over an equal entry
struct; sets from SplitMix64 bit patterns (0-8 entries, all 4 kinds, wide exponents, NaN, inf, zero,
denormal). Canonical = NaN mapped to 0x7FC00000 (sv's `CanonicalNaN`). Receipts:
`receipt/statfold-hosts.txt`, `receipt/h-e-first.txt`; rerun with `run-hosts.sh` and `run-v0.sh`.

| Host | canonical digest | raw digest |
|---|---|---|
| .NET 10 Release | **1420F6ADDA0DE4CF** | B92436E15A0DE4CF |
| .NET 10, `DOTNET_EnableHWIntrinsic=0` | 1420F6ADDA0DE4CF | B92436E15A0DE4CF |
| Unity 7 editor managed (CoreCLR 10.0.10) | 1420F6ADDA0DE4CF | B92436E15A0DE4CF |
| Burst 2.0.0 `FloatMode.Strict` | 1420F6ADDA0DE4CF | 9FCACAC45A0DE4CF |
| Burst `FloatMode.Default` | 1420F6ADDA0DE4CF | 9FCACAC45A0DE4CF |
| Burst `FloatMode.Fast` (not used by sv) | C10061A6FC71A8AA | 0C44787F7C71A8AA |
| **Mono 6.13** (Unity MonoBleedingEdge JIT; also `-O=-all`, `-O=float32`, `--llvm`) | **703F5466FFA87BB8** | 5FF874FA7FA87BB8 |
| Mono 6.13, same fold with `(float)(...)` on each add/multiply | 1420F6ADDA0DE4CF | 5B4645875A0DE4CF |

- **Mono differs** in 62,328 sets (5.9 %) by 1-2 ulp; .NET equals a strict IEEE float32 reference
  (`statfold_ref.py`, 3000/3000; Mono 2819/3000). Reported as stop-and-report; the manager chose
  option (a): explicit `(float)` casts in `StatMath` (agent B), Burst function pointer as fallback.
  Check B's fix against canonical **1420F6ADDA0DE4CF** on Mono.
- Burst vs .NET raw differences are 6 NaN results of opposite sign bit only.
- `mono --interpreter` segfaults on this program (no result).
- The Unity 7 editor itself is CoreCLR; Mono matters only for the Mono2x player backend.

## Not done / unverified

- (e) change-version behaviour was probed with hand-written jobs using the same Entities calls the
  tl generated job makes, not by running a tl kind job (that needs a baked library; V6 does it).
- 3 processes per arm, not 5; no ABAB (no A/B arms in V0).
- Mono numbers come from the editor's bundled `mono.exe`, not a Mono2x player build.
