# sv v3 benchmark report (BM1, 2026-11-13)

Branch `v3/bench` from `v3/int` b1e5a00. Ryzen 5 8500G (6 cores, 12 threads), Windows 11, .NET 10.0.401, Release, no debugger,
nothing else on the CPU, Vector256 on. Every frame number is the median of 5 processes, each the median of 50 measured frames after
64 warm frames; spread is min..max of the 5 medians. Run: `dotnet benchmarks/Sv.Bench/bin/Release/net10.0/Sv.Bench.dll <entry> --ms 1000`.

## Set-up

- Pack: `packs/lego` (the 22 mechanics in one pack: 55 rules, 44 value lanes, 28 channels, 3 events, 5 cards), compiled by `sv pack`
  at build time into `tests/Sv.Kernels.Lego` and driven through `FrameLoop<Sv.Kernels.Lego.HostKernels>` (generated kernels, not the
  stub `BenchKernels` of `V3Bench.cs`).
- World: 1,000,000 entities, 15,625 chunks; chunk templates cycle through 20 slots (8 dummy, 2 shielded, 2 vampire, 2 thorny,
  regen_low, ward_charges, masked, crit_target, bloodmage, worked_example_player), so cards, absorb, thorns and lifesteal all run.
- Workers 11 (main thread + 10), 64 parts, shared lanes, ring 32 epochs x 2 frames, digest every frame (as default).
- Collect = 11 external writers sending in parallel from precomputed lists (`send`) + the host's active-chunk collect.
- Scenarios (records sent per frame; canonical count includes derived records from thorns, lifesteal, regen):
  A 100,000 random records into every 10th chunk (canonical 104,881, 3,052 active chunks); B 1,000,000 Tick records, one per
  entity, pre-bucketed; C B + 1,000,000 random cross-entity records (2,165,758); D 1,000,000 Tick + 500,000 Poison pre-bucketed +
  1,500,000 random (3,248,724). Random = Physical 50 %, Healing 25 %, Fire, Poison; amount 1-8, hits 1-2, source random, kind of source.
- Phase times come from `FrameLoop.Marks` (commit 4372554: ten timestamps per Step, no other change).

## Gate status (2026-11-14, decision D20)

The 8.3 ms gate covers chunk-bucketed load (records from timelines and Collect): A 2.4-2.8 ms and B 7.7-7.8 ms pass. Scenario r (1M uniformly random cross-entity records) is a stress benchmark with a no-regression ceiling of 19.0 ms (median of 5 x 50 frames); it is 17.6-18.1 ms on a quiet box. C and D are reported only. See plan.md D20 and issue 0374.

## Update P3 (2026-11-14, agent worktree branch from v3/m8-unity 6c219e2, merged into v3/m8-unity 94311ca)

Ryzen 5 8500G, Linux 7.2, .NET SDK 10.0.403, Release, 11 workers, 64 parts, digest every frame. Every number is the median of
5 processes x 50 frames (9 where marked), spread = min..max of the medians, measured interleaved (base, new, base, new, ...) in one
session under the bench lock. Other agents loaded the box during part of the work (load average up to 10); the final table was
taken at load 3-9 and every comparison below is same-session. Digests unchanged everywhere (r 9CFD52083A9DA3FB, A DAF3A1D9BD73BA10,
B 6E7618BF0900D3F8, C D3C714D46555B7CE, D 7413F619472566C4, worked example E0F445186E2C3672, PackHash reference BD70627B1C671888,
lego 9A043301EA06000E, NorthStar3 1456B551FFCFA7CF); 0 B allocated on the main thread in every run.

Gate case: scenario r, 1,000,000 random cross-entity records per frame (1,165,758 canonical). Final table, 6c219e2 against
f8b53af in the same session (ms):

