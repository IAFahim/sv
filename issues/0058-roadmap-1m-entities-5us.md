# Issue 0058 — Roadmap: sv at 1M entities in < 5 µs and < 200 MB (O(Δ) frame)

Status: closed (2026-11-26, obsolete: this roadmap is for the sv 2.1 engine, removed in db8dbac on 2026-11-14; GitHub #38 closed; the 1M-entity goal moved to the v4 columns engine, docs/v4-design.md)

Plan: [Roadmap.md](https://github.com/IAFahim/sv/blob/main/Roadmap.md) on `main`. This issue tracks it. Each atom gets its own issue file and GitHub issue when it starts (next free: atom 51, issue 0059).

## Goal

Owner, 2026-10-21: 1,000,000 entities with full-fat stats, intrinsics and events, in under 5,000 ns per frame and under 200 MB of RAM. The consumers are a Unity ECS (Burst) hot path and a custom .NET engine.

## Verdict

One dense pass over a 1M-row `int` column moves 8 MiB. On one core that takes about 200–800 µs, which is 40–160× the whole budget. So sv's per-frame cost has to depend on the changes (Δ), not on the entity count (N). On the cold-cache model in Roadmap.md §1, 5 µs covers about 150 changed entities per frame, or about 350 when warm.

FF-1M (defined in Roadmap.md §2.2: 1M entities, 16 stats, 4 intrinsics, 2 timed modifiers per entity, 4 event channels, an 8-frame rollback window, Δ = 128 changes per frame) needs about 0.9 GiB today. The O(Δ) design targets about 150 MiB (≈ 157 MB).

## Defects to fix first

Full list with file:line references: Roadmap.md §3. These gate everything else:

- **F1 (high, memory safety):** `Population.cs:229-238`. The Frent enabled window never checks that `offset` lies inside the chunk. Writes land before the component array.
- **F2 (high, memory safety):** `Guard.cs:80-86`. `1ul << validBits` masks the shift count, and the multi-word seams check only the last word.
- **F3 (high, memory safety):** the fast paths trust `applied` and `deltas` span lengths. A window that runs past the chunk end writes past the caller's `applied` span.
- **F4 (high, integrity):** `Population.cs:63-107`. `Reconcile` validates a count, not a bijection, and rewrites the world before the board rejects it.
- **F5 (high, integrity at scale):** `Board.Stats.cs:99,564`. Modifier ids are 32-bit. The counter wraps after 2³¹ adds.
- **F8, F9 (medium, contract):** crossed stat bounds oscillate. `applied` can report the wrong sign, because `after − before` overflows in 32 bits.
- **F16 (medium, cost):** renumbering re-resolves every live pair. About 1 s per reconcile at FF-1M (estimate).

## Plan

| Phase | Exit criterion |
|---|---|
| 0 · Measure | FF-1M bench (atom 0.1), guard-page fuzz (0.2), eager oracle frozen (0.3) |
| 1 · Safety | F1–F9 fixed; lattice oracle extended with crossed bounds |
| 2 · Memory | set refcount; no member lists or pair queue; FF-1M ≤ 160 MiB |
| 3 · O(Δ) frame | sv's own stages ≤ 5 µs at Δ = 128 |
| 4 · Drift | `ClampShift` monoid, bit-equal to the eager oracle; FF-1M busy frame ≤ 5 µs p50 on one cold core |
| 5 · Replay | Zobrist digest, undo-log rollback, incremental keyframes |
| 6 · Portable | `Sv.Core` unmanaged kernels, Burst, blend-store masks |
| 7 · Algebra | `Column<T>` / `Window` / `Delta` / `Bounds` verb; history-free fold |

Phase 0 comes first. No later number can be trusted until the FF-1M bench exists.

## Contract batches (bus first; none sent yet)

Under AGENTS.md, every contract change goes through the bus before any atom depends on it. Roadmap.md §8 lists 13 proposals.

- **v1.2 (safety):** clause 10 (crossed bounds), 18 (saturating `applied`), 3 (64-bit modifier ids), 12 (park writes while stale), 5 (windows at any offset), 5/6 (blend-store rewrites), 7 (explicit expiry sub-step).
- **v2.0 (semantics):** drift verbs (7, 17), declared drain order (7), sparse tl effects (13(b), tl owner), history-free fold (3), `Cap` op kind (3), indirect stats (4).

## Decisions needed from the owner

1. Approve sending the v1.2 batch on the bus.
2. Crossed bounds (F8): is "ceiling wins" the rule?
3. Modifier handles: widen `ModifierHandle` to 64 bits? This changes the public surface.
4. History-free fold (§4.5): it invalidates every baked digest. Accept?
5. Blend-store (§4.9): change "disabled rows are never touched" to "disabled rows are rewritten with their own value"?
6. Target hardware for the 5 µs budget: which CPU and core count? Is the budget measured on one cold core?
7. Unity: which version, and is Burst the only compiler for the hot path?

## Not doing

Roadmap.md §10: a dense 1M-row pass in 5 µs; per-tick events for drifting intrinsics; multithreading the Δ path; tapes that stay bit-identical across the eager-to-drift switch.

## Provenance

Written from a read of `src/`, the README receipts and the vendored contract at `main@0c063b1`. Nothing was built, run or benchmarked. Numbers are README receipts, byte arithmetic from the code, or labelled estimates.
