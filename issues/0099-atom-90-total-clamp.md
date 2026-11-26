# Issue 0099 — Atom 90 (roadmap 1.3): total clamp — crossed bounds are ceiling-wins, `applied` saturates

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) 1.3, fixes F8 + F9.
Labels: `ai`, `area:core`, `kind:feat`. Contract: the v1.2 batch items 1–2
(decisions 0044 + 0043 batch approval; bus relay 0052, correction 0053 —
implemented ahead of the fold per owner instruction, atom-83/0051 precedent;
the drafted batch in `issues/0097` carries the wording).

## Changes

- **Crossed bounds are ceiling-wins** (F8, decision 0044): every clamp site
  tightens the floor to `min(lo, hi)` before clamping, so `[0, −5]` is the
  constant −5 instead of an oscillator and a zero-delta pass is the identity.
  - Scalar `Clamp.Apply`: the `Columns` and `StatBounds` arms now load both
    bounds (missing side = ±int.MaxValue) once, apply ceiling-wins, then clamp —
    replacing the old early-return chain that clamped to `lo` first and made
    crossed rows alternate.
  - Vector: `ClampBoth` (128 and 256) prepends `Vector256.Min(lo, hi)` — int
    `Min` is clause-2-legal; single-sided `MinOnly`/`MaxOnly` cannot cross.
    `Constant` still rejects `min > max` at construction (unchanged).
- **`applied` is `saturate(after − before)`** (F9): new
  `Simd.SaturatingSubtract` (scalar, Vector128, Vector256 — the same
  compare/select shape as the atom-50 saturating add) replaces every plain
  `after − before` at the nine reporting sites: `ChunkScalar`, the
  `Loop` 256/128/tail arms, `EnabledWordScalar`, `MaskedWord` full and partial
  lanes (`& mask` preserved — masked lanes report 0), and the two Frent
  `Population.Apply`/`ApplyEnabled` fragment paths. F9's repro now reports
  `int.MaxValue` for `before = int.MinValue, delta = 0, Constant(0, 100)`
  instead of `int.MinValue`.
- **The frozen eager oracle moved with the contract** (as its header demands):
  `EagerOracle.ClampTotal` is ceiling-wins and `FrameStep` reports the
  saturating difference — the oracle change is the owner-decided bump, recorded
  here, not a chase. `SaturatingAddTests` assertions model the saturating
  difference; the cross-host digest pin moves
  `73D6FCED95A8BA9D → 0781B61BE6F53640` (both runs still agree in-process and
  across ISA dispatch; the scenario bytes changed because the semantics did).

## Tests (total 400)

- `TotalClampTests` (5 new): crossed columns over a 21-row range (scalar tail
  covered), crossed stat bounds through `Bounds.Stats`, crossed bounds through
  a masked window (disabled rows untouched, applied 0), the F9 saturating
  pullback lattice (both saturation directions, incl. `Constant(int.MinValue,
  int.MinValue)`), and a 512-row × 3-pass random differential against a local
  eager model with extreme values and crossed rows.
- Updated: `EagerOracle` model, `SaturatingAddTests` expectations, digest pin.

## Receipts

Measured on the bus host (decision 0048): i9-14900K, 24C/32T, .NET 10 Release.

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **400/400 passed** (395 + 5 new);
  2× repeat stable.
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 400/400,
  0 warnings.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27.
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok.
  Honest cost of the saturating applied: `intrinsics-uniform` 16.5 → 33.9 µs /
  100k rows (the applied column costs ~0.17 ns/row extra — plain subtract was
  one `vpsubd`, the saturating form is ~8 vector ops), ff1m p50 2932 → 3317 µs
  (+13% composite), everything else within noise. F9 correctness is
  owner-approved; fusing the saturation into the add path is a possible later
  perf atom, recorded here.
- FMA grep clean; no production comments (XML docs only); `contract/` and
  `StrictFloatOracle.cs` untouched.

## Provenance

`main@3f04162` + this atom. Bus checked before and after (correction 0053
sent ahead of the code).
