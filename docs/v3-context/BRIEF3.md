# sv v3 coding phase (.NET first): common brief for every agent

Date 2026-11-12. Owner order: "complete fully the coding part ... fully write the full code and do the hardest part for .NET, then we do
bench". So this phase WRITES the code and proves it correct with tests. It does NOT chase performance numbers.

## Read first (in this order, fully; you may not guess or assume)

1. `D:\GitHub\_work\ALL-IN-ONE-sv3.md` sections 0 to 7 only (the owner's intent, requirements R1-R18, the worked example). The owner's own
   words are there. Do not skip: previous agents failed by building without knowing what sv is for.
2. `D:\GitHub\_work\plan.md` (the senior's design, 17 sections, 1190 lines): THE authority. Read all of it. Its section 13 has source
   examples that were never compiled: follow them as intent, fix them when they do not compile, and report each discrepancy.
3. `D:\GitHub\_work\sv-proto\BRIEF.md` rules (they still hold unless this file says otherwise) and the sv repo `AGENTS.md`.
4. The sv code you touch (`src/Sv`, tests) and the contract (`contract/`).

## Code style authority (read before writing a line)

`D:\GitHub\_work\agent-code-brief.md` is the standing code brief for every agent (data plain and behaviour separate, total functions,
order from data, scalar function is the law then SIMD twins, what compiles where). It overrides any style hint elsewhere. Unity-visible
sources stay C# 9 on netstandard2.1; `static abstract` and other newer features only behind `#if NET` or in .NET-only projects.

Adopted plan decisions, each isolated behind ONE function so it can change in one place: D1 is one mechanism, not two engines: one
address function from (chunk, value) to a lane, and the dense layout is the identity table chosen at allocation. Plan assumptions S1
(types are per-frame totals), S2 (one frame of delay between entities, except forwarding), G2 ("consumed" is computed Taken/NotTaken, no
stored field) and G3 (the unit of "every damage you take" is a field of the rule) are adopted as written.

## What sv is (so you do not guess)

A host-independent, step-by-step, rewindable game-state engine. Every entity has all stats, intrinsics and events at generated enum
offsets (O(1), no strings). Everything that affects an entity in frame N is stored as records (target, source, source kind, type, amount,
hits) and calculated in layers at the start of frame N+1: Seal, Stats, Decide (gate, remove, emit, flow), Commit (fold, events). Rules are
authored by designers and compiled per content pack. Rewind is a lane journal over 32 epochs. tl stays outside (generated glue only);
Unity is just a host. All of this is in plan.md.

## Owner decisions (answers to plan section 17.3: the owner said go; the plan's recommendations are adopted)

D1 storage: implement BOTH policies behind one interface: shared lanes (default) and dense identity table. The owner wrote "contiguous":
the dense policy must work and be tested too. D2 16-byte record. D3 ring 32 epochs of 2 frames at 30 Hz (configurable). D4 sum then clamp
once per frame. D5 sv-only replay, game decides per retracted event. D6 gate 8.3 ms at 1M entities and 1M records at 30 Hz, 60 Hz reported:
NOT measured in this phase. D7 contract v3.0.0 as `contract/CONTRACT-v3.0.0.md` (new file; owner-approved while the agent-bus is
unreachable; nothing existing is edited). D8 sourced timeline effects as a status entity (no tl change). D9 outward event channel 16,384.
D10 arena full: grow once and rerun; hard cap option for the browser.

## Scope of this phase

In: M0 (contract text), M1, M2, M3, M4, M5 (.NET frame loop and workers, correctness only), M6, M7 of plan section 14, plus the .NET
`Sv.Laws.Tests`, `Sv.Diff.Tests`, `Sv.Scenario.Tests` projects. Out for now: M8 Unity adapter, M9 tl glue and NorthStar3, M10 web, M11
deletions, M12 reserves. Do not touch `packages/com.sv.unity`, tl, or tl.unity.

OWNER ORDER (latest, overrides the paragraph below): WRITE ALL THE CODE FIRST. Production code only, written as high-performance code from
the first line (SIMD with scalar twins, unmanaged, 0 B warm, straight loops, no abstractions that cost). Do NOT write or run tests, laws,
benchmarks or digest checks in this wave; they come later. You MUST still make it compile: run `dotnet build <your csproj> -c Release` for
both target frameworks and fix every error and warning (warnings are errors) before you commit. Complete files only: no placeholders, no
TODO, no elided bodies. The owner wants the fundamental core that holds everything together finished, not sketched.

Earlier text, now deferred: no benchmark numbers in this phase. Where a milestone gate is a speed number, write the benchmark project code
(`benchmarks/Sv.Bench`) so it builds, but do not run it for numbers and do not tune. Kill rules that depend on speed are checked in the
benchmark phase. The correctness gates (laws, digests, differential tests, 0 B warm allocation, equal digests at 1, 2, 4, 7, 11 workers)
are checked in the test phase after all the code exists.

## Where and how

- sv checkout: `D:\scratch\plan-connect\sv` (main = c0a3ae9). Your worktree and branch are in your task. Commit atoms on your branch.
  Never commit on main in the shared checkout. Do not push. Do not edit `sv.slnx` (the manager adds projects at integration); build
  and test your projects by path with `dotnet test <csproj>`.
- Old code stays side by side until M11. New projects: `src/Sv.Core`, `src/Sv.Rules`, `src/Sv.Pack`, `src/Sv.Host` per plan 13.9. Files
  that the plan says "stay unchanged" (IntrinsicLaw, Clamp, Condition, CellDigest, StatMath, ...) are LINKED into the new project with
  `<Compile Include="..\Sv\X.cs" Link="..."/>` so there is one copy until M11. Never fork a copy.
- Production code: no comments, no FMA, C# 9, multi-target `net10.0;netstandard2.1` (Sv.Core, Sv.Rules and everything the emitter
  produces), `#if NET` for System.Runtime.Intrinsics, unmanaged data path, deterministic order, 0 B warm allocation including workers.
- Backward compatibility is not required for new code, but never edit an existing test body, a pinned digest or `contract/` file. Pins
  that must not move: cross-host `A2ACAA69E55F79D8`, Burst kernel `2B43BC1549AAE751`, soak `6D6C602BF07E9366`, the north-star migration
  digests. New pins are baked once on .NET and asserted on all ISA legs (`eng/isa-legs.sh`: default, no-AVX2, scalar, netstandard2.1).
- Tests: new tests only. Property tests over generated inputs where plan 12.2 says so. A slow reference interpreter (M3) is the oracle.
- Issues: file an `issues/NNNN-*.md` ONLY for a finding you made while working (a bug, a plan contradiction, a kill rule tripped, a
  design problem). Not one per atom. Number range is in your task. Format: short, with receipts and `Status: open|closed`. Close your
  own when fixed and verified; the manager reviews.
- Commits end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>` unless your harness requires another trailer; say which.
- Stop and report (do not decide): a pinned digest moves; a digest differs between hosts or worker counts; a sentence of R8-R10 cannot be
  expressed; the plan is wrong in a way that changes its design (list it with evidence); anything needing a tl change.
- Usage limits are tight: work efficiently; if you hit a limit, stop cleanly with work committed. Final report under 400 words: commits,
  exact test counts, digests, what is not done, plan discrepancies found, decisions for the owner.

## Wave 2 addendum (2026-11-12): integer stats and file ownership

Wave 1 is merged on branch `v3/int` (head 03254cc): Sv.Core (Layout, Lanes, LaneMath, LanePool, World, Journal, ChunkImage, Digest,
InputLog, Replay, Effect, Arena, Seal, Route) and Sv.Rules (Pack, Reference). Read their issues first: 0160, 0170, 0180 (plan gaps and
the readings the code follows). Wave 2 branches start from v3/int.

D11 (owner: "use the senior's plan fully"; the senior's answer 15 and the owner's wish for integer stats with precision): STATS ARE
INTEGERS. Every stat is an `int` with a designer-chosen decimal scale (1, 10, 100, 1000, ... up to 1,000,000) stored per value in the pack
(`Scale`). The engine holds no float for state anywhere; a float exists only at a host edge for display and never flows back. Percent
modifiers are basis points (10,000 = 100 %). The integer stat law mirrors the algebra of `src/Sv/StatMath.cs` (Added, Additive, Multi, Cap,
derived stats): read StatMath.cs and the plan's stat machine sections, then define it in integers with int64 intermediates, one
division rule (truncate toward zero, i.e. C# `/` on int64), saturation to int32, and a fixed order of evaluation; the law lives in ONE
file `src/Sv.Core/Stats/StatLaw.cs` (scalar bodies are the law, vector twins under `#if NET`). The old float stat machine in `src/Sv`
and its pins (soak 6D6C602BF07E9366, A2ACAA69E55F79D8, 2B43BC1549AAE751) stay untouched as legacy until M11; the new integer machine gets
its own pins later. Gate Shape (the modifier algebra on record amounts) uses the same integer law. C3 owns writing the law and the
contract addendum `contract/CONTRACT-v3.0.1-integer-stats.md` (new file); C1 and C2 call `StatLaw` only through its public functions and
may stub against the signatures they need until C3's commit lands (merge happens at integration).

Ownership (each agent only touches its own files; issue ranges): 
- C1 (compiler): src/Sv.Rules/Passes/*, src/Sv.Rules/Emit/{Values,Kernels}*.cs and emit plumbing; issues 0190-0199.
- C2 (host and tool): src/Sv.Host/*, src/Sv.Pack/*, build/Sv.Pack.targets, the scenario file format and runners, benchmarks/Sv.Bench entry points written (not run); issues 0200-0209.
- C3 (stats): src/Sv.Core/Stats/*, the stat hand-over to lanes (M7), the integer StatLaw, the matching change of the stat fold inside src/Sv.Rules/Reference (Law.cs and where StatFold is used), the contract addendum; issues 0210-0219.
Shared files (csproj): append lines only. Do not touch another agent's files. Kernel convention for C1 and C2 (plan 13.5, 13.6, senior
answers 1, 4, 5): generated kernels call only the lane primitives in Sv.Core/LaneMath; Burst-safe scalar bodies; real `.cs` files written by
a pre-build tool (not a Roslyn generator); one method per channel and per card group; enum number = lane index.
