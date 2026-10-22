# Issue 0078 — Atom 70 (roadmap 4.1): `ClampShift` — the intrinsic-update monoid as a value type

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) phase 4, atom 4.1
(§4.1, law L6). Labels: `ai`, `area:core`, `kind:feat`. Contract: none —
pure addition; the drift verbs that consume it are 4.2 (bus: clauses 7,
17).

## Changes

- New `readonly struct ClampShift { long Shift; int Floor; int Ceiling }`
  (src/Sv/ClampShift.cs) implementing the composition of intrinsic
  updates `x ↦ clamp(x + s, f, c)` (§4.1, on top of atom 50's
  saturate-then-clamp totality):
  - `Identity`, `Step(delta, floor, ceiling)` (throws on crossed bounds),
    `Apply(int)`, `Then(ClampShift)`, `Repeat(long)` with the closed-form
    n-th power and settle detection, and the canonical form (bounds
    tightened to the image of `int`; constant maps carry `Shift == 0`)
    so equal structs ⇔ equal functions.
  - Private constructor — every instance enters through `Step` or
    composition, so `Floor <= Ceiling` holds everywhere.
- Nothing consumes it yet — this atom lands the algebra and its laws;
  drift storage (4.2) and rebase (4.3) follow behind the bus.

## Tests (law L6)

- Identity: `Identity.Apply(x) == x` on the lattice; left/right
  neutrality of `Then` on canonical fields.
- Associativity: exhaustive triples over a lattice of ~40 rules
  (deltas ±, floors/ceilings incl. crossed-rejects) —
  `(a.Then(b)).Then(c)` and `a.Then(b.Then(c))` field-equal.
- `Repeat(n) == Thenⁿ` for n ∈ {0, 1, 2, 3, 7, 100}.
- `Repeat(n).Apply(x)` equals n eager `Apply` steps, exhaustive rules ×
  x lattice {MinValue, −1, 0, 1, MaxValue} × n, plus random rules.
- Settling: rising rules settle at the ceiling, falling at the floor.

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **314/314 passed** (300 + 14
  new ClampShift law facts); 3× repeat stable, law suite 165 ms.
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 314/314.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27.
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok
  (ff1m p50 2,789 µs, board 150 MiB, alloc 0 B; nothing consumes
  `ClampShift` yet, so no bench delta is claimed).
- L6 receipts: associativity field-equal over ~343k lattice triples;
  `Repeat(n) == Thenⁿ` for n ∈ {0,1,2,3,7,100}; `Repeat(n).Apply(x)`
  eager-equal over the full rule lattice × {MinValue, −1, 0, 1, MaxValue,
  ±40} × n ∈ {0..29} plus extreme-shift rules; 20k random composition
  chains match sequential application; settling pinned at both bounds.
- Implementation note: the roadmap sketch's image endpoints
  (`int.MaxValue + shift`) were transcribed as `long.MaxValue + shift`
  on the first pass — every `Step` collapsed to a constant map and the
  law suite caught it immediately (the receipts discipline working).
  The final form computes the endpoints with compare-then-add guards,
  so it is total for every long shift (no add can overflow).
- FMA grep clean; no production comments.

## Provenance

`main@1240653` + this atom.

Bus note: `/home/i/GitHub/agent-bus` does not exist on this host
(`/home/os`), so the pre/post-atom bus checks could not run; recorded
here per AGENTS.md discipline.
