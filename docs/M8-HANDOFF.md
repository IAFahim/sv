# M8 handoff (Unity adapter v4), branch v3/m8-unity, 2026-11-13

Second stop (owner moving to another PC), 2026-11-13. See "Where it stopped" at the end first.

## Done and verified on .NET (first stop: Laws 548, Scenario 80, Diff 20, Host 27, Lego 120)

- c4fc094: Sv.Host builds net10.0 + netstandard2.1 (C# 9). FrameLoop takes an IPhaseRunner (src/Sv.Host/Runner.cs:
  FramePhase enum of 11 phases, WorkerRunner = the .NET pool, still the default). Phase bodies no longer allocate:
  Decide record scratch and input-log part scratch are sized on the main thread (FrameLoop.DecidePhase/LogPhase,
  InputLog.ReservePart). Needed because Burst cannot call Marshal.AllocHGlobal.
- 080d893: PackTables.Of(plan) in Sv.Rules (HostPack.From calls it). HostEmitter (src/Sv.Rules/Emit) emits
  HostKernels.g.cs (byte-identical), and with `sv pack <pack> --host unity` also PackData.g.cs (HostPack as code)
  and PackJobs.g.cs (one [BurstCompile(Strict, CompileSynchronously)] IJobParallelFor per phase calling
  Phases<HostKernels>.X, probe per job, PackJobs runner, PackWorld.Create). HostPack split: HostPack.Plan.cs holds
  the Sv.Rules half and is left out of Unity.
- bd753ca: eng/engine-sync.sh sync|check -> packages/com.sv.engine (Sv.Engine.Core, Sv.Engine.Host runtime,
  Sv.Engine.Rules editor-only). Rerun sync after any src change.

## Written, never compiled (this commit): packages/com.sv.unity.v4

Runtime: SvProbe (0 unseen, 1 Burst, 2 managed via [BurstDiscard]), SvJobs (runner base, probe table),
SvWorld<TKernels> + ISvWorld + SvWorlds registry (DisposeAll), SvSlot/SvSlotCleanup/SvWorldHandle (GCHandle),
SvStepSystem (ISystem: kills released slots, Step). Editor: SvReload (beforeAssemblyReload -> SvWorlds.DisposeAll,
which joins worker threads and frees native tables). Asmdefs for runtime, editor, Tests/Packs, Tests/Editor.

## Left, in order

1. Generate test packs: `sv pack packs/reference --host unity --namespace Sv.Kernels.Reference --out
   packages/com.sv.unity.v4/Tests/Packs/Reference` and the same for packs/lego (Sv.Kernels.Lego); drop pack.svb
   and .svpack.key from the package.
2. Scratch Unity project (7000.0.0a16, the only editor here), manifest file: entries for com.sv.engine and
   com.sv.unity.v4, testables com.sv.unity.v4; template p2-run/gate.sh; take D:\GitHub\_work\unity.lock.
   Compile; count `error CS` and `error BC`. Unverified: Burst on Memory.Copy (Buffer.MemoryCopy) and
   Memory.Clear (Span.Clear) in netstandard Memory.cs; docs do not list them. If Burst rejects them, fix the
   emitter/core, never the output. Unity analyzer UAL0010/13 on statics may need csc.rsp -nowarn in the engine.
3. .NET twin fixtures: new test project writing per-scenario journals (spawn/kill/stat op/send with resolved kind,
   per-frame LastDigest, final Digest) for reference + lego scenarios at workers 1 and 11 into
   packages/com.sv.unity.v4/Tests/Editor/Twin; SV_UPDATE_TWIN=1 rewrites, else compare.
4. Unity EditMode tests: twin replay at JobsUtility.JobWorkerCount 1 and 11 (digests equal), every phase probe 1
   (pick a scenario that hits SealCompact, LogExamine, EdgeEmits), main thread 0 B over warm frames (delta into a
   local first), SvStepSystem on a manual World, reload hook disposes worlds.
5. Authoring window (Editor): load the four *.sv.json, edit, validate with PackCompiler diagnostics, add-value
   row, save, regenerate with HostEmitter.Unity(plan, ns); test: editor output equals the committed generated files.
6. Fresh clone + fresh project gate, then handoff section in V3-HANDOFF.md and AGENTS.md layer table.

Decision taken: Sv.Rules stays editor-only in Unity (plan 10); runtime gets the pack as generated PackData.

## Where it stopped (second session)

