# Issue 0066 — Atom 58 (roadmap 1.9): totality fixes — pure canonical Stat, Modifier kind validation, dead intrinsic bounds deleted, README sample + Markdown alloc scan

Roadmap [issues/0058](issues/0058-roadmap-1m-entities-5us.md) phase 1, atom 1.9 —
F20, F21, F22, F26. Labels: `ai`, `area:core`, `kind:fix`. Contract: none.

## Changes

- **F20** — `Board.Stat(slot, stat)` is pure and canonical: it gathers into a
  dedicated tooling scratch (`GatherInto` refactor — `Recalculate`'s hot scratch is
  untouched), folds, and returns `CanonicalNaN(...)` like the broadcast path does
  (atom 48 parity). Four saturating `Additive` values folding `0 × ∞ = NaN` now read
  back as `0x7FC00000` exactly like the column.
- **F21** — `new Modifier(kind, value)` rejects undefined kinds (`(ModifierKind)7`
  no longer silently hashes into a set Fold ignores); the finite-value check stays.
  Existing random tests cast `Next(3)` → valid kinds, unaffected.
- **F22** — `IntrinsicDef.Min/Max` were never read by any code path, and §4.1's
  drift rules read bounds from **stats** (clause 16), so the declared intrinsic
  bounds are dead by design: `IntrinsicDef(string Name)` and
  `SchemaBuilder.Intrinsic(name)` — the optional `min`/`max` parameters and their
  inverted-bounds validation are deleted. sv-examples uses single-argument
  `Intrinsic` throughout (verified); the seven local test call sites updated; the
  schema content hash no longer carries the dead bytes.
- **F26** — the README quick start allocated with the two-argument
  `NativeMemory.AllocZeroed(count × size, size)` (4× over-allocation, the exact
  pattern atom 41 banned) and `NativeMemory.Alloc` two-arg; both lines are now
  single-argument byte counts. The `AllocationTests` scan now covers `README.md`
  beside `src/**/*.cs`.

## Tests

- `Stat_IsCanonicalAndPure` — NaN fold canonicalized; `Stat` equals the broadcast
  column bit-for-bit before and after further mutations (F20 receipt).
- `Modifier_RejectsUndefinedKinds` — kind 7 and NaN values throw; the three real
  kinds construct.
- `InvertedBounds_RejectedAtDeclaration` removed with the feature it tested.
- The extended allocation scan is itself the F26 receipt (it failed on the README
  before the sample was fixed).

## Receipts

- `dotnet test sv.slnx -c Release` — 252/27/6 passed; `-p:SV_CHECKED=true` — 252.

## Provenance

`main@35d535f` + this atom. Phase 1's remaining atoms (1.3, 1.5, 1.8) are bus-gated
on the v1.2 batch; 1.6 (sv-local break) follows next with its bus announcement.
