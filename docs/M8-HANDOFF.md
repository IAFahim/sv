# M8 handoff (Unity adapter v4), branch v3/m8-unity, 2026-11-13

Stopped at the usage limit. Nothing here ran in Unity yet.

## Done and verified on .NET (five suites equal baseline: Laws 548, Scenario 80, Diff 20, Host 27, Lego 120)

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
