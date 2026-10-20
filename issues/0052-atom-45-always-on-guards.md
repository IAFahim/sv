# Issue 0052 — Atom 45: always-on O(1) range guards + tail-bit policing on the single-word seam (review C-6)

Filed as GitHub issue #32 (numbering offset — see issue #24 provenance note). Labels:
`ai`, `area:core`, `kind:feat`. Source: design review 2026-10-20
([issues/0044](0044-2026-10-20-design-review.md), GitHub #24), finding C-6 — verified
against source before fixing.

## Problem

- `Guard.ChunkRange`, `EnabledRange` ×2, `EnabledTail` were `[Conditional("SV_CHECKED")]`,
  so release builds ran the vectorized pointer paths with no range checking at all —
  a wrong `start/count` was a silent out-of-bounds write.
- The single-word `ChunkSeam.RunEnabledChunk` never called `EnabledTail`: a full-ones
  word on a partial tail window took the 64-row fast path and wrote values past the
  chunk's rows into adjacent Frent chunk memory.

## Shape

- `ChunkRange`, `EnabledRange` ×2, `EnabledTail` are now unconditional (O(1) per call,
  not per row). `BoundColumnFit` (per-row) and `BindingFresh` stay `SV_CHECKED`-only.
- Single-word `ChunkSeam.RunEnabledChunk` polices its word with
  `Guard.EnabledTail(enabled, start + count - offsetSlot)`, matching the multi-word
  variant and `ChunkWindow.ApplyEnabled`.
- `BindingFresh` stays conditional deliberately: release-silence after
  `InvalidateBindings` is pinned by
  `BindingGenerationTests.ExternalBind_Invalidate_Recalculate_CheckedThrows_ReleaseSilent`
  — promoting it is a contract-grade decision recorded on issue #24, not taken here.
- The pointer-path `applied` sizing contract ("sized for the full window; disabled rows
  zeroed") is unchanged: it is documented API semantics (clause-5 adjacent); clipping
  stores to a partial window would change them and needs the bus.

## Receipts

- `GuardAlwaysOnTests` (5 facts, run in a release build with no `SV_CHECKED`):
  `ChunkRange`/`EnabledRange` throw on out-of-range start/count/offset; a full-ones
  word beyond valid rows throws; a zero-padded partial tail runs and zeroes `applied`
  beyond the mask; `ApplyEnabled` on a partial tail window (100-row chunk, window at
  64) stays correct on the fast path.
- The partial-tail shape is the normal post-`Reconcile` pattern
  (`PopulationTests.EnabledWindow_ParticipatesInHeal`): windows span chunk tails with
  zero padding — an earlier draft of this atom rejected partial windows outright and
  broke that pattern; the landed fix polices bits instead of window shape.
- Suite: build green; 227 tests (23 + 6 + 198), green under `SV_CHECKED` (200).
- Test-isolation note: Frent-touching test classes must join `[Collection("Frent")]`
  — an uncollected class ran `World`s in parallel with collected tests and produced
  NREs in `PopulationTests` (fixed in the same atom).
