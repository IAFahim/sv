# Remaining work after K1, S1, S2 (for the owner and smaller agents)

Order and owner of each item. Everything below is already designed in plan.md and docs/V3-HANDOFF.md.

1. K1 (running, Opus): fast Decide kernel. Result in docs/BENCH-REPORT.md style numbers.
2. S1 (BRIEF-small-1-seal.md): Seal fast path. Closes issue 0362.
3. S2 (BRIEF-small-2-stat-layers.md): stat layers and derived rows. Closes LEGO missing brick 4 and issue 0210 item 3.
4. Cleanup (docs and small code, a Haiku-sized agent): remove redundant bricks only after the owner agrees (list in docs/LEGO-REPORT.md "Redundant bricks"); issue 0365 (raw arena counts two intervals per layer: changes HardCap verdicts, so only with a test that pins the new verdict); issue 0363 (input log memory with non-repeating records: cap per frame).
5. Integration into the skill repo: update unity-cli-skill field notes with what v3 taught (layers, test order, bench numbers, agent process) and AGENTS.md of sv with its layer table (rule 19).
6. M8 Unity adapter v4, M9 tl glue and NorthStar3, M10 web designer/simulator, M11 deletions of the legacy code: not started; each is a milestone of its own in plan section 14, to be briefed one by one after items 1 to 5 and the owner's confirmation of the readings below.
7. Push: v3/int has never been pushed. Only the owner decides when, and to which branch name.

Readings the owner still has to confirm (all are in contract text and issues): per-Hit counts offered hits; card modifiers once per held card (per count is optional); DoT ticks carry no source; assign values per hit; roll draws once per record; spend and absorb share one budget; edge rules cannot read cells; rewind does not undo spawns of the rewound epoch; rewind carry in the host versus journalled derived records; float literals only for ExprType.Float.

Rules for every agent: BRIEF3.md and BRIEF4.md; never edit an existing test body; never move a pinned digest; verify with the five suites and the bench digests before reporting; do not push.
