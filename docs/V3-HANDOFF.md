# sv v3 handoff (written 2026-11-12 before context compaction). Read this first.

This file is the manager's complete state. Anything not here is lost on compaction. Owner = the user (IAFahim). Manager = the main Claude
session. Local time is Asia/Dhaka (+0600).

## 1. What sv is and what the owner wants (their intent, not my guess)

sv = state library for games, sitting beside tl (timelines) and not depending on tl or Unity. Owner's words, condensed:
- Every entity has ALL stats (float in v1; now INTEGER fixed-point, see D11), intrinsics (ints) and events, at fixed offsets in contiguous
  memory; a generated enum IS the offset (O(1), no dictionary, no strings). Designer-set defaults. Stat can be max/min of an intrinsic.
- Everything that happens to an entity in frame N is STORED (records: target, source, source kind, type, amount, hits) and calculated in
  LAYERS at the start of frame N+1 (not applied immediately, not discarded).
- Rules (gates, immunity as numbers/bit masks, shields, curses, reflection, triggers, priorities, chains of chains) are authored by
  designers as data and COMPILED at build time to straight fast code. About 10,000 rules, 1,000,000 entities.
- Rewind by snapshots (ring of 32 worlds) for server lag compensation; dead entities retained a while; stateless step-by-step layers.
- Runs in plain .NET and browser WASM; Unity 7 is one host. A web app lets designers author and SIMULATE. Designers never write code,
  components, [TlState] structs or ComponentLookup. Any entity can pass to any other (hand heals body, shield curse lands on player).
- Owner preferences learned the hard way (also in memory and unity-cli-skill rules 17, 18): TALK FIRST and understand before building or
  filing; never guess; know what a project IS before touching it; no issues from chat guesses (issues come from agent findings; manager
  opens/closes/reopens them itself); use OPUS agents; the owner reads code directly and judges by reading; no comments in production code;
  code first, tests and benchmarks later (their order for the v3 coding phase); do not make them relay between agents or senior.

## 2. Authoritative documents (paths)

- `D:\GitHub\_work\plan.md` = the senior's design (17 sections, corrected 2026-11-12, plus the manager addendum appended at its end).
- `D:\GitHub\_work\senior-answers.md` = senior's 15 black-magic answers (lane primitives, no 64-bit SIMD, no compaction in fused kernel,
  pre-build emitter, permutation test, pre-mortem, doubts, "stats as fixed-point" = answer 15).