| Scenario | | Send | Seal | Log | Decide | Commit | Events | Digest | End | **Total** | Spread |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| r | before | 0.76 | 4.64 | 1.19 | 10.18 | 1.23 | 0.15 | 2.32 | 0.26 | **20.88** | 20.73-21.01 |
| r | after | 0.77 | 4.28 | 1.22 | 8.42 | 1.29 | 0.15 | 1.13 | 0.27 | **17.64** (-15.5 %) | 17.59-17.81 |
| A | before | 0.09 | 0.73 | 0.15 | 1.00 | 0.16 | 0.06 | 0.47 | 0.05 | **2.78** | 2.71-2.83 |
| A | after | 0.10 | 0.70 | 0.15 | 0.84 | 0.16 | 0.06 | 0.23 | 0.05 | **2.36** (-15 %) | 2.31-3.35 |
| B | before | 0.76 | 2.05 | 1.12 | 3.13 | 0.21 | 0.13 | 0.14 | 0.04 | **7.66** (9 runs) | 7.54-7.75 |
| B | after | 0.76 | 2.10 | 1.11 | 3.15 | 0.20 | 0.13 | 0.13 | 0.04 | **7.76** (9 runs) | 7.59-7.91 |
| C | before | 1.51 | 7.86 | 1.87 | 13.11 | 1.24 | 0.14 | 2.24 | 0.26 | **28.45** | 28.01-28.47 |
| C | after | 1.59 | 7.13 | 1.86 | 11.41 | 1.28 | 0.14 | 1.12 | 0.26 | **24.91** (-12.4 %) | 24.86-25.09 |
| D | before | 2.33 | 12.09 | 2.53 | 18.73 | 1.22 | 0.18 | 2.83 | 0.35 | **40.32** | 40.23-40.48 |
| D | after | 2.33 | 11.12 | 2.54 | 16.36 | 1.26 | 0.17 | 1.28 | 0.34 | **35.45** (-12.1 %) | 35.38-35.64 |

B is unchanged within noise: a second 7-run session read 7.70 before and 7.68 after (the 9-run session above read +0.10 ms).
B no longer builds the source table (no record has a source).

**Gate verdict: r is not met.** 17.64 ms against 8.3 ms (2.1x over). B still passes (7.7 ms), A passes, C and D do not. What is
left and why it is structural is in issue 0374: about 120 MB of record traffic per frame across Send, Seal (Count, Scatter, Order),
the input log and Decide (estimate, about 4 ms of bandwidth), Decide at about 40 ns of core time per record (per-target scalar
code, branch bound), and Commit plus digest over every chunk because r touches all 15,625. Reaching 8.3 ms needs design changes
(Seal and log examine fused per chunk, a batched per-channel Decide or a per-chunk M12a proof, an incremental digest), not trims.

How it was found: `perf record` with `DOTNET_PerfMapEnabled=1 DOTNET_EnableWriteXorExecute=0`, jitdump and `perf inject --jit`
for per-instruction annotation, absolute sample counts per phase group, temporary Stopwatch probes for the Seal steps and the
new Sources phase. First profile of r: Decide 45 % of CPU samples, Seal 20 %, digest 10 % (half of it in one 32-step bit loop),
Commit 6 %, log 5 %, send 3 %. In `Target<Lone0>` 18 % and in `Target<Mixed>` 13 % of samples sat on the source chunk's lane-table
entry and meta word.

Kept (each its own commit, every suite green, before -> after against the previous commit in the same session, ms):

