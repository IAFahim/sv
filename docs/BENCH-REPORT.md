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
