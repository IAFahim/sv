# Issue 0114 — Atom 105: Unity editor CLI — live-editor iteration without domain reload + the 1M-entity sv-stage receipt

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Unblocks the receipt deferred BLOCKED-ON-EDITOR in [issues/0110](0110-atom-102-unity-adapter-v2.md)
(atom 102, roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) "1M-entity scene: sv stage ≤ 5 µs
on the main thread"), against the BCL audit of [issues/0111](0111-atom-101-burst-kernels.md).
Labels: `ai`, `area:adapter`, `kind:feat`. Built in worktree `agent-103` (main@e3870b7).
Per owner steer: batch non-interactive mode only — no GUI editor session, no socket-fed live
editor; the fancier mechanisms are documented below as options with evidence, not built on.

## Changes

- **`tests/Sv.Editor/`** — the editor leg (all test tooling, managed freely):
  - `unity/` — minimal Unity 6000.7.0b5 project (ProjectSettings copied from the Burst leg)
    with a real dependency set: `com.unity.entities 1.5.0` (+ `com.unity.burst 1.8.29`,
    `com.unity.collections 2.7.1`, `com.unity.mathematics 1.3.2` — resolved from
    packages.unity.com; the registry is reachable from this host). Note: `com.unity.entities`
    tops out at **1.5.0** on the registry — the "6.7" in `com.sv.unity/package.json`'s
    dependency pins is the editor version, not a shippable Entities version; the package.json
    is unchanged (nothing consumes it here) and the discrepancy is recorded in this finding.
  - `Assets/SvShim/` — the BCL shim assembly (see Findings B2).
  - `Assets/Editor/SvEditorBatch.cs` — the batch harness: builds the FF-1M program (ff1m's
    exact 16-stat schema, 4 int intrinsic columns as `IComponentData, IEnableableComponent`,
    1/16 of rows' mana disabled so Unity's enabled masks are real) over a real `World`, then
    runs the clause-7 v2 frame order **in one in-process loop** — reconcile at structural
    boundaries (every 64th tick: 8 unbind+destroy, 8 spawn+bind), per-chunk O(1) SvRange
    intrinsics visits through `IJobChunk`+`Run` (main thread, Unity hands the v128 enabled
    masks), hit/heal windows as `Board.RunEnabledChunk` over the chunk's slot range,
    `ExpireModifiers` + `TakeDueCrossings`, `Recalculate` — Stopwatch per stage, p50/p99,
    `AssemblyReloadEvents` counters, verify probes with ff1m's exact arithmetic.
  - `run-editor.sh` — two phases: `probe` (verbatim `src/Sv` + adapter copies, no shim on the
    compile path — captures the exact in-editor compile errors) and `measure` (shim active +
    the eight recorded compile-surface transforms below, sha256 of pristine and transformed
    sources, per-file diff-line counts into `receipt/transform-report.txt`, boot epochs/wall
    time). `asmdefs/measure/` holds the canonical assembly definitions the script installs.
  - `receipt/` — the artifacts this issue cites (probe log, warm+cold 1M receipts, transform
    report, source hashes, boot timings).