| Commit | Change | r | C | D | A | B |
|---|---|---|---|---|---|---|
| 5979b94 | digest: event cells read only the event words a lane uses (`LaneMath.UsedWords`, `RowLevels(lane, row, usedWords)`) | 21.17 -> 20.59 (digest 2.34 -> 1.16) | 28.87 -> 28.42 (digest 2.28 -> 1.15) | 41.09 -> 40.62 | 2.92 -> 2.71 | 8.27 -> 8.11 |
| fd1c108 | decide: cross-entity reads through a per-frame source-chunk table (new `Sources` phase, 0.17 ms; skipped without sourced records) | 19.58 -> 18.58 (decide 10.13 -> 8.96) | 27.16 -> 26.29 | 38.86 -> 37.35 | 2.56 -> 2.42 | 7.70 -> 7.75 (noise) |
| 03edc9e | host: a worker thread that starts late no longer skips the first Run (issue 0372, a hang, no perf effect) | - | - | - | - | - |
| 77b0129 | seal: Order returns at the first descent and merges each row as it sorts it (order step r 2.96 -> 2.48, C 4.62 -> 3.81) | 18.57 -> 18.25 (seal 4.66 -> 4.32) | 26.22 -> 25.52 | 37.39 -> 36.48 | 2.43 -> 2.42 | 7.71 -> 7.70 |
| f8b53af | decide: an on-template source read is one load from the template's value row | 18.14 -> 17.56 (9 runs; decide 8.92 -> 8.39) | 25.43 -> 24.89 | 36.55 -> 35.47 | 2.43 -> 2.34 | 7.75 -> 7.74 |

77b0129 is under 3 % on r total; it was kept under the 15 % rule for the step it targets (Seal order step -16 % on r, -18 % on C,
probes) with no other phase moving.

Tried and reverted (same A/B method):
- Two-stage software prefetch of the next chunks' source lane-table rows and meta lines from `Phases.Decide`: Target samples
  -1.8 points, warm loops +3.4 points; r decide 10.36 -> 10.66 ms (net loss).
- Prefetch of the next chunk's source-table entries from `Phases.Decide` (with the table): r decide 9.76 -> 10.05 ms.
- Prefetch of meta lines while building the source table, and a Vector256 compare of the lane-table row against the template row:
  build stayed at 0.17-0.19 ms (bandwidth-bound), reverted for simplicity.
- Prefetch of the segments two chunks ahead in Seal Order: r seal 4.31 -> 4.36, total 18.26 -> 18.35.
- Count and Scatter on 2 x workers parts (22) instead of 64, Order still 64: A 2.42 -> 2.19, B 7.70 -> 7.49, but r 18.25 -> 18.47,
  C 25.52 -> 25.86, D 36.48 -> 36.81 (issue 0373).
- Shape checking for a descent every 8 records (branch-free inside a block): B 7.68 -> 7.76, r 17.65 -> 17.75.

Not tried (from the brief): carrying source values in the record (contract change; the table removes most of the miss without
it); M12a delta scratch (no lego channel qualifies pack-wide, `immune_by_kind_mask` tells every channel apart by kind); M12b run
splitting (r has about 1.2 records per target, no hot targets); radix by chunk on the writer side (needs a raw-arena layout change).

Suites at every kept commit: Laws 569 (556 + 5 `RowLevels` cases + 8 source-table cases), Scenario 80, Diff 20, Lego 120, Host 29
under default, `DOTNET_EnableAVX2=0` and `DOTNET_EnableHWIntrinsic=0`; Twin.Fixtures 32, NorthStar3 18, NorthStar2 13, NorthStar 27,
Web 6, Portable 390; Sv.Core, Sv.Rules, Sv.Host build with 0 warnings on netstandard2.1 and net10.0; `eng/engine-sync.sh check`
passes. The allocation law `InputLogCapLaws.PerFrameByteCapBoundsBlocksPerFrameAndAllocatesNothingWarm` failed once in the first
HWIntrinsic=0 Laws run of 2 of 5 full sequences on a loaded box and passed on every isolated rerun, also on pristine 6c219e2
(issue 0371). The Unity test packs were regenerated with `sv pack --host unity` after the emitter change in fd1c108 (each gets a
`SourcesJob`; PackHashes unchanged). Unity/Burst was not run.

## Update P2 (2026-11-13, branch v3/p2-perf from v3/m8-unity 1aa315e)