- `D:\GitHub\_work\ALL-IN-ONE-sv3.md` = the one-file brief given to the senior (requirements R1-R18 in sections 0-7, then source appendices).
- `D:\GitHub\_work\agent-code-brief.md` = standing code style authority for every agent (written by the owner's other session).
- `D:\GitHub\_work\sv-proto\BRIEF.md` (phase 1/2 rules), `BRIEF3.md` (v3 coding phase rules, wave 2 addendum with D11 and file ownership).
- `D:\GitHub\_work\research\notes-statesmith.md`, `notes-quantalib.md` (+ clones of StateSmith, StateSmith-examples, QuanTAlib).
- unity-cli-skill (pushed, main 0780495): `skills/unity-cli-field-notes/references/deterministic-simd-state-engines.md` and
  `sv-v3-architecture-and-process.md`; SKILL.md rules 17, 18.
- `D:\GitHub\_work\sv-proto\VERIFY.md` = phase 1 verification. For Honor GDC talk (JgSvuSaXs3E) lessons: section 8 below.

## 3. Repos and branches (sv checkout D:\scratch\plan-connect\sv, private github IAFahim/sv)

- sv `main` = origin/main = c0a3ae9 (phase 1+2 + issues). Pushes to main were approved by the owner for the verified phase 1/2 work only.
  NOTHING of v3 is pushed. No tags/releases/force-push/history-rewrite are approved.
- v3 worktrees under `D:\GitHub\_work\sv-proto\`: `v3-int` (branch v3/int, HEAD f30a118 = wave 1 merged + skeleton 03254cc + C3 stats),
  `v3-a1-lanes` (a87c364), `v3-a2-records` (f9eb431), `v3-b1-reference` (48ccc46), `v3-c3-stats` (f30a118), `v3-c1-compiler` and
  `v3-c2-host` (both still at skeleton 03254cc: no work saved). Older phase 1/2 worktrees (a-core, b-portable, c-unity, d-frame, int, p2-*,
  verify*, tl) are history.
- v3/int builds: `dotnet build src/Sv.Pack/Sv.Pack.csproj -c Release` = 0 errors 0 warnings (net10.0 and netstandard2.1 for Sv.Core/Sv.Rules).
  NOTHING HAS BEEN RUN: no tests, no scenarios, no benchmarks, no digests (owner's order: write all code first).

## 4. What exists on v3/int (about 460 KB new code + 1,800 lines stats)

Sv.Core (C# 9, netstandard2.1 + net10.0): Layout, Lanes (one address function, dense = identity table), LaneMath (~20 lane primitives,
scalar bodies + Vector256 under #if NET), LanePool (2 MB segments), World, Journal (ring 32 epochs), ChunkImage, Digest, InputLog (records +
spawns + kills + StatOps), Replay, Effect (16-byte record + plain-fields oracle form), Arena, Seal (parallel), Route; Stats/ = StatLaw
(integer law), StatOp, StatDefs, StatMachine (+Modifiers, Recalculate, Undo). Kept files linked from src/Sv (Bits, CellDigest, Memory,
NativeArena, XxHash64). Sv.Rules: Pack (Json, Schema, Expression, Rule, PackReader/Writer/Plan = validation) and Reference (Interpreter,
Law, Records, State, Trace) = the slow oracle. contract/CONTRACT-v3.0.0.md and CONTRACT-v3.0.1-integer-stats.md (owner-approved by "go").
Skeleton projects only: Sv.Host, Sv.Pack (Program.cs returns 0).
packs/reference/*.sv.json = worked example + R8-R10 sentences as rows (never run).

## 5. Decisions (owner said "use the senior's plan fully")

D1 one address function; shared lanes default, dense = identity table. D2 16-byte record (plain-fields form kept as oracle; key pack/unpack in
one pair of functions). D3 ring 32 epochs of 2 frames at 30 Hz. D4 sum then clamp once. D5 sv-only replay. D6 gate 8.3 ms at 1M entities and
1M records at 30 Hz (60 Hz reported only) = NOT measured yet. D7 contract v3.0.0 approved. D8 sourced timeline effects = status entity.
D9 outward event channel 16,384. D10 arena full: grow once and rerun (hard cap for browser). S1, S2, G2, G3 adopted as written, each behind
one function. D11 (owner liked it, then "use the senior's plan fully"): STATS ARE INTEGERS with designer-chosen decimal scale, percentages in
basis points (10,000 = 100%), int64 intermediates, truncating division, saturation to int32; floats only at host display edge. Old float stat
machine in src/Sv and its pins (A2ACAA69E55F79D8, 2B43BC1549AAE751, soak 6D6C602BF07E9366) stay untouched as legacy until M11.
Pending owner confirmations (readings the code follows, issue 0180 etc.): what a designer types for "heals twice" (stat math is 1+v: `Multi 1`;
owner may prefer typing x2); "every damage you take, heal 5" per frame/source/record/hit (a field of the rule); one-frame delay for
reflection/triggers aimed at others (S2).

## 6. Agents and what is left (milestones M0-M11 of plan section 14)

Done (code written, builds, unrun): M0 contracts, M1 lanes, M2 records/Seal, M3 reference interpreter (scenario runner/format missing),
M6 journal/log/replay, M7 integer stat machine + oracle integer fold.
NOT written: M4 compiler = C1 (passes 01_Bind..10_Validate2 in Sv.Rules/Passes, Emit Values+Kernels via a pre-build tool writing real .cs,
IR, partitioning). M5 = C2 (Sv.Host Frame/Workers/Net front doors/Collect, Sv.Pack tool `sv pack|run|trace`, scenario format and runners,
benchmark entry points). Both agents were stopped to save usage: C1 agent id `ac7d0747f9d0221f7`, C2 agent id `aafd483ec701d0dfd`
(resume with SendMessage; they must first `git merge v3/int` to get StatLaw/pack changes; C2 duties: call Kill on slot release,
MarkEpoch after each epoch mark, Rewind with the world's rewind, fill template stat lanes from FoldTemplate). A one-shot cron job
`3af62024` (session-only) fires Nov 13 01:51 local and does exactly that resume. If the session restarted, redo it by hand.
Later (not started): M8 Unity adapter v4, M9 tl glue/NorthStar3, M10 web designer + simulator, M11 deletions of old adapter/code, M12 reserves.
After all code exists: TEST PHASE (scenario 1 must print health 95; permutation test; laws; compiled-vs-interpreted differential; ISA legs;
equal digests at 1,2,4,7,11 workers; 0 B warm) then BENCHMARK PHASE (section 15 of plan; kill rules in section 14).
Plan: after tests, integrate by merging branches into v3/int, resolve csproj conflicts (append-only), remove duplicate linked files, fix the
CS0433 duplicate-type trap (Bits/CellDigest/Memory/XxHash64 exist in both Sv and Sv.Core; extract IntrinsicLaw into its own file in src/Sv was
decided but NOT done).
Agent hygiene: Opus agents only; 4 concurrent agents burned the 5-hour limit twice; run at most two big agents at once; every agent gets
BRIEF3.md + agent-code-brief.md + plan.md; code only compiles with zero warnings; file ownership per agent; skeleton commit before parallel work.

## 7. Open findings that need decisions or fixes (all in repo issues; do not file new ones from chat)

Issues 0160 (lane table stride = pack lanes + 2 meta lanes, kernels use WorldView.LanesPerChunk; JournalEntry 8 bytes; Rewind(e) = state at
start of epoch e; LaneMath.BoundOf restates Clamp law, needs a test pinning equal; slot 2^22-1 reserved), 0170 (IntrinsicLaw twin Saturate32;
overflow merge level 1 conserves totals; hard-cap rule discards a whole layer; 24-byte layout not built), 0180 (8 plan readings: Multi, card
field, overflow, depth rule, fold timing, event re-check, per Source/Frame emits, state-only gates), 0210 (oracle and stat machine use
different stat-op models; floats remain in the rule language; no row form for derived stats; CS0433 clashes). Frame-loop rule: worker writers
are absorbed into the journal before any main-thread structural op. Segment pointer table is a fixed 4 MB per world. Senior caveats to verify:
RyuJIT size thresholds, Zen 4 compress-store penalty, three Burst behaviours (float mode per entry point, Loop.ExpectVectorized, noalias).
Older open sv issues: 0146 (modifier clips from Burst), 0147 (events start timelines), 0148 (designer demo), 0149 (tl kind job 8.45 ms at 1M over
budget, owner decision), 0150 (Mono/IL2CPP unproven), 0151 (SV_CHECKED test, schema event cap, defaults), 0152 (re-scoped), 0154 (core model =
the owner's requirement). 0145 and 0153 are closed (withdrawn/merged).

## 8. Lessons from the For Honor GDC talk (Aurelie Le Chevalier, "Data-Driven Dynamic Gameplay Effects on For Honor", 2017)

Their system: handler per entity with modifier entries (start time, origin, condition state, pointer to data); effects of 5 kinds (stat by percent
from base, disable capability, add tag, remove modifiers, create new effects); duration + conditions (and/or/not); modifiers apply to other
entities (origin). Polling ~3,000 queries/frame (about 200 soldiers + 8 players), ~700 modifiers total, max 6 entries per soldier, 30 per player,
bit field of touched stats skips queries (about 80% saved). Rewind worked because the simulation is deterministic with a history buffer.
Lessons we have NOT yet applied (owner was asked, no answer yet): (1) stack core modifiers first, then external ones (easy mode -80% then +50%
of 10 = 3, not 7): add an origin layer to Shape; (2) snapshot the attacker/victim modifiers when a damage-over-time effect activates and use the
snapshot for later ticks; (3) keep it simple: no node-driven graphs, designers got lost, a dedicated editor was their top wish: our chains of
chains and 10,000 rules need the same caution; (4) feedback hooks (alive, active, effect applied) and a detailed debug display were decisive:
build a minimal trace viewer EARLY (before Unity work); (5) Excel-style bulk export/import for balancing; (6) status effects with power levels to
reduce duplicated data; (7) they avoided priority conflicts (capabilities are only disabled, disable wins; healing removes damage-over-time);
our priority-number gates are more powerful and riskier. Where we differ: they ran ~200 entities, we target 1,000,000 with bit-exact compiled code.

## 9. Immediate next steps for the manager (in order)

1. At 01:51 (cron 3af62024) C1 and C2 resume; check they merged v3/int first. If usage limit hits again, stop and retry later; at most two big agents.
2. When C1 and C2 report: merge into v3/int (resolve csproj by appending), build everything, fix compile errors, extract IntrinsicLaw, handle CS0433.
3. Ask the owner for the pending confirmations in section 5 and the For Honor items in section 8 (core/external stacking layer, DoT snapshot,
   early trace viewer) before the test phase; apply them as plan/code changes via agents.
4. Test phase (health 95 first), then benchmark phase, then M8-M11.
5. Keep the skill repo updated (dense, no secrets); close/reopen repo issues myself with receipts; tell the owner exact refs before any push.

## 10. Added late on 2026-11-12 (owner said "you may")

Acceptance test for the brick set ("Lego test"): write about 20 real mechanics as rule rows in `packs/reference` (poison over time, shield, lifesteal,
thorns, immunity bit mask, stun, double jump, cooldown, crit, execute, revive, reflect with immunity, heal over time, stacking buffs, DoT snapshot,
core versus external stacking, ...). A mechanic that cannot be written is a missing brick; two bricks that can express the same thing are a candidate
for deletion. Time (timers, ticks) is tl's job, space (areas, auras) is grid-influence's, AI and movement are not sv's. Run it on the reference
interpreter first. Start it AFTER C1 and C2 report (not part of the 01:51 cron prompt). Owner is out of usage limit tonight; nothing else starts before 01:51.

## 11. Update 2026-11-13 02:15 (code phase complete for M4/M5)

C1 (v3/c1-compiler 8d91ef2, 8791693) and C2 (v3/c2-host 03dcd7f..ae935f1) are merged into v3/int; integration commit 63b3b33 aligns Decide (capacity, per-part 32 B per record scratch) and EdgeEmits (capacity, chunk scratch) between host and emitter. Sv.Core, Sv.Rules, Sv.Host, Sv.Pack, Sv.Bench build in Release with 0 warnings and 0 errors. `sv pack packs/reference` emits Values/Kernels/HostKernels/pack.svb (PackHash BD70627B1C671888). `sv run` on the reference interpreter gives P health 95, S2 75 next frame. Nothing is a test yet: compiled == interpreted not checked.
Next: test phase (compile generated kernels, `sv run --host compiled` vs reference, laws, permutation, digests at 1/2/4/7/11 workers, 0 B warm), then the brick-set acceptance test, then benchmarks.
Open owner decisions: rewind carry vs journal derived records; stat-op model (issue 0210 item 1); float literals only for ExprType.Float; template numbering assumption (world template index == pack order); IPackKernels is net10-only (Unity binding in M8).

## 12. Update 2026-11-13 (test phase 1 done)

Merged on v3/int (head after be3b9f7 merge): Sv.Laws.Tests 341, Sv.Scenario.Tests 80, Sv.Diff.Tests 12, all green on default, DOTNET_EnableAVX2=0 and DOTNET_EnableHWIntrinsic=0. Issues 0300, 0310, 0311, 0312, 0313 closed with receipts (Seal overflow merge drops zero groups; absorb fold scans NonZero(delta); reference stat ops many per source; card terms in StatMachine; SelfAdmit unused var; kind of unborn source). Compiled == reference on 300 random packs (no absorb ordering aside), digests equal at 1/2/4/7/11 workers, 16/64 parts, shared == dense. Pin so far: worked example E0F445186E2C3672 (asserted).
Not covered: netstandard2.1 and Burst legs, host-level 0 B warm with cards, cards with rewind at host level, event streams, exclusive-group gates, `total` operator, Seal Moved path, StatMachine laws other than cards, rewind-undoes-spawns question (owner), RawArena double interval count (check in bench phase).
Next: brick-set (20 mechanics) acceptance test, remaining coverage, then benchmarks (plan section 15).

## 13. Update 2026-11-13 (Lego test and coverage merged)

v3/int 8103786: Sv.Laws.Tests 376, Sv.Scenario.Tests 80, Sv.Diff.Tests 12, Sv.Host.Tests 27, Sv.Lego.Tests 90 all green; ISA legs default, no-AVX2, scalar equal; netstandard2.1 builds of Sv.Core and Sv.Rules have 0 warnings and pass 326 of 376 laws (50 need Sv.Host). Host-level 0 B warm confirmed at 1 and 7 workers with cards; events and cards equal across 1/2/4/7/11 workers.
Lego test (docs/LEGO-REPORT.md, issue 0330): 22 mechanics, 13 clean, 7 awkward, 2 not sayable (DoT snapshot, core vs external stacking). Brick work running as agent B1 on branch v3/b1-bricks (assign modes, amount from cell, per-count and cell-driven modifiers, deterministic roll, spend gate; contract/CONTRACT-v3.1.0-bricks.md). Later: stat layers or derived-stat row (HostPack passes no stat sources, issue 0210 item 3), then redundant-brick removal, then benchmarks (run when no other agent loads the CPU).
Owner readings still to confirm: per-Hit counts offered hits (heal 5 per hit gives 55 not 45), card modifiers once per held card, DoT ticks carry no source, rewind undoing spawns, rewind carry vs journalled derived records.

## 14. Update 2026-11-13 (bench done, weekly usage nearly out)

Bench (docs/BENCH-REPORT.md, v3/int d68abde): A 5.07 ms pass; B 24.21, C 49.74, D 68.94 fail the 8.3 ms gate; Decide is 68 percent of B (75 ns per record at one worker, plan 4-8). Memory at rest 41-46 B per entity, 0 B warm, rewind and 10k-rule compile pass. Open: 0360 Decide kernel, 0361 scratch, 0362 Seal over M2 gate (kill rule: stop and report, reported to owner), 0363 input log memory, 0364 emitter empty forward switch, 0365 raw arena intervals.
K1 (Opus, branch v3/k1-decide) is the last big agent: Decide kernel restructure plus 0361 and 0364. Remaining work is written as briefs for smaller agents in D:\GitHub\_work\sv-proto: BRIEF-small-1-seal.md, BRIEF-small-2-stat-layers.md, BRIEF-small-3-remaining.md (order, readings to confirm, push is the owner's call).

## 15. Update 2026-11-13 (K1 merged)

v3/int 123ad3a: Decide kernel restructured (K1). Bench totals ms before to after: A 6.83 to 3.07, B 31.36 to 10.65, C 58.01 to 30.80, D 77.13 to 43.06; digests unchanged; suites Laws 516, Scenario 80, Diff 18, Host 27, Lego 119 green. Gate 8.3 ms: A passes, B 10.65 close, C and D fail. Floor and causes in issue 0370 (random source reads miss cache: needs source attributes at Seal or a per-frame source table). Issues 0361, 0364 closed.
Gotcha: after the host/kernel surface changes, kernel test projects keep stale generated files: build src/Sv.Pack first and delete tests/Sv.Kernels.*/obj/SvPack, then rebuild.

## 16. Update 2026-11-13 (P1 merged, last big agent)

P1 merged: Seal writes the canonical buffer directly (B 11.21 to 9.60 ms, C 31.08 to 30.0, D 43.28 to 41.7), digests unchanged, all suites green on default, no-AVX2 and scalar. Per-frame source table was exact but slower (reverted); issue 0370 overstated source misses. Gate 8.3 ms: A passes, B 9.6 close, C and D fail. Remaining ideas: skip order check for pre-bucketed blocks, touched-chunk count matrix (0.15-0.4 ms), B Decide per target 27 ns, C/D random sort, 0363 input log cap. Remaining briefs for smaller agents: D:\GitHub\_work\sv-proto\BRIEF-small-1-seal.md (partly done), -2-stat-layers.md, -3-remaining.md. Unity 7 facts for M8 (owner paste): netstandard2.1/C# 9 today, net10 TFM only later; statics and worker threads survive play mode, static ctors may run on background threads, stop workers on code reload; Burst function pointer delegates public or internal; no managed exception across native frames.

## 17. What to do next (unambiguous)

NEXT TASK: S2 stat layers, brief D:\GitHub\_work\sv-proto\BRIEF-small-2-stat-layers.md. Do this one. Do not ask which brief.
BRIEF-small-1-seal.md is mostly DONE by agent P1 (see section 16); remaining Seal gain is under 0.4 ms; skip it unless the owner asks.
After S2: BRIEF-small-3-remaining.md item 4 onward. Push of v3/int is the owner's call, never the agent's.

## 18. Update 2026-11-13 (S2 merged)

S2 stat layers and derive rows merged on v3/int (1f12954, contract/CONTRACT-v3.2.0-stat-layers.md, issue 0390 readings). Mechanic 17 now gives 209. Suites: Laws 548, Scenario 80, Diff 20, Host 27, Lego 120, all green; all old digests unchanged. Next: BRIEF-small-3-remaining.md item 4 onward (redundant bricks only with owner agreement, 0363 cap, 0365, skill notes, AGENTS.md layer table). Push is the owner's call.
