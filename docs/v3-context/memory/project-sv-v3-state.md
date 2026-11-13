---
name: sv-v3-state
description: "Where sv v3 stands (branches, merged agents, pending owner decisions) and the pointer to the full handoff file"
metadata:
  node_type: memory
  type: project
  originSessionId: 383aeffb-e16f-4f69-b907-8b8946cb0cce
  modified: 2026-11-13T08:12:04.675Z
---

Full handoff: `D:\GitHub\_work\HANDOFF-sv-v3.md` and `docs/V3-HANDOFF.md` on v3/int (section 11 is the latest). Plan authority: `D:\GitHub\_work\plan.md`; senior's answers `senior-answers.md`; briefs `D:\GitHub\_work\sv-proto\BRIEF3.md` and `agent-code-brief.md`.

State as of 2026-11-13 02:15 (Asia/Dhaka): sv main = origin = c0a3ae9 (phase 1/2 only). v3/int (worktree `D:\GitHub\_work\sv-proto\v3-int`) now holds the whole code phase: Sv.Core, integer stats, Sv.Rules pack and reference interpreter, compiler passes and emitter (C1), host frame loop, workers, pack tool and bench entry points (C2), plus an integration commit aligning the Decide and EdgeEmits signatures. All five projects build with 0 warnings. Nothing of v3 is pushed. No tests have been run yet except a smoke: `sv pack packs/reference` emits, `sv run` reference gives health 95 / 75.

Update 2026-11-13: test phase 1 done on v3/int: 341 law + 80 scenario + 12 diff tests green on default, no-AVX2 and scalar; compiled equals reference; issues 0300, 0310-0313 closed (handoff section 12). Next: brick-set (20 mechanics) test, remaining coverage (netstandard2.1, Burst, host-level 0 B warm), then benchmarks. Pending owner answers: heals twice (Multi 1 vs x2), unit of "every damage you take", one-frame delay S2, For Honor lessons, rewind carry vs journalled derived records, stat-op model (issue 0210 item 1).

Update 2026-11-13 later: bench done (A passes, B/C/D fail the 8.3 ms gate, Decide kernel is 68 percent; handoff section 14). Weekly limit nearly out: K1 (Decide kernel, branch v3/k1-decide) is the last Opus agent; the rest is written as briefs for smaller agents in D:\GitHub\_work\sv-proto\BRIEF-small-1-seal.md, -2-stat-layers.md, -3-remaining.md. Nothing is pushed; push is the owner's call.

**Why:** the owner lost a day when agents built sv without knowing what it is; everything must be recorded so compaction loses nothing.
**How to apply:** start sv work by reading the handoff; Opus agents only, at most two big agents at once; talk before building or filing issues; never push without telling the owner exact refs. Related: [[know-the-project-before-working]], [[own-issue-lifecycle]].
