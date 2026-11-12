# Issue 0116 - V2: the intrinsic fold (law, kernel, conditions, cell digest, inbox)

Plan `_work/plan-sv.md` SV-Q1 / SV-Q7 / SV-Q10, step V2 plus the .NET half of V8
(clause 19 of `contract/CONTRACT-v2.1.0.md`). Labels: `ai`, `area:core`, `kind:feat`.

## Changes (new files; one keyword edit: `Kernel` became `partial`)

- **`src/Sv/Fold.cs`** - `IntrinsicLaw` (Saturate, Step, Applied), `FoldWord`, `Fold.Word`
  exactly as SV-Q1, plus `Fold.Word(in word, long* inbox, rows)` (the clause-19 reference
  with inbox). `Bcl.BoundOf` is spelled `Clamp.BoundOf` (the same clause-16 function;
  `Bcl` lives only in `Sv.Burst` today).
- **`src/Sv/Condition.cs`** - `Below`, `Next`, `Edges` as SV-Q7.
- **`src/Sv/CellDigest.cs`** - `Key(column, slot, bits)` (16-bit column id in the key, so
  equal values in two columns never cancel), `Move`, slot-keyed `Sum` over ranges and masks.
- **`src/Sv/Inbox.cs`** - `Inbox.Send/Take` as SV-Q1; `InboxBuffer` (NativeArena,
  64-byte aligned, zeroed, `Column<long>` view, Clear, Dispose).
- **`src/Sv/Kernel.Fold.cs`** - `Kernel.Fold(value, window, delta, bounds, changed = default)`
  and an overload with `Column<long> inbox`. Range and masked windows; changed bits per
  window word; the consumer clears delta and inbox in the same pass. Struct-generic bound
  strategies (constant, rows, stats, stat ceiling, any) hoist every branch.
- **`src/Sv/Simd.Fold.cs`** (`#if NET`) - 256 and 128 strategies: saturating add then
  ceiling-wins `ClampBoth` (equal to the exact long clamp), widened 64-bit inbox add,
  lane-partial groups fall to the scalar law. Non-NET builds take the scalar path.

## Tests (new only): `tests/Sv.Tests/FoldLawTests.cs` (12)

Lattice {MIN,-1,0,1,MAX}^4 incl. crossed bounds = `Kernel.Run` = `Fold.Word`; NaN/inf/
+-3e9 stat bounds; every width (scalar, 128, 256, auto) x 6 bound modes x inbox on/off x
13 window shapes (10 mask shapes, ranges with tails) = scalar reference for value, delta,
inbox and changed bits (and no write past the window's changed words); multi-row
`Fold.Word` = kernel; partition invariance (shuffled disjoint masks and ranges); 100
shuffled orders of 4,000 contributions (local wrapping adds + inbox sends) give one result;
parallel sends = sequential sum; inbox saturation and alignment; 30/40 % hysteresis edges;
digest column/slot keying and order-freedom; 0 B warm; guards.

## Receipts

See issue 0117 for `fold1m`. FMA grep clean, no production comments.
