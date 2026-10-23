# Issue 0096 — Atom 88 (roadmap 4.1, completes issue 0078): `ClampShift.FirstTickAtLeast` — thresholds are scheduled, not scanned

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) §4.1, the one piece issue
0078 did not land ("Thresholds are scheduled, not scanned"). Labels: `ai`,
`area:core`, `kind:feat`. Contract: none — pure addition to the 0078 type;
the drift verbs that consume it stay behind the bus (clauses 7, 17).

## Changes

- **`ClampShift.FirstTickAtLeast(rising, value, threshold, horizon)`**
  (`src/Sv/ClampShift.cs`): the earliest n in [1, horizon] with
  `rising.Repeat(n).Apply(value) >= threshold`, or horizon + 1 when no tick in
  range reaches it — a bisection over the closed-form n-th power, O(log
  horizon) evaluations. This is the bridge from the monoid to the timing wheel
  (§4.6): "first tick mana is full" or "the tick the poison kills" becomes one
  wheel entry instead of a per-frame scan.
- One deliberate deviation from the roadmap snippet, in house style: the
  bisection's predicate is monotone in n only for maps that rise or stay
  constant, so the method throws `ArgumentException` on `Shift < 0` (the
  roadmap names the parameter `rising` and relies on the caller; `Step`
  validates its precondition the same way). Constant maps (`Shift == 0`) are
  valid: the predicate is constant, and the bisection returns 1 or horizon+1
  correctly.
- Nothing consumes it yet — same landing discipline as 0078: the algebra and
  its laws first, drift storage (4.2) and rebase (4.3) follow behind the bus.

## Tests (total 388)

- `FirstTickAtLeast_MatchesBruteForce_Random` — 20,000 trials: rising rules
  over a delta/floor/ceiling lattice, values straddling the bounds, thresholds
  inside and outside the image, horizons 1–200; the eager walk finds the same
  first hit, including the unreachable → horizon+1 case.
- `FirstTickAtLeast_IsExactAroundTheHit` — for every hit n, `Repeat(n).Apply(v)
  >= threshold`, and `Repeat(n−1).Apply(v) < threshold` whenever the start
  value was below the threshold (n = 1 with v already above is minimal by
  definition — the segment convention starts at tick 1).
- `FirstTickAtLeast_UnreachableAndExactEdges` — threshold above the ceiling →
  horizon+1; value at ceiling, threshold at ceiling → 1; the 4-tick climb
  0→3→6→9→10; the same climb from 1 → 3; constant map reaching and never
  reaching.
- `FirstTickAtLeast_RejectsNonRisingMaps` — `Shift < 0` throws.

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **388/388 passed** (384 + 4 new);
  2× repeat stable.
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 388/388,
  0 warnings.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27.
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok
  (ff1m 2,932 µs, wheel-expiry 94.4 µs Expire / 286.6 µs tick at 262,496
  pending, renumber1m 5.0 ms + 9.0 µs — no movement; nothing calls the new
  method yet).
- FMA grep clean; no production comments (XML docs only); `contract/` and
  `StrictFloatOracle.cs` untouched; digest pins unchanged.

## Provenance

`main@5c110b0` + this atom. Bus checked before and after.