Ryzen 5 8500G, Linux 7.2, .NET SDK 10.0.403, Release, 11 workers, 64 parts, digest every frame. Every number is the median of
5 processes x 50 frames; spread is min..max of the 5 medians. Every comparison ran interleaved (base, new, base, new, ...) in one
session against a pristine build of the previous commit, because the CPU drifts 0.3-0.5 ms between sessions. Digests unchanged
everywhere (A DAF3A1D9BD73BA10, B 6E7618BF0900D3F8, C D3C714D46555B7CE, D 7413F619472566C4, worked example E0F445186E2C3672);
0 B allocated on the main thread in every run.

Final table, 1aa315e against 152bee6 in the same session (ms):

| Scenario | Send | Seal | Log | Decide | Commit | Events | Digest | **Total** | Before P2 (1aa315e) | What limits it now |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| A | 0.10 | 0.76 | 0.15 | 1.02 | 0.17 | 0.06 | 0.50 | **2.82** (2.80-3.18) | 3.23 (3.19-3.24) | Decide and Seal fixed cost per chunk; digest's per-lane template compare over all 15,625 chunks |
| B | 0.74 | 2.09 | 1.12 | 3.14 | 0.20 | 0.13 | 0.14 | **7.69** (7.62-7.76) | 9.68 (9.58-9.84) | Decide per target (12.8 ns per single-Tick target L1-hot); Seal and Log stream the 16 MB of records three more times and are bandwidth-bound |
| C | 1.52 | 7.89 | 1.87 | 13.20 | 1.24 | 0.14 | 2.28 | **28.53** (28.09-28.60) | 30.63 (30.54-30.75) | Decide on mixed-channel targets (cross-entity reads), Seal sort of random records (order 4.7, scatter 2.3), digest of changed cells |
| D | 2.50 | 12.33 | 2.54 | 19.06 | 1.27 | 0.17 | 2.88 | **41.23** (40.52-46.11) | 43.04 (42.52-56.34) | as C, 3.2M records |

Gate (B <= 8.3 ms): met in this harness, 7.69 ms, worst of the 5 medians 7.76 ms. A passes. C and D do not.

How it was found: `perf record` with `DOTNET_PerfMapEnabled=1` and `DOTNET_EnableWriteXorExecute=0` (with W^X on, JIT code runs
from `memfd:doublemapper` and perf shows no symbols), jitdump plus `perf inject --jit` for per-instruction annotation,
`DOTNET_JitDisasm` for Tier1 code, temporary Stopwatch probes for the Seal steps. First profile of B: `Kernels.Events` was 10.5 %
of samples; a measurement-only build that skipped Events took B commit 1.23 -> 0.12 ms, so Events cost about 1.1 ms (every Tick
touches all 1M rows, and the low-health event ran four inlined saturating multiplies per row). Decide is compute-bound (the
in-frame cost is the L1-hot `v3-lego-decide-shape` cost over about 4.6 workers); Seal, Log and Send are bandwidth-bound.

Kept (each its own commit, every suite green, before -> after against the previous commit, ms):

| Commit | Change | B | C | D | A |
|---|---|---|---|---|---|
| da8f278 | `Below` with operands provably within 2^31 compiles to plain 64-bit products | 11.14 -> 10.40 (commit 1.41 -> 0.67) | 32.42 -> 31.97 | 45.09 -> 43.62 | 3.44 -> 3.39 |
| 3ddf93e | event conditions over own-row lanes compile to whole-chunk row masks; new `Mask.Below` (AVX2 + scalar law) | 9.23 -> 8.71 (commit 0.63 -> 0.22) | 32.31 -> 30.73 | 45.82 -> 42.99 | 3.26 -> 3.19 |
| 15560e6 | `Target<Lone{c}>`: a target whose records share one channel runs a copy where present, the gate switch and every presence guard are constants; settle reads the channel once | 9.11 -> 8.23 (decide 4.04 -> 3.22) | 30.05 -> 29.55 | 41.98 -> 41.52 | 3.25 -> 3.23 |
| 152bee6 | digest phase caches each chunk's liveness sum by (live mask, template) | 8.05 -> 7.81 (digest 0.44 -> 0.14) | 28.79 -> 28.51 | 41.12 -> 40.95 | 3.28 -> 2.97 (digest 0.81 -> 0.49) |