Done:
- Merged origin/v3/int dba1582. Its new law InputLogCapLaws.PerFrameRecordCap... called LogScan.Examine without
  part scratch (this branch moved the scratch sizing to the main thread); the law now calls InputLog.ReservePart(part,
  count) after Scan. Five suites: Laws 556, Scenario 80, Diff 20, Host 29, Lego 120, all green. No src file changed this
  session (only that law, the engine package resync and generated files), so the bench digests A..D are untouched
  (not rerun this session).
- engine-sync.sh sync after the merge (Arena.cs, InputLog.cs, Frame.cs came in); check passes, 76 files.
- Item 1 done: Tests/Packs/Reference (PackHash BD70627B1C671888) and Tests/Packs/Lego (PackHash 9A043301EA06000E)
  generated with `dotnet run --no-build -c Release --project src/Sv.Pack -- pack packs/<p> --host unity --namespace
  Sv.Kernels.<P> --out packages/com.sv.unity.v4/Tests/Packs/<P>`; pack.svb and .svpack.key deleted.
- Item 2, first compile: Unity 7000.0.0a16 batch mode, all assemblies build (Sv.Engine.Core/Host/Rules,
  Sv.Unity.V4, Sv.Unity.V4.Editor, Sv.Unity.V4.TestPacks): error CS 0, error BC 0, warning CS 6 (all CS8632 nullable
  annotations outside a #nullable context; fix with a csc.rsp `-nullable:enable` next to the engine asmdefs, verified
  to work on a16 per the field notes).

NOT verified: error BC 0 means only that Burst compiled nothing during a -quit import. Burst compiles a job when it is
first scheduled (CompileSynchronously), so Buffer.MemoryCopy (Memory.Copy) and Span.Clear (Memory.Clear) inside
Burst jobs are still unproven. No web search done yet. The first EditMode test that schedules a PackJobs runner
will show it: SvProbe reads 2 (managed fallback) or a BC error in the log instead of 1. If Burst rejects them, swap in
UnsafeUtility.MemCpy/MemClear behind `#if UNITY_2017_1_OR_NEWER` in src/Sv.Core/Memory.cs (netstandard path only),
resync, digests stay unchanged.

Only on the old PC (recreate on the new one):
- Editor: C:/Program Files/Unity/Hub/Editor/7000.0.0a16/Editor/Unity.exe
- Scratch project D:/GitHub/_work/sv-proto/m8-harness: ProjectSettings and Library copied from c-harness;
  Packages/manifest.json = c-harness manifest minus com.sv.* and com.tl.unity, plus
  "com.sv.engine": "file:<repo>/packages/com.sv.engine", "com.sv.unity.v4": "file:<repo>/packages/com.sv.unity.v4",
  "testables": ["com.sv.unity.v4"] (entities 7.0.0, burst 2.0.0, collections 7.0.0, test-framework 1.9.0).
- Gate script D:/GitHub/_work/sv-proto/m8-run/gate.sh (env R repo, P project, O out, T tag, COMPILE_ONLY, MODES,
  FILTER): runs engine-sync.sh check, a -batchmode -nographics -quit compile with a log, counts error CS / warning CS /
  error BC, then -runTests per mode. m8-run/locked.sh takes D:/GitHub/_work/unity.lock and bench.lock around it.
  Logs in D:/GitHub/_work/sv-proto/m8-out (c1-compile.log is this session's compile).

First command next (after recreating the project on the new PC):
  COMPILE_ONLY=1 T=c2 bash m8-run/locked.sh m8-run/gate.sh
then item 3 (.NET twin fixtures), then item 4 (EditMode tests; the first one proves Burst on the memory helpers).

## Scripts copied into the repo (2026-11-13)

`eng/m8-run/gate.sh` and `eng/m8-run/locked.sh` were only on the first PC; they are now in the repo. They expect to be run from the sv-proto parent folder as `m8-run/...` and a scratch project `m8-harness` next to the repo: copy them to `<parent>/m8-run/`, rebuild the scratch project as described above, check the Unity editor path inside them for this PC, then run the first command: `COMPILE_ONLY=1 T=c2 bash m8-run/locked.sh m8-run/gate.sh`.
Status at handoff: Unity 7000.0.0a16 batch compile gives error CS 0 and error BC 0 (6 nullable warnings); Burst has not compiled any job yet (it compiles on first schedule), so Buffer.MemoryCopy and Span.Clear in jobs are the first thing to test; no web search was done on that yet.
