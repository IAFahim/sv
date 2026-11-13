---
name: feedback-no-backward-compat
description: "Owner says Tl and tl.unity need no backward compatibility at all - rename, move and delete freely, no shims"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 6290b447-5746-48e4-9bce-bb7f2ae2e362
  modified: 2026-11-07T21:25:05.192Z
---

No backward compatibility is needed for Tl, tl.unity, samples or installers (stated by the owner on 2026-11-08). Rename, move, delete and re-pin freely.

**Why:** the owner is the only user right now and wants the cleanest final API, not a pile of shims from earlier phases.

**How to apply:** do not add obsolete forwarding types, old-name aliases or compatibility fallbacks in briefs or code. What still holds: both pinned state hashes (13943614770702343869, 6607956939547305625), the five Force contract files compile unchanged (they define the coder-side API shape), and the K bench stays in band. Related: [[feedback-orchestrate-push-main]].