`v3-lego-decide-shape` (one chunk, L1-hot), 3ddf93e -> 152bee6: Tick 64x1 1,115 -> 822 ns, 1x64 613 -> 468 ns; Physical 64x1
2,864 -> 2,360 ns, 1x64 1,519 -> 1,310 ns. Tier1 `Target<Lone4>` is 5,745 bytes against 13,251 for the generic Target. The
10k-rule pack emits 14.42 MB of C# (14.40 before), two emissions equal.

Seal split now (means over all frames, temporary probes, not committed), B / C: count 0.79 / 1.55, plan 0.43 / 0.31, scatter
0.72 / 2.26, order 0.47 / 4.67, finish 0.05 / 0.04.

Tried and reverted (same A/B method): input log compare word-wise with Vector256 (B log 1.12 vs 1.14, C 1.89 -> 1.75, totals in
noise: the compare waits on old log blocks, not on instructions); software prefetch of the next chunks' previous log segment
(each 1 KB segment starts its own 4 KB block, so every chunk is a new page for the stream prefetcher; B log 1.12 -> 1.02, total
8.72 -> 8.60, 1.4 %, C unchanged); skipping Seal's order check for chunks that received one repeat-free run of an ordered block
(B seal 2.13 -> 2.01 but decide 3.83 -> 3.94, total 8.77 -> 8.78; C 29.38 -> 29.86, D 41.67 -> 42.15, A 3.21 -> 3.34: the misses
move to Log and Decide and the flag store in the unordered scatter costs); initializing `a.Assign` only before its first use
(B decide 3.42 vs 3.45, C 31.63 -> 31.33, D 41.33 -> 41.35, inside noise); Vector256 mixes for digest lanes with at least 16
changed rows (C 28.53 -> 28.19, digest 2.27 -> 2.08; D 40.37 -> 40.23; under 3 %).

Suites at every kept commit: Laws 556, Scenario 80, Diff 20, Host 29, Lego 120 under default, `DOTNET_EnableAVX2=0` and
`DOTNET_EnableHWIntrinsic=0`, except the two workers:1 "allocate nothing on any thread" Host tests, which read 168 B in some runs
on 1aa315e as well (issue 0371). Twin 32, NorthStar3 17, NorthStar2 13, NorthStar 27, Web 6. `eng/engine-sync.sh check` passes;
the Unity test packs under `packages/com.sv.unity.v4/Tests/Packs` were regenerated with `sv pack --host unity` after each emitter
change (PackHash unchanged). Unity/Burst was not run.

## Update P1 (2026-11-13, branch v3/p1-perf from v3/int 5c81210)

Same set-up, Release, median of 5 processes x 50 frames, digests unchanged (A DAF3A1D9BD73BA10, B 6E7618BF0900D3F8,
C D3C714D46555B7CE, D 7413F619472566C4). The CPU drifts by about 0.3-0.5 ms in B between sessions, so every atom was measured
against a baseline run in the same session.

| Scenario | Send | Seal | Log | Decide | Commit + Events | Digest + End | **Total** | Before P1 (same day) | What limits it now |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| A | 0.08 | 0.74 | 0.14 | 1.03 | 0.28 | 0.81 | **3.1** | 3.17 | digest (whole world each frame), Decide fixed cost per chunk; passes the gate |
| B | 0.69 | 2.05 | 1.08 | 3.98 | 1.32 | 0.46 | **9.6** (9.4-9.8) | 11.21 | Decide per-target fixed cost (about 27 ns per target), Seal count matrix + route + one copy, input log compare |
| C | 1.43 | 7.82 | 1.88 | 13.25 | 2.66 | 2.73 | **30.0** | 31.08 | Decide on cross-entity records, Seal sort of random records (order 4.0 ms, scatter 2.3 ms) |
| D | 2.19 | 12.24 | 2.55 | 18.48 | 2.64 | 3.35 | **41.7** | 43.28 | as C, 3.2M records |

