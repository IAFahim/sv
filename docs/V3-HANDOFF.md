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

## 19. Update 2026-11-13 (small agent merged)

v3/s3-remaining merged (deb452d): sv AGENTS.md with layer table, issue 0363 (per-frame input log cap, off by default) and 0365 (raw arena counts one interval per layer) closed with pinned verdict tests. Suites: Laws 556, Scenario 80, Diff 20, Host 29, Lego 120 green; bench digests unchanged. Open: M8 Unity adapter (big agent), M9 tl glue, M10 web tool, M11 deletions; speed gate C and D; redundant bricks (owner agreement); readings to confirm.

M8 progress (v3/m8-unity, Linux PC, 2026-11-13): Unity 7000.0.0a16 compiles with 0 error CS, 0 warning CS, 0 error BC; Burst runs all 11 frame phases as jobs with digests equal to the .NET host at 1 and 11 job workers over all 31 pack scenarios plus a SealCompact case (tests/Sv.Twin.Fixtures journals); 76 EditMode tests green; authoring window and its generated-files test done. Fixed along the way: engine-sync sort order under non-C locales, LF pinning for pack files (Rules.map.txt offsets). Real git clone gate passes (76 of 76). Next: M9. Details in docs/M8-HANDOFF.md.

M9 progress (v3/m8-unity, 2026-11-13): tl glue and status-as-entity done on .NET, requests not started.
- `sv pack <pack> --glue tl` writes Glue.g.cs from `Sv.Rules.Emit.TlGlueEmitter`: per channel marked `timeline` a `[TlState] <Channel>Out` outbox column, a `[TlKind] <Channel>Feed` (Combine total, Apply adds to the outbox), a `[TlClip] <Channel>Clip` and an `ITlTrack` with `[Feeds]`. The shapes compile against tl main as it is (no tl change; tl repo untouched). The plan's appendix shapes (Frame<,>) were outdated, the NorthStar2 shapes (TlFrame, ITlTrack) are the real ones.
- `Sv.Host.Outbox.Send(host, cells, channel, kindLane, targetLane)` turns non-zero outbox cells into `Send(target, source = slot, kind = row's Kind, Origin.Timeline)` in ascending slot order and zeroes them. The target lane holds slot+1, 0 means the row itself (a zero cell is nothing everywhere, and slot 0 is a valid victim). A status entity gets its victim with an assign-mode channel (`Aim` into `Target`, mode set).
- Frame order per host frame: tl Tick and `TlAuto_*.Run`, Outbox.Send per timeline channel, `FrameLoop.Step`. A status entity must be aimed one frame before its timeline starts.
- `tests/Sv.NorthStar3` (pack in tests/Sv.Kernels.NorthStar3/pack = reference pack plus Target, Aim, three status templates): poison status ticks reach the victim as timeline records; a sniper_ward victim drops a sniper status's ticks and not a rifle status's; collected ticks equal the same records sent directly; digests equal at 1, 2, 4, 11 workers; a fresh world fed only the causes replays every digest; committed Glue.g.cs equals the emitter output. 9 tests. NorthStar and NorthStar2 unchanged and green (pins untouched).
- Requests (sv to tl) need no new rule action: an event row may carry `"starts":"<timeline>"` (values.sv.json, events only, omitted when empty so no existing PackHash moved). The emitted `TimelineRequests` (Glue.g.cs, only when a pack has such an event) resolves timeline names once with `TimelineWorld.Find` and, per outward event of a frame, calls `PlaybackTable.Start` on enter and `Stop` on leave, in the host's event order. NorthStar3: LowHealth starts the poison timeline on the entity that went low (4 ticks, replay and 1/2/11 workers equal); `starts` on a non-event is an error; it round-trips through PackWriter. 16 NorthStar3 tests. The Unity half of the M9 gate (same digests in Unity) needs tl in the Unity project; the glue is plain C# and the engine package carries the emitter and Outbox.

