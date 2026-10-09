# Issue 0042 — Atom 37: derived stats (`SchemaBuilder.Derived`)

Filed as GitHub issue #42 (pending — see receipts). Labels: `ai`, `area:core`,
`kind:feat`, `contract:proposed`. Contract: bus 0036 amendment 6 (clause 3, additive)
— vendored contract stays v1.0.3 until ack.

## Problem

"attack = 1 + 2 x strength, then attack's own buffs" is the most common stat shape in
games; hosts had to re-implement it outside sv's canonical fold, determinism and
dedupe.

## Shape

- `SchemaBuilder.Derived(name, base, params (string Source, float Coefficient)[])`;
  sources must already be declared (acyclic by construction), at least one,
  coefficients finite. `StatDef.Sources` / `StatSource(StatId, Coefficient)` expose it.
- Each source contributes one synthetic `Added` entry, `coefficient * sourceValue`
  (one IEEE multiply), with ids `int.MinValue + k` so canonical order places them
  before every real modifier, in declaration order (clause-3 extension).
- The synthetic entries are part of the set's canonical content: set values stay
  immutable and deduped, so atom 23's join-only broadcast still holds.
- `Recalculate` drains the queue as it grows; a pair whose set changes re-queues the
  slot's dependents (CSR dependent table, native). New sets fold at resolve (value
  known immediately for dependents); broadcast stays in the dirty pass. A dependent
  processed before its source in the same pass is re-queued when the source settles.
- Non-derived schemas: identical behavior and bits (north-star migration digests,
  tape tests, cross-host digest unchanged).

## Receipts

- `DerivedStatTests`: three-level chain (str -> atk -> crit, crit also reads str)
  exact values through add/remove; schema validation (forward/self/missing source,
  no sources, NaN coefficient); independent StrictFloat oracle over 2 seeds x 120
  frames x 70 ops (acquire/release/add with sources/RemoveSource) with renumbers —
  every stat of every live slot bit-identical.
- Bench: stat-recalc 22 ns, stat-churn 17 ns, frame-run ~700 us (unchanged).
- Tests default: 173 + 21 + 6 passed, 0 failed. SV_CHECKED: 175 + 21 + 6 passed.
- no-FMA grep clean.