Kept (each its own commit, five suites green): 74b8239 Seal scatters straight into the canonical buffer, merges in place and
compacts (Records to Scratch, then swaps) only when a merge moved a chunk start: Seal B 3.15 -> 2.06, C 9.17 -> 7.95 ms, Log B
1.36 -> 1.02 ms (records stay hot). 915e3cb route leaves a kept record in place: Seal B 2.13 -> 2.03, C 8.14 -> 7.85 ms.
Seal split after both (probes, B / C): count 0.68 / 1.45, plan 0.41 / 0.32, scatter 0.72 / 2.32, order 0.39 / 4.0, compact 0.

Tried and reverted: chunk-local sort buffer in Order (C Seal 7.95 -> 7.80, inside noise); fewer Seal parts (16 / 32 / 64: B Seal
1.90 / 1.91 / 2.04 but C Seal 8.99 / 8.10 / 7.78, totals best at 64); per-frame source table for gates (issue 0370: Decide C
13.0 -> 14.8 ms); a warm pass over each chunk's sources before Decide (Decide C 15.9 ms).

## Frame time at 1M entities, 11 workers, after the fixes (ms)

| Scenario | Collect | Seal | Log | Decide | Commit + Events | Digest + Stats + End | **Total** | Projected total | Gate 8.3 ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| A | 0.13 (0.1) | 0.77 (0.2) | 0.12 (-) | 2.67 (0.2) | 0.55 (0.2) | 0.81 (0.3) | **5.07** (5.02-5.15) | 1.0 | pass |
| B | 0.67 (0.7) | 2.99 (0.3) | 1.29 (-) | 16.37 (1.7) | 2.34 (0.9) | 0.46 (0.6) | **24.21** (23.94-25.07) | 4.2 | fail |
| C | 1.45 (0.7) | 9.15 (2.3) | 1.87 (-) | 31.15 (2.6) | 3.38 (1.1) | 2.66 (0.8) | **49.74** (49.57-50.53) | 7.5 | fail |
| D | 2.20 (0.7) | 14.36 (3.5) | 2.47 (-) | 42.87 (5.0) | 3.36 (1.5) | 3.34 (1.0) | **68.94** (67.94-69.20) | 11.7 | fail (D is over 1M records; plan expected fail) |

Projected values from plan 15.2 in brackets. "Log" is the input log write (plan 9.3), which the plan's cost table leaves out.
Before the fixes the totals were A 6.14, B 28.81, C 57.35, D 77.34 ms (same digests). Digest off (`-nodigest`): A 4.32, B 24.45,
C 50.66, D 68.70 ms. 60 Hz (4.2 ms): no scenario fits. Warm allocation: 0 B on the main thread and 0 B over all threads
(`GC.GetTotalAllocatedBytes`) in every scenario (50 frames x 5 runs, not the 2,000 frames of the M5 gate).
Bench digests (stable over all runs and every fix): A DAF3A1D9BD73BA10, B 6E7618BF0900D3F8, C D3C714D46555B7CE, D 7413F619472566C4.

Scaling (A): 1 worker 25.1 ms, 6 workers 6.1 ms, 11 workers 5.0 ms. B Decide: 1 worker 74.7 ms, 11 workers 16.4 ms (4.5x).
Extra shapes: r (1M random only) total 34.8 ms, Seal 5.0 ms; c-rotate (C with two alternating record sets, no log dedupe) Log 6.0 ms.

## Memory, rewind, compile, stability

