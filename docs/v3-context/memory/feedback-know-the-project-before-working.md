---
name: know-the-project-before-working
description: "Understand what a library is for and how it stores/moves data (from AGENTS.md, README, owner) before touching it or briefing agents; never guess"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 383aeffb-e16f-4f69-b907-8b8946cb0cce
  modified: 2026-11-12T11:29:30.929Z
---

Before editing a project or managing agents on it, I must know fully what it is, what it stores and where, how it works, and what the owner wants to build with it. Get it from AGENTS.md, README, design docs, code and the owner. Never guess or assume; a green plan checklist does not prove the owner's intent.

**Why:** 2026-11-12, sv phase 1 and 2: I managed agents on sv without understanding it (I did not know intrinsic values live in host columns, or what the owner wanted sv to be). The owner said I "ruined my day" and asked that this rule go in unity-cli-skill in general terms, not as an sv checklist.

**How to apply:** unity-cli-skill field notes rule 18. If a project's AGENTS.md does not say what it is building, stop and get the owner's statement. Agent briefs carry that understanding. Talk first, no guessing; see [[own-issue-lifecycle]].

Owner's stated intent for sv (2026-11-12, their words, not yet written into sv's AGENTS.md): all stats, intrinsics and events of an entity live in ONE contiguous block (performance, SIMD, cheap save/load/rollback of whole chunks for multiplayer, load and unload chunks instantly); no strings: names become generated enums; every entity has default intrinsics and stats it starts with and they change in play; an entity (player, hand, shield) can pass to or read from another entity via jobs that resolve in a simulation pass. Designers define the values; nobody writes a component per value.