M10 progress (v3/m8-unity, 2026-11-13): web designer built and measured, see docs/WEB.md. Worked example traced in the browser with digest E0F445186E2C3672, first simulate 54 ms, 5.8 MB (gates: pinned digest, 5 s, 30 MB). 6 tests in tests/Sv.Web.Tests (pins, canonical fixed point, diagnostics, scenario errors, Unity files from canonical output equal the raw edit except the offset map). Needed a user-local SDK with the wasm-tools workload (system SDK state is root-owned). Not done: a pack edited in the browser loaded into a real Unity project (the .NET equivalence is tested); AOT build (not needed, interpreter runtime meets the gates).

M9 Unity leg (2026-11-13): the sv half of the tl chain is proven equal in Unity. `Sv.NorthStar3` records every call the glue makes on the host (spawn, direct sends, the `Origin.Timeline` sends Outbox produces, per-frame digest) as twin journals under `packages/com.sv.unity.v4/Tests/Editor/Twin/northstar3` (`SV_UPDATE_TWIN=1 dotnet test tests/Sv.NorthStar3` rewrites them) and Unity replays them through the generated Burst jobs at 1 and 11 workers: 91 EditMode tests green, every per-frame digest equal to the .NET host. Not proven: tl itself running in Unity (Timeline assets, baked SubScene, tl.unity kind jobs feeding the outbox columns); that needs a tl.unity project with authored Timeline assets and is the remaining part of the M9 gate. tl's own Unity = .NET equality is its own pinned hashes, untouched.

P2 merged (v3/m8-unity, 2026-11-13, agent branch v3/p2-perf, 5 commits, Opus): Below as plain 64-bit products, whole-chunk row masks for own-lane event conditions (Mask.Below, AVX2 + scalar), Target specialised on a lone channel, digest liveness cache. Ms before to after, same session: A 3.23 to 2.82, B 9.68 to 7.69, C 30.63 to 28.53, D 43.04 to 41.23; digests unchanged (A DAF3A1D9BD73BA10, B 6E7618BF0900D3F8, C D3C714D46555B7CE, D 7413F619472566C4). Gate 8.3 ms: A and B pass, C and D fail (Decide compute-bound; Seal, Log, Send bandwidth-bound; five more ideas measured under 3 percent and reverted, listed in BENCH-REPORT Update P2). Verified on the merge: Laws 556, Scenario 80, Diff 20, Lego 120, Host 29 on default, AVX2=0 and HWIntrinsic=0; Twin.Fixtures 32, NorthStar3 18, NorthStar2 13, NorthStar 27, Web 6, Portable 390; engine-sync check; Unity gate 91 of 91 with 0 error CS, 0 error BC. Open: issue 0371 (Host any-thread allocation test flakes at 168 B, also on the pre-P2 build), C and D gate (0360, 0362, 0370).

Correction to the P2 gate line (2026-11-13): the gate is "up to 1M records per frame", and only the pre-bucketed Tick shape B passes (7.69 ms). The random shape `r` (1,000,000 random cross-entity records, 1,165,758 canonical, digest 9CFD52083A9DA3FB) is inside the gated envelope and measures 22.4 ms (send 0.79, seal 4.78, log 1.26, decide 10.45, commit 2.08, events 0.16, digest 2.36, end 0.27). So the gate is open for random records. C (2M records) and D (3M) exceed the envelope and are reported, not gated. Work on it: branch v3/p3-perf (Opus agent, reserves M12a/M12b and the source-locality ideas in issue 0370). In parallel: issue 0400 (tl running in Unity via the plain host path).

M9 Unity leg closed (issue 0400, 45f0e5c, 2026-11-13). tl now runs inside Unity on its plain host (`packages/com.sv.unity.v4/Tests/Tl`, docs/UNITY.md "tl in Unity"). The glue's poison timeline writes the `PoisonOut` outbox, and `Outbox.Send` collects it into the SvWorld. Every per-frame digest of the five NorthStar3 journals equals the .NET host at 1 and 11 job workers, including pins `A566FAEC8837BA57` and `2D6A06776AE25DCB`. Unity gate with com.tl.unity: 101 of 101, 0 error CS, 0 warning CS, 0 error BC. Without it: 91 of 91, and the tl assemblies are skipped. NorthStar3 .NET: 21 of 21. No tl change. The ECS/SubScene path is the open follow-up.