| Item | Measured | Target / projection | Verdict |
|---|---|---|---|
| At rest, 1M entities, lego pack | 40.8-45.5 B per entity (process private bytes minus 5 MB of bench arrays); table 2.9 MB (46 lanes per chunk), pool 4.2 MB (15,736 lanes) | <= 200 B per entity | pass |
| Ring, 32 epochs, 10 % chunks active | 159,259 journalled lanes = 40.8 MB (264 B per lane incl. entry); 192,187 = 49.2 MB with rotating records | 40 MB, within 20 % for the measured activity (9.2: 264 B per lane) | pass |
| Total with history | 450 B per entity (same records) and 757 B per entity (3 rotating sets): decide scratch 296 MB, input log 6.6 / 288 MB, pool 81.8 MB | about 210 B per entity (15.3) | over: issues 0361, 0363 |
| Rewind | 0.05-0.82 ms per 10,000 journalled lanes; fixed cost about 0.25 ms (1 epoch back, 6,145 lanes: 0.29-0.50 ms; 16 epochs, 545,608 lanes: 2.6-2.8 ms) | <= 1 ms per 10,000 changed lanes | pass |
| 10,000-rule pack | plan 184-201 ms + emit 186-195 ms in process (median of 5); `sv pack` cold 0.72 s; 13.66 MB of C#; `dotnet build` of the kernels project 9.0 s (8.9-9.3, 3 runs) | under 60 s to under 20 MB; C# compile under 5 min | pass, but the output only builds with `NoWarn CS1522` (issue 0364) |
| `sv pack` output stability | two emissions in one process equal (lego, 10k); two `sv pack` processes byte-identical (lego, 10k); lego CLI output equals the build-time kernels except the namespace | stable | pass |

The 10k pack (`Pack10k.Source`): 200 intrinsics, 40 stats, 60 channels, 200 kinds, 500 cards; rules cycle deny gates (kind and
channel), additive and added Shape gates on self/source cells, self emits and Taken-scaled reflections, each guarded by a card.

## Where the time goes (measured, not guessed)

Per-phase Stopwatch marks for the split above; inside Decide, temporary Stopwatch probes around `kernels.Decide` and `Memory.Clear`
plus the one-chunk microbench `v3-lego-decide-shape`; inside Seal, temporary probes around each Seal step. `dotnet-trace`
thread-time sampling was tried and is wrong for Decide (it puts 97 % in `Buffer.ZeroMemoryInternal`; removing the clear did not make
Decide faster), so no sampler number is used below.

1. **Decide kernel, 64-68 % of B, C, D (issue 0360).** One chunk, L1-hot: about 34 ns fixed per target plus 28 ns per Tick record and
   51 ns per Physical record, even where no rule acts; the cost model says 4-8 ns per record. In the frame at one worker (probes): 69.0 ms
   kernel + 8.9 ms scratch clear per frame = 4.4 us + 0.57 us per chunk. The generated `Target` calls 10 rule methods unconditionally per target and passes
   over the target's records about 17 times; `Gate` is a call per record.
2. **Seal, 12-21 % (issue 0362, M2 kill rule tripped).** 1M pre-bucketed records 3.0 ms (gate 0.4), 1M random 5.0 ms (gate 3).
   Pre-bucketed records still go through route + count, scatter copy, ascending check + merge copy, compact copy; the parts x chunks
   count matrix (4 MB at 64 parts) costs about 0.75 ms per frame whatever the record count (derived: B Seal 2.94 ms at 16 parts
   vs 3.69 ms at 64, before d45b099).
3. **Commit + Events and Digest, 2.3-3.4 ms and 0.4-3.0 ms (2-3x projected).** Commit walks all 18 delta lanes of every active chunk
   (`LaneScan.NonZero`, bounds fill, copy-on-first-write of each written lane per epoch: one worker 10.5 ms in B). Digest recomputes the
   whole world every frame (one liveness mix per live row plus every cell that differs from its template); plan 15.1 assumes 2 mixes
   per changed cell. `DigestEvery` is the existing knob; the incremental digest is not built.

