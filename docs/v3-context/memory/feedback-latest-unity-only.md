---
name: feedback-latest-unity-only
description: "Hard rule - only the newest Unity 7 editor installed on the PC; never install, test or mention older versions (6000.x, a12)"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 6290b447-5746-48e4-9bce-bb7f2ae2e362
  modified: 2026-11-06T10:50:15.019Z
---

Use only the newest Unity 7 editor installed on this PC (7000.0.0a16 on 2026-11-06). Never test, install, download or plan for older versions: no 6000.x, no 7000.0.0a12, no "compat" checks. Stated by the owner as a hard rule after repeated AI backsliding.

**Why:** the owner only ships for the latest editor; AIs kept copying old checklist items (issue #10 "Unity 6000.x", package floor 6000.0) and spending time on them.

**How to apply:** put this rule in every agent brief; drop 6000.x / a12 items from issues, docs and scripts instead of verifying them; when a script defaults to an old version, change the default to "newest installed". Related: [[feedback-orchestrate-push-main]].
