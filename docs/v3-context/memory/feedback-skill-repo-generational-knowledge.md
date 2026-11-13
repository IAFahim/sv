---
name: feedback-skill-repo-generational-knowledge
description: "Owner wants every discovery, golden technique and dead end pushed densely into IAFahim/unity-cli-skill so later agents become experts without bloat"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 40912814-b185-4771-a0f3-95ea3d839533
  modified: 2026-11-11T19:03:38.087Z
---

Owner (2026-11-12): the unity-cli-skill repo must be kept fully updated with ECS (newest Unity 7 Entities), Burst/C# performance discoveries, how-to recipes, measured wins and dead ends, "generational evolution" style: each run leaves the skill better so the next agents save time and money and stop re-learning. Gold finds are marked and pushed. Agents may decompile Unity source and use any tool to overcome blockers.

**Why:** repeated agent runs waste tokens re-deriving the same facts; the 5h usage limit makes cut-off runs likely (so push learnings as you go, not only at the end).

**How to apply:** put this in every agent brief; merge into existing lines instead of appending, delete stale lines, one verified fact per line with version/sha; push to skill-repo main early and often (pull --rebase, never --force). Never write the PC admin password or any secret into memory or the public skill repo. Related: [[feedback-orchestrate-push-main]], [[feedback-latest-unity-only]].