Memory (issue 0361): Decide scratch is 18,944 B per active chunk (74 lanes, 56 of them channel totals) and grows to 296 MB on the first
frame after a mass spawn, against 8 MB projected. Input log (issue 0363): 16 B per external record for 66 frames in 256-record blocks
per chunk segment: 64 MB with steady traffic, 4.2 GB with C's records alternating.

## Gate verdict

The gate (sv <= 8.3 ms per frame at 1M entities with up to 1M records, 11 workers) **fails**: only A (100,000 records) passes, at
5.07 ms. B (1M records) takes 24.2 ms, about 3x the gate; C and D are further over. Memory at rest, ring, rewind and compile pass;
total memory with history is over the plan's projection. The causes are design-level (issues 0360-0363); no design work was started.

## Fixes made (each its own commit; all five suites green after each; bench digests unchanged)

| Commit | Change | Before -> after (median of 5 x 50 frames) |
|---|---|---|
| d45b099 | Seal plan runs on the workers (per-range totals, serial prefix over parts, per-range offsets) instead of the main thread | Seal A 1.442 -> 0.763, B 3.689 -> 3.008, C 9.508 -> 9.154, D 14.739 -> 14.354 ms |
| 401b69f | Input log: filter, hash and compare-with-last-segment per chunk on the workers (`LogScan.Examine`); main thread only stores and links (`RecordScanned`) | Log A 0.545 -> 0.124, B 5.022 -> 1.293, C 8.107 -> 1.872, D 10.732 -> 2.472 ms; no-dedupe case 6.9 -> 6.0 ms |

Suites after each fix: Laws 516, Scenario 80, Diff 16, Host 27, Lego 119 passed. Tried and reverted (slower, so not committed):
a row-major rewrite of `Seal.Plan` (A Seal 1.44 -> 1.64 ms) and skipping the log copy when a chunk is all external (C Log 8.1 -> 9.2 ms).

## Issues filed

0360 Decide kernel cost per record; 0361 Decide scratch per active chunk; 0362 Seal over the M2 gate (stop and report);
0363 input log memory; 0364 emitter empty `switch` in `Forward` (CS1522, correctness, C1's files); 0365 RawArena counts an interval
twice per layer (no measured cost: `rawGrows=0` in C, c-rotate and r, the runs that print it; a fix moves HardCap verdicts).

## Not measured

Unity / Burst and the netstandard2.1 host; the 10,000-rule kernels running a 1M world (only compiled); the dense storage policy;
late-input `Resolve` (rewind + replay) cost; Collect from real tl outboxes (records come from precomputed lists); the M1 rule
(64-row loop through the table within 10 % of a flat array); M4's "throughput halves between partition sizes"; 2,000 warm frames
for the 0 B gate (250 measured per scenario); 60 Hz is derived from the same numbers, not run separately.

## Update rc.4 (2026-11-14, Linux, Ryzen 5 8500G, quiet machine)

Red-team and 0511 fixes against the commit before them (`395a41c`), 5 interleaved processes per side, `--ms 1000`, medians of the 5 per-process medians (ms). Digests identical on both sides: A DAF3A1D9BD73BA10, B 6E7618BF0900D3F8, r 9CFD52083A9DA3FB.

| Shape | Seal before | Seal rc.4 | Total before | Total rc.4 | Limit |
|---|---|---|---|---|---|
| A | 0.683 | 0.695 | 2.298 | 2.325 | |
| B | 2.063 | 2.175 | 7.591 | 7.658 | gate 8.3: pass |
| r | 4.251 | 4.431 | 17.444 | 17.641 | ceiling 19.0 (D20): pass |

The cost is Seal's per-record check of the channel and source (issue 0504): +0.11 ms on B, +0.18 ms on r (about 1 percent of the frame).
