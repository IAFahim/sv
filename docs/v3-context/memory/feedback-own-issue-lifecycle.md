---
name: own-issue-lifecycle
description: "Talk with the owner first; issues come from agent findings, not from chat; Claude opens/closes/reopens them itself"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 383aeffb-e16f-4f69-b907-8b8946cb0cce
  modified: 2026-11-12T11:16:53.471Z
---

Issues come from agent work (a finding, a failed check, a design problem, a proof gap). Do not file an issue from a chat answer, from a guess, or right after the owner describes what they want. Talk first: understand the requirement, say plainly what is or is not possible, then file once it is clear. The owner does not read issues and does not need to give permission to file one; they just do not want issues instead of a conversation.

When an issue exists, I close it myself once the work is done and independently verified (commit sha plus receipts) and reopen it when something touches it again. The owner never babysits issues.

**Why:** 2026-11-12 sv phase 2: I filed 0145 to 0153 from my own guesses (including an "immunity" issue, when immunity is just a number); owner said "talk with me first... I don't care about your issues". Earlier same day: "I am not babysitting opening and closing and checking issue".

**How to apply:** rule 17 in unity-cli-skill field notes. When the owner describes a product idea, reply with my understanding in their words and ask the few questions that matter; no files, no issues. Also keep code examples out of design talk unless asked; owner said my code was "horrible" when it guessed their model. Related: [[feedback-orchestrate-push-main]], [[feedback-skill-repo-generational-knowledge]].