- **`packages/com.sv.unity/UnityPopulation.cs`** — small real-Unity API drift fixes (in scope
  per instruction; verified against the fetched Entities 1.5.0 source):
  `AddComponent(entity, data)` → **`AddComponentData`** (1.5's name for add-with-value);
  `EntityManager.GetComponentTypeHandle<T>()` → explicit `(false)` (1.5 has no default);
  `chunk.GetNativeArray(ref handle)` → by-value handle (1.5's signature). No behavior change.
- **`tests/Sv.Unity.Facade/Entities.cs`** — facade mirrors the real API names so the same
  adapter sources compile both places: adds `AddComponentData<T>(Entity, T)` (delegates to the
  existing add-with-value) and a by-value `GetNativeArray(EntityTypeHandle)` overload next to
  the by-ref one. The facade kit's `CreateSystem`/`unmanaged` relaxation stands as in 0110.
- No change to `src/Sv`, `contract/`, `tests/Sv.Tests/StrictFloatOracle.cs`, or README math.

## Findings

### Part A — the editor CLI surface and the cheapest iteration loop

- **`which unity` → `/home/i/.local/bin/unity`** — the real Unity CLI (a .NET 10 app,
  `UnityCli`) ships on this host; `unitycli` as a name does not exist. Its help documents the
  full live-editor surface: `run` (batch forward), `test` (UTF CLI: EditMode/PlayMode,
  `--filter/--shard/--rerun-failed`), `recompile` (recompile a *running* editor), `command`
  (execute registered editor commands on a *connected* editor instance), `status` (live
  port/project/PID of every connected editor), `open`/`close`, `shell` (warm REPL), `watch`,
  `job`, `list` (Pipeline-package tools). These are the documented no-reload iteration
  mechanisms (a kept-alive editor fed commands); evidence: `unity --help`,
  `unity command --help`, `unity test --help`, `unity recompile --help` (excerpts in
  Receipts). **Not built on** — they need a GUI/interactive editor session, outside the steer.
- **`"$UNITY" --help` / `-help`** is not a console usage printer on this Linux build: bare
  `--help` starts the GUI splash (hung, killed); `-batchmode -nographics -help` runs the
  editor up to licensing, then crashed on the forced-kill shutdown (sig 11 in the log). The
  flag echo is in the log's `COMMAND LINE ARGUMENTS:` block. The editor dir ships no
  `unitycli` of its own (`ls` of `Editor/` in Receipts).
- **The mechanism used (per steer): one `-batchmode -nographics -quit -executeMethod`
  invocation that boots once and runs its own in-process loop.** The script code is fixed
  across rounds, so **no domain reload occurs**: `AssemblyReloadEvents` before/after counters
  read **0/0** across warmup 16 + 512 measurement rounds (both 1M runs). A reload would be
  paid per iteration otherwise: the editor's own boot stats put a script-import domain reload
  at **440–490 ms** (`Scripting: domain reloads=1, domain reload time=465 ms, compile time
  900 ms`) — 512 rounds × ~0.45 s ≈ 230 s of pure reload vs the 0 actually paid.
- **Boot costs** (wall, shell-start → editor exit; `date +%s.%N` epochs recorded):
  warm boot with changed scripts → executeMethod entry **4.9 s** (full invocation incl. the
  8K smoke program: 11.5–12.4 s); warm full 1M program (boot 4.9 s + setup 3.7 s + 528 frames
  ≈ 18 s + shutdown): **32.4 s**; cold (Library wiped, full import + package re-resolve):
  **39.9 s** for the same 1M program — i.e. cold ≈ 12–13 s to entry, ≈ 2.5× the warm 4.9 s.
  First-ever boot additionally pays the UPM fetch (entities 1.5.0 + deps ≈ 47 MB tgz).
- **Enter Play Mode Options / domain-reload-off**: documented Unity mechanism
  (ProjectSettings → Enter Play Mode Options) that skips reloads when entering play; not
  exercised — irrelevant to a `-quit` batch measurement and to a fixed program (which needs
  no reload at all, as proven above).

### Part B — the blocked receipt, delivered

**B1. Does `src/Sv` compile in-editor verbatim? No.** The probe boot (verbatim copies, no
shim) fails with, per error class (exact text in `receipt/unity-probe.log`):

| Blocker | Error | Sites |
|---|---|---|
| `System.Runtime.Intrinsics` Vector128/Vector256 absent | CS0234/CS0246 (`The type or namespace name 'Intrinsics' does not exist in the namespace 'System.Runtime'`) | Simd.cs, Simd.Chunk.cs (261 errors) |
| `IStateCodec.static abstract bool Reading` — runtime gate | CS8919 (`Target runtime doesn't support static abstract members in interfaces`) | StateCodec.cs |
| `delegate* unmanaged<...>` — runtime gate | CS8889 (`target runtime doesn't support extensible or runtime-environment default calling conventions`) | EventStreams.cs (24) |
| `IsExternalInit` absent | CS0518 | Schema.cs (27) |
| primary constructors need C# 12 | CS9058 at `-langversion:11` → fixed by `csc.rsp` `-langversion:12` | Schema.Attributes.cs (9) |

With the Intrinsics cascade cleared, the member-level gaps surface (the probe's first pass
masks them — Roslyn stops binding after the namespace failures):

| Blocker | Error | Sites |
|---|---|---|
| `System.Numerics.BitOperations` internal (matches 0111's audit) | CS0122 (`inaccessible due to its protection level`) | Board*.cs, Drift, EventStreams, SetTable, Simd.Chunk, XxHash64 (24) |
| `NativeMemory` absent | CS0103 | 12 files (~90) |
| `BitConverter.SingleToUInt32Bits/UInt32BitsToSingle` absent | CS0117 | Board.Stats, Board.Keyframe, InputState, Schema, SetTable (20) |
| `OperatingSystem.IsLinux` absent (type exists, member doesn't) | CS0117 | NativeArena.cs |
| `Environment.TickCount64` absent | CS0117 | Bulk.cs (2) |
| `ObjectDisposedException.ThrowIf` absent | CS0117 | Drift.cs (4) |
| `Interlocked`/`Volatile` over `nint` (generic class-constrained only in netstandard2.1) | CS0452 | EventStreams.cs (2) |

**B2. What the measurement was built on (shim + recorded transforms, per the mission's
option (a), extended where members on existing BCL types cannot be supplied by any
compilation unit):**
- `SvShim` supplies types: `System.Runtime.Intrinsics` Vector128/Vector256
  (`IsHardwareAccelerated => false`, so sv executes its **scalar kernel twins** — the exact
  path the Burst leg proved bit-identical on .NET/Mono/Burst), `IsExternalInit`, a public
  `BitOperations`, `NativeMemory` (libc malloc family), and `System.SvUnityBits` (twins for
  the five member gaps above; `SingleToUInt32Bits` is bit-identical via the profile's
  `SingleToInt32Bits`).
- `run-editor.sh` applies eight recorded, mechanical transforms to the SvCore **copies**
  (never `src/Sv`): static-abstract `Reading` → instance property (receiver `codec` in scope
  at all 27 call sites), `delegate* unmanaged<` → `delegate*<`, and the five member-name
  rewrites to `SvUnityBits.*`. `receipt/transform-report.txt` pins the counts and per-file
  diff-lines; `source-hashes-*.txt` pins both sides byte-for-byte.
- Correctness cross-check: the Unity leg's fold values (armor 22.5 steady / 56.25 buffed —
  `Multi v` folds ×(1+v), matching ff1m's own Verify constants) reproduce **bit-exact on
  .NET 10** (a standalone `Board` program with the same setup prints 22.5/56.25) — the
  shimmed Mono leg and the native leg agree.

**B3. The measurement** (FF-1M program shape at Unity: 2^20 entities, 8,200 real chunks,
16-stat board 244 MB, 2 M timed modifiers (~6.7 expiries/tick over 512 ticks = 3,447), 8
scheduled crossings (all collected, first at tick 200 with id intact), 8 buffs/tick and
64+32 hit/heal rows per frame as board windows, reconcile every 64th tick, 16 warmup + 512
measured rounds, **0 domain reloads**, verify probes ff1m-exact — hp 84/mana 46/hit 59 at
1,048,576 live):

| Stage (main thread, Mono) | p50 | p99 |
|---|---|---|
| **sv stage total, steady frame** | **33.81 ms** | 35.6 ms |
| — chunk visits (4 uniform intrinsics × 8,200 chunks + hit/heal windows, enabled masks) | 33.79 ms | — |
| — ExpireModifiers + TakeDueCrossings | 4.1 µs | 8.3 µs |
| — Recalculate (Δ=128 dirty propagation, 8 bound outputs) | 13.9 µs | 48.5 µs |
| sv stage total, structural frame (reconcile inside) | 102.4 ms | — |
| — UnityPopulation.Reconcile alone (8,200 chunks, board renumber) | 64.8 ms | 71.2 ms |

Warm and cold runs reproduce (33.81 vs 33.84 ms total-steady p50).

**The ≤ 5 µs roadmap line is not met — and as written it cannot be, at any compiler:**
the stage's cost *is* the column math (4 uniform intrinsic passes = 4 M saturating
int adds + clamps per frame over real chunk memory). Even perfect AVX2 throughput bottoms
out around 4 M ops / (32 lanes × ~4 GHz) ≈ **31 µs** of pure arithmetic; the measured .NET
native ff1m frame for the same shape is **2.99–3.24 ms** (this atom's bench run), and the
editor-Mono leg measures **33.8 ms ≈ 10.6× the .NET frame** — attributed to (a) managed
`Board` on Mono JIT (roadmap 6.1's unmanaged core is the fix), (b) the scalar kernel twins
(`IsHardwareAccelerated=false` in the shim; real Unity would Burst-compile the chunk job,
blocked today because `SvChunk` holds the managed `Board` — the same unmanaged-core gap
0110 recorded), and (c) `IJobChunk`+`Run` main-thread dispatch over 8,200 chunks. For
contrast, the sv-owned bookkeeping outside the column math is already at microsecond scale
(18.0 µs combined for expiry + crossings + Δ=128 recalculate), and the Burst-compiled kernel
subset already measured ~1 ms/program in 0111. The 5 µs figure can only describe a
per-chunk-visit or system-dispatch slice, not the 1M-row column math; re-stating the target
(per the roadmap's own ff1m anchor of ~3 ms native frames) is a roadmap note, not this atom.

Also recorded: `SvSystem` compiles in-editor as authored (the drift was only in
`UnityPopulation`), but its `ISystem` cannot be instantiated in real Unity while it holds
managed `Board`/`UnityPopulation` fields (`CreateSystem<T>`'s `unmanaged` constraint) — as
0110 already flagged for the facade; the harness therefore drives OnUpdate's exact verb
order directly on the main thread instead of through a system instance.

## Receipts

Measured on the bus host (decision 0048): i9-14900K, 24C/32T, Linux x64, Unity 6000.7.0b5
(Personal license active — same editor as the Burst leg's `tests/Sv.Burst/run-burst.sh`),
Entities 1.5.0 / Burst 1.8.29 / Collections 2.7.1 / Mathematics 1.3.2. CI is dead (GitHub
billing) — local receipts authoritative.

- **CLI surface**: `unity --help` (subcommand list quoted in Findings A; `unity command
  --help`: `--project-path`, `--runtime`, `--timeout`, `--detach`, `--result-only`),
  `unity test --help` (`--mode EditMode|PlayMode`, `--filter`, `--shard n/m`,
  `--rerun-failed`, `--coverage`), `unity recompile --help` (recompile a running editor,
  `--strict`). `"$UNITY" --help` hangs (GUI); `-batchmode -help` log excerpt in Receipts
  below. `ls Editor/`: no CLI tool beyond `Unity` itself.
- **Probe boot (verbatim src/Sv)**: `bash tests/Sv.Editor/run-editor.sh probe` → editor
  exit 1, `Scripts have compiler errors`, full error table in
  `tests/Sv.Editor/receipt/unity-probe.log` (headers: `Assets/SvCore/Simd.cs(1,22): error
  CS0234: The type or namespace name 'Intrinsics' does not exist in the namespace
  'System.Runtime'` …; `StateCodec.cs(8,36): error CS8919`; `EventStreams.cs(255,28):
  error CS8889`; `Schema.cs(50,49): error CS0518`). Wall 11.5 s (packages cached).
- **Reference-assembly probe** (corroboration, editor disk): `strings` over
  `Data/NetStandard/ref/2.1.0/netstandard.dll` and `Data/UnityReferenceAssemblies/unity-4.8-api/mscorlib.dll`:
  `Vector256`/`Vector128`/`NativeMemory`/`SingleToUInt32Bits` 0 hits both profiles;
  `SingleToInt32Bits`/`Int32BitsToSingle`/`BitConverter`/`IsLittleEndian` present (probe
  method validated).
- **Smoke boot (8,192 entities)**: verify ok (probe hp=84 mana=46 hit=59 armor
  22.5/56.25, live=8192), editor exit 0, wall 11.5–12.4 s.
- **1M warm run**: `SV_EDITOR_*` defaults (`bash tests/Sv.Editor/run-editor.sh measure`),
  wall 32.4 s, boot-start→entry 4.9 s, setup 3,687 ms, 512 rounds + 16 warmup,
  `reloadsBefore=0 reloadsAfter=0`, total-steady p50 **33.809 ms** / p99 35.574,
  visits p50 33.791 ms, expire p50 0.0041 ms, recalculate p50 0.0139 ms, reconcile p50
  63.452 ms (n=8), structural total p50 100.554 ms, expiredTotal 3,447, crossingsTotal 8,
  crossingFirstTick 200, chunks 8,200, board 244,008,916 B, rss 991 MiB
  (`receipt/run-warm/editor-receipt.json`).
- **1M cold run** (Library wiped): wall 39.9 s, same program, verify ok,
  total-steady p50 **33.835 ms** / p99 37.524, reconcile p50 64.819 ms, reloads 0/0
  (`receipt/editor-receipt.json`).
- **Domain-reload cost** (the editor's own boot stats, per boot with changed scripts):
  `Scripting: domain reloads=1, domain reload time=465 ms, compile time: 900 ms` — a reload
  is ~0.45 s; the measurement loop paid zero.
- **Cross-check of the fold on .NET 10**: standalone program (schema + `Board` + the same
  modifier program) prints `armor@3=22.5 … armor@8069=56.25` — identical to the editor leg.
- **Transform accounting**: `receipt/transform-report.txt` — Reading transform: interface +
  4 impls + 27 call sites; `delegate*<` 8 sites; `SvUnityBits.SingleToUInt32Bits` (4 files) / `UInt32BitsToSingle` (2
  files); TickCount64 2; ThrowIf 4; nint atomics 2; IsLinux 1; per-file diff-lines (only
  transformed lines differ; e.g. Board.Stats.cs 48 diff-lines for ~4,000 lines). Source hashes both sides in `source-hashes-*.txt`.
- **Battery (this worktree)**: `dotnet build sv.slnx -c Release` — **0 warnings 0 errors**;
  `dotnet test tests/Sv.Tests/Sv.Tests.csproj -c Release` — **446** passed; same with
  `-p:SV_CHECKED=true` — **446**; `dotnet test tests/Sv.Unity.Tests/Sv.Unity.Tests.csproj
  -c Release` — **13** passed; `dotnet run --project tests/Sv.NorthStar -c Release` —
  silent, exit 0; `dotnet run --project benchmarks/Sv.Bench -c Release` — **all scenarios
  ok** (ff1m p50 2,985.3 µs, renumber1m 5,314.9 µs, wheel-expiry 131.8 µs);
  `grep -rn "FusedMultiplyAdd" --include="*.cs" src/` — no matches.

## Provenance

`main@e3870b7` + this atom, worktree `agent-103`. Bus checked before and after (inbox empty
at start). References: blocked receipt [issues/0110](0110-atom-102-unity-adapter-v2.md),
BCL audit [issues/0111](0111-atom-101-burst-kernels.md), roadmap
[issues/0058](0058-roadmap-1m-entities-5us.md).
