---
name: keep-going-dont-ask
description: Owner wants the next planned step started without asking when the direction is already known; ask only for real decisions
metadata:
  node_type: memory
  type: feedback
  originSessionId: 383aeffb-e16f-4f69-b907-8b8946cb0cce
  modified: 2026-11-13T03:12:38.220Z
---

When the plan already says what comes next (test phase, Lego test, coverage gaps, benchmarks, merge and fix of agent findings), start it and report afterwards. Do not end a turn asking "start X now?".

**Why:** the owner said "keep going without asking next time. Like you already know what we want" after I asked whether to start the Lego test; they have given the whole direction (code, tests, benchmarks) and each ask costs a round trip.
**How to apply:** continue through the known sequence on my own, one step after another, merging, fixing bugs found by agents and relaunching agents (Opus, at most two big at once). Still stop and talk for genuine design choices that change what designers can say (new bricks, semantics of readings) and never push or create issues from chat without telling the owner. Related: [[sv-v3-state]], [[own-issue-lifecycle]].
