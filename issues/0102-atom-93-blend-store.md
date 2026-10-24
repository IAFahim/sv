# Issue 0102 — Atom 93 (roadmap 6.3): blend-store masked words — one vector masked path for every ISA, the atom-54 guard-page contract kept

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) 6.3, owner decision 0047.
Labels: `ai`, `area:core`, `kind:feat`. Implemented ahead of the v1.2 fold per
owner instruction (bus 0053; 0051 precedent).

## Changes

- **The masked-word kernels are `Load` + `ConditionalSelect` + `Store` blends**
  (`MaskedWord256`: 8 groups of 8 lanes, `MaskedWord128`: 16 groups of 4 —
  per-group lane mask from the word's bits, computed as
  `~Equals(Create(lanes) & laneBits, Zero)`); the group is loaded full-width,
  the add+clamp result is blended against `before`, and the store is
  full-width. `applied` gets `SaturatingSubtract(after, before) & mask` —
  disabled lanes report 0 inside blended groups.
- **The AVX2-only `ApplyMasked`/`LoadMasked256` surface is deleted** (IBound +
  7 bound impls, IDelta + 2 delta impls). One masked path serves every ISA:
  x86 with AVX2 dispatches `Masked256`, **everything with 128-bit vectors
  dispatches the new `Masked128`** — arm64 (S8) and no-AVX2 x86, which
  previously fell to the per-bit scalar walker, now run vectorized. Dense
  words (`ulong.MaxValue`) still take the dense width dispatch.
- **`validBits` threads through the whole masked surface** — full-width
  touches are only legal inside the caller's valid rows, so blend groups run
  while `group*width + width <= validBits` and the boundary group runs scalar
  per bit (set bits compute+store, all bits report their applied value). This
  is what keeps the pinned atom-54 fuzz contract — *a masked word never
  touches a column past its end* (the old AVX2 masked loads/stores suppressed
  faults on masked-off lanes; a plain load does not) — under the blend.
  - `Simd.EnabledWord` takes `int validBits`; Span/`Column` call sites derive
    it from the actual lengths (`clamp(length − offset, 0, 64)`, per-row paths
    take `min(value, delta)` — a delta column may legally end at the reach).
    Raw-pointer paths stay length-blind: multiword raw callers are
    word-complete by the S6 padding contract (Frent), so they pass 64.
  - **The raw single-word surface gains `int validBits = 64`** (Board +
    Kernel overloads, optional, source-compatible): a flush partial word now
    says so (`Guard.EnabledTail` rejects set bits past it — always-on, like
    the other range guards). The 3-int-against-`PROT_NONE` guard-page test
    drives exactly this.
- **Fixed, pre-existing**: `SvUnityFacadeTests.BindUnbind` expected the dead
  LIFO free stack (rebind → slot 39); since c1d080c (roadmap 2.6) the bitmap
  allocator is lowest-free-first, so the rebind is slot 1. The facade suite
  had fallen out of the local battery — it is in every battery from here.

## Tests (total 408)

- `EnabledWord_ShortColumnMatchesScalar` (new): a 13-row column — blend group
  0 + scalar boundary group + zero words + all-set — × 3 clamp kinds ×
  per-row delta, values and all 64 applied rows bit-equal to the scalar
  walker.
- The exhaustive `EnabledWord_MatchesScalar_AllClampKinds` fuzz (all clamp
  kinds × 18 words × offsets × uniform/per-row × applied on/off) and the
  `PROT_NONE` guard-page test now run against the blend path.
- Sv.Unity.Tests 6/6 (stale expectation fixed), NorthStar 27/27, checked
  build green.

## Receipts

Measured on the bus host (decision 0048): i9-14900K, 24C/32T, .NET 10 Release.

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors; SV_CHECKED build
  + full retest green.
- `dotnet test` — **408/408** Sv.Tests (407 + 1 new), 6/6 Sv.Unity.Tests,
  27/27 NorthStar.
- Same-box before/after `enabled-runwalk` (100k rows): MaskLoad/MaskStore
  41.8 µs / 2391 M rows/s → blend-store 42.7 µs / 2339 M rows/s (median of 3)
  — **−2.2% on AVX2**, recorded honestly: that is the price of one path for
  every ISA; the roadmap's ≥3 G rows/s goal was not met by *either* path on
  this box (both ~2.3–2.4 G). The arm64/no-AVX2 win is structural (scalar →
  `Masked128`); this host has no arm64 or AVX-512 to measure it, and those
  dispatch arms are hardware-gated so the local battery exercises `Masked256`
  only — the no-avx2/arm CI matrix is their coverage.
- FMA grep clean; no production comments (XML docs on the new public
  parameter only); `contract/` and `StrictFloatOracle.cs` untouched.

## CI incident (owner action needed)

GitHub Actions has been dead since the 2026-10-24 ~03:09 UTC pushes: every
determinism/bench job fails to start — *"recent account payments have failed
or your spending limit needs to be increased"* (billing, not code). Atoms
90–93 are green locally per this receipt; the README bench table stays at its
last CI refresh (2026-10-22, EPYC CI runner) with the provenance note already
in place above it.

## Provenance

`main@9941db9` + this atom. Bus checked before and after (inbox empty).