Decision D20 (2026-11-14, plan.md section 17 table): r is a stress benchmark, not a gate; ceiling 19.0 ms, must not regress. The 8.3 ms gate applies to chunk-bucketed load (A 2.4-2.8 ms, B 7.7-7.8 ms: both pass). Final P3 numbers and the senior consult results are in docs/BENCH-REPORT.md (Update P3) and issue 0374.

2026-11-14 later: fresh `git clone` of v3/m8-unity into a brand-new Unity project (no Library) passes the Unity gate 101/101 with com.tl.unity (Unity package cache warm). Allocation test flakes fixed (issue 0371 closed: tests measure sv threads, not the test host). Pack build regenerates when the emitter changes (build/Sv.Pack.targets).

2026-11-14, v3-only repository (owner: "we don't need legacy or old things"): the sv 2.1 engine and everything that only served it were removed (src/Sv, Sv.Compose, Sv.Frent, Sv.Burst, Sv.Generators; tests Sv.Tests, Sv.Fuzz, Sv.NorthStar, Sv.NorthStar2, Sv.Unity.*, Sv.Portable.Tests, Sv.Editor, Sv.Burst; samples/Sv.Samples; packages com.sv.core and com.sv.core.tests; the legacy benchmarks; eng/unity-sync.sh, eng/isa-legs.sh; the bench workflow that refreshed the old README). The five files v3 shared with it (Bits, CellDigest, Memory, NativeArena, XxHash64) now live in src/Sv.Core/Kept. Contracts v1.0.3 and v2.1.0 stay because v3.0.0 amends v2.1.0; issues stay as history. Same day: `sv pack` emits `PackData.g.cs` (the pack as code) for .NET too and a `Template` enum; its skip key includes the tool's and the generator's build ids (no more stale kernels after an emitter change); `build/Sv.Pack.targets` runs after project references resolve (no race with the tool build); `-p:SvChecked=true` adds SV_CHECKED without dropping the SDK constants; sv.slnx now holds every v3 project and CI checks out tl at the pinned commit, so the determinism workflow tests v3 on six hosts; README is a course in tl.unity's style with `samples/Sv.Hello` as chapter 1, its output pinned in CI.

2026-11-14, fuzzing: `docs/FUZZING.md` and `eng/fuzz.sh` (PR 96) let small agents fuzz the differential suite with `SV_FUZZ_OFFSET`. The first agent campaign (agent-1, offsets 10000000-10240000, four modes, 21,000 packs per mode) found issue 0401: the presence pre-guard of `per: Frame/Source` emits intersected the channel sets of different `taken`/`offered` conjuncts, so the emit could be skipped (dead when the sets were disjoint). Fixed in PR 97 (3489b9b): one presence test per conjunct, ANDed; 6-case regression in `HandwrittenTests`; no shipped pack kernel changed. Next fuzz work: section 4 harnesses (send order, rewind/replay, malformed packs, round trip). `eng/player-smoke.sh` can now target Windows (`SMOKE_TARGET=win64`) and IL2CPP (`SMOKE_BACKEND=il2cpp`); this Linux editor has no IL2CPP variation, so those legs run on the Windows machine.

2026-11-14, red team (PR 101): an Opus red-team agent ran 54 attack tests with real game mechanics at extremes (`tests/*/RedTeam/`, index `issues/0500-redteam-report.md`): 32 attacks survived, 10 findings (0501-0510, 4 critical), all fixed in the same PR. The public behaviour changes a game can see: `Rewind` drops the input log after the rewound frame (rollback by hand); `Send` is single-threaded per frame and throws for a second thread (`ExternalWriters` + `Sender(i)` for threads); `Send` grows the raw arena under `Grow` and throws for kind or channel above 255; Seal drops records on unknown channels or out-of-world sources (counted in `RouteFaults`); `Resolve` throws when the input-log cap dropped records it needs; packs with names that collide in C# (templates too) or become `value__` are rejected at plan time; calls after `Dispose` throw `ObjectDisposedException`. The reference now orders RemoveSource before Add and applies the level-4 arena cut. Unity 98/98 with tl. Open: re-measure B on a quiet machine (Seal validation cost about +0.2 ms measured under load).
