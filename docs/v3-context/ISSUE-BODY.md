# sv v3: full context, state and plan (read this first, then continue the work)

You are taking over sv v3 from another machine. Everything you need is in this repository, branch `v3/int` (and `v3/m8-unity`). Read this whole issue, then `docs/v3-context/` in the order below. Do not start coding until you can state in your own words what sv is, what it stores and where, how data moves, and what the owner wants (the owner's rule 18 in the unity-cli-skill field notes). The previous agents lost a day building without that understanding.

## 1. What sv is (the owner's intent)

sv is a host-independent, step-by-step, rewindable game-state engine, a library like tl but for state.
- Every entity has all of its stats, intrinsics and events at fixed generated-enum offsets in contiguous memory: O(1), no strings. Designers define them as data (health, double-jump count, death-in-5-seconds event); designers never write code.
- Everything that affects an entity in frame N is stored as an input record (target, source, source kind, type, amount, hits) and calculated in layers at the start of frame N+1: Seal, Stats, Decide (gate, remove, emit, flow), Commit (fold, events), Publish. Records last one frame and are folded into values; sticky effects are stat modifiers tagged with their source, with optional expiry, removed by source or handle.
- Rules (gates by type and source, immunity as numbers or bit masks, shields/absorb, reflection, triggers, priorities, chains) are authored by designers as data per content pack and compiled at build time to real C# (about 10,000 rules, 1,000,000 entities). A slow reference interpreter is the oracle.
- Rewind: a lane journal over 32 epochs plus an input log (32 worlds for the web simulator). Dead entities are retained. Any entity (hand, shield) can pass to another.
- Runs outside Unity (plain .NET, and later WASM in a web designer and simulator). Unity 7 (CoreCLR, Burst) is one host. tl (time), sv (state and rules), grid-influence (space) never reference each other; glue is generated in the host.
- Stats are integers with a designer-chosen decimal scale; percent modifiers are basis points; no float in state.

## 2. Reading order (all in `docs/v3-context/` unless noted)

1. `ALL-IN-ONE-sv3.md` sections 0 to 7: the owner's own words, requirements R1-R18, the worked example (health 100 to 95).
2. `plan.md`: the senior's design (17 sections), the authority. Section 14 has milestones M0-M12 with gates and kill rules, section 15 budgets.
3. `senior-answers.md`: SIMD and C# deep answers (lane primitives, scalar body is the law, vector twins under `#if NET`).
4. `agent-code-brief.md` and `BRIEF3.md`, `BRIEF4.md`: code style and agent rules (no comments in production code, plain data and separate behaviour, total functions, C# 9 and netstandard2.1 in Sv.Core and Sv.Rules, `#if NET` for intrinsics, 0 B warm allocation).
5. `../V3-HANDOFF.md` (sections 1 to 19, newest last) and `HANDOFF-sv-v3.md` (the earlier one).
6. `contract/CONTRACT-v3.0.0.md`, `v3.0.1-integer-stats`, `v3.1.0-bricks`, `v3.2.0-stat-layers`; issues in `issues/` (0160, 0170, 0180, 0190, 0200, 0210, 0330, 0350, 0360, 0370, 0390 hold the readings and findings).
7. `docs/LEGO-REPORT.md` and `docs/BENCH-REPORT.md`.
8. `docs/M8-HANDOFF.md` on branch `v3/m8-unity` (where the Unity adapter stopped).
9. `memory/` (the owner's working preferences) and the unity-cli-skill repo `SKILL.md` rules 1-20 (especially 17 talk before filing, 18 know the thing, 19 layers, 20 search the web when stuck).
10. `research/`: notes on StateSmith (pipeline, enum+switch emission) and QuanTAlib (SoA, SIMD dispatch).

## 3. State at handoff (2026-11-13)

Branch `v3/int` (pushed) holds everything for .NET: Sv.Core (lanes, records, Seal, journal, replay, integer stat machine with core/external layers and derive rows), Sv.Rules (pack model, reference interpreter, compiler passes, emitter, bricks v3.1: assign modes, amount from a cell, per-count modifiers, deterministic roll, spend gate), Sv.Host (frame loop, workers, input log cap), Sv.Pack (`sv pack|run|trace`), benchmarks. Legacy sv code stays untouched until M11. `main` is still the old phase-1/2 code (c0a3ae9) and must not be touched.
Verified: Laws 556, Scenario 80, Diff 20, Host 29, Lego 120 tests all pass on default, `DOTNET_EnableAVX2=0` and `DOTNET_EnableHWIntrinsic=0`; netstandard2.1 builds of Sv.Core and Sv.Rules have 0 warnings; compiled kernels equal the reference interpreter (differential tests, digests equal at 1, 2, 4, 7, 11 workers and in both storage policies); 0 B warm. Pinned digests that must never move: worked example `E0F445186E2C3672`, bench A `DAF3A1D9BD73BA10`, B `6E7618BF0900D3F8`, C `D3C714D46555B7CE`, D `7413F619472566C4`, plus the three legacy pins in the handoff.
Speed (1M entities, 11 workers, Ryzen 5 8500G): A about 3.0 ms passes the 8.3 ms gate; B 9.5 ms is close; C 29.5 and D 40.9 fail. Floors and causes are in issues 0360, 0362, 0370 and `docs/BENCH-REPORT.md`.
Lego test: 22 real mechanics written as designer data; all expressible after the bricks work.

## 4. What is left, in order

1. **M8 Unity adapter v4** (branch `v3/m8-unity`): the .NET groundwork is done (netstandard2.1 frame loop with a pluggable phase runner, `sv pack --host unity` writing PackData/PackJobs, engine-sync script, adapter package written but never compiled). Next: generate packs into the adapter, compile in Unity 7000.0.0a16 batch mode and drive `error CS` and `error BC` to 0 (fix the emitter or engine, never hand-edit output); main risk is Burst support for the engine's memory helpers (Buffer.MemoryCopy, Span.Clear): search Unity docs and use UnsafeUtility behind `#if` if needed; then fixtures, Unity gate tests (digests equal .NET at 1 and 11 workers, every probe 1, 0 B main thread), the authoring window, a fresh-clone run.
2. **M9** tl glue, Collect, requests, NorthStar3 (needs no tl change beyond generated glue; stop and report if it does).
3. **M10** web designer and simulator (WASM, interpreter-only build if too big).
4. **M11** deletions of the legacy code; all old pins stay green.
5. Speed: C and D (Seal random-record sort, Decide per-target cost), only with a measured before and after and identical digests.
6. Owner decisions pending (readings in contract text and issues, the owner has not yet said no): per-Hit counts offered hits; card modifiers once per held card; DoT ticks carry no source; assign values per hit; roll one draw per record; spend and absorb share one budget; edge rules cannot read cells; rewind does not undo spawns of the rewound epoch; rewind carry in the host versus journalled derived records; redundant bricks (card row vs intrinsic, `held` vs `gt`, deny vs shape-to-zero) to remove only with agreement.

## 5. How the owner works (follow exactly)

- Talk first and understand before building or filing. Never guess. The owner reads code directly; show code, no code comments.
- Replies short (the owner asked for 20 words, later 100).
- Use Opus agents for hard design work, smaller agents for bounded briefs; at most two big agents at once (usage limits); one measurement at a time on the CPU.
- Keep going without asking when the next step is already known; ask only for real design decisions (new bricks, meanings).
- You (the manager) own issues from agent findings: open, close and reopen them with receipts; do not create issues from chat. Never edit an existing test body or move a pinned digest. Never push to `main`; push branches. Commit after every atom.
- Verify before reporting: build with 0 warnings, run the five suites, check the bench digests. After a host or kernel surface change, build `src/Sv.Pack` first and delete `tests/Sv.Kernels.*/obj/SvPack` before rebuilding (stale generated kernels otherwise).
- Search the web when stuck instead of a third guess (rule 20); keep secrets and the owner's personal details out of searches and out of the repo.
- Update the unity-cli-skill repo with generational knowledge as you learn it (dense, early, no secrets).

## 6. First actions

1. Read section 2 above. 2. `git fetch`; check out `v3/m8-unity`; build and run the five suites (commands in `docs/V3-HANDOFF.md`). 3. Continue M8 from `docs/M8-HANDOFF.md`. 4. Report to the owner with numbers, not claims.
