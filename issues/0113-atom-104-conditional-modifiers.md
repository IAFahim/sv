# Issue 0113 — Atom 104 (roadmap 7.5, §4.11 conditionals): conditional modifiers via crossings — no per-frame predicate

## Changes

None to production code — this atom is the §4.11 **facts receipt** proving the
conditional-buff pattern end-to-end over primitives that already landed:
`ClampShift.FirstTickAtLeast` (atom 88) schedules the threshold tick, drift
segments (atoms 89–95) carry the value, `Board.ScheduleCrossing` /
`TakeDueCrossings` (atom 96) deliver it, and `AddModifier`/`RemoveModifier` +
`Recalculate` apply it. "+20 % damage below 30 % hp" costs O(Δ): zero board
work between crossings.

The one host-side discipline the facts make explicit: `FirstTickAtLeast`
requires a **rising** map (the predicate is monotone only there), so a falling
hp schedules through the traveled distance — hp falls 2/tick from 100, the
buff tick is `FirstTickAtLeast(Step(2, 0, 70), 0, 70, …)` = 35, the rising map
on the distance, not the falling map on hp.

## Tests (Sv.Tests 444 → 446; Unity 13; NorthStar 27 unchanged)

- `tests/Sv.Tests/ConditionalBuffTests.cs` (new, 2 facts):
  - `ConditionalBuff_AddRemoveRideCrossings_NoPerFramePredicate` — hp drifts
    down 2/tick from 100; the crossing scheduled at tick 35 fires exactly at
    `ExpireModifiers(35)` (0 due for ticks 1–34), the host adds
    `Multi +0.25` → damage 10 → 12.5; ticks 36–49 assert zero due and the value
    unchanged (no predicate ran); a recaptured rising rule schedules the clear
    crossing at hp ≥ 60 (tick 50, `Read == 60` verified), the removal fires, and
    damage returns to 10.
  - `ConditionalBuff_PopulationFiresInOnePass_Converges` — 200 slots, hp
    staggered 300 − slot falling 2/tick, one crossing per slot scheduled from
    `FirstTickAtLeast`; two `ExpireModifiers` passes drain all 200 in exact
    (tick, id) order (198 at ticks ≤ 134 — same-tick pairs ordered by id —
    then 2 at tick 135), 200 modifier adds, ONE `Recalculate`: every slot
    12.5f, **SetCount 1** — the whole conditional population shares one interned
    set.

## Receipts

- Local, i9-14900K: Sv.Tests **446/446**, SV_CHECKED **446**, `Sv.Unity.Tests`
  13/13, `Sv.NorthStar` exit 0, `Sv.Bench` `all scenarios ok`, FMA grep clean
  (no production change).
- CI: GitHub Actions dead since 2026-10-24 (billing; receipt 0102) — local
  receipts authoritative.

## Provenance

main@454b7cc + this atom. Bus checked before and after (inbox empty both checks).
