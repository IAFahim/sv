---
name: feedback-orchestrate-push-main
description: "How IAFahim wants work run on the tl, tl.unity and unity-cli-skill repos - orchestrate with agents, push straight to main, local first, CI not a priority"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 6290b447-5746-48e4-9bce-bb7f2ae2e362
  modified: 2026-11-06T10:54:05.055Z
---

Act as a manager and orchestrator: delegate real work to agents sized to the job (Opus for perf/diagnosis, Sonnet or Haiku for mechanical work), and avoid doing the work or ping-ponging with agents yourself.

The owner authorized pushing directly to `main` of IAFahim/tl, IAFahim/tl.unity and IAFahim/unity-cli-skill, for current and future work (stated 2026-11-06). Branches are not protected.

**Why:** the owner wants to use the library the same day, wants minimal token spend, and treats CI and runner work as not a priority.

**How to apply:**
- Finish and test locally first (Unity a16 is the baseline, never install older editors), then push to main; do not chase CI unless asked.
- Public tags/releases were denied by the permission classifier once; ask the owner for an explicit yes before creating them.
- Start EVERY agent brief with: pull D:\GitHub\unity-cli-skill (`git pull --ff-only`) and read SKILL.md first; agents push their learnings back to it before finishing. The repo is shared by several PCs, so rules live in the skill repo, not in this memory.
- Keep replies very short when the owner asks for "20 words" mode.
- Related: [[unity-cli-skill-workflow]] (the same workflow is written into the skill's section 0b).
