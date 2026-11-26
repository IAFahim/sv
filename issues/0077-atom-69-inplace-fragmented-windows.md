# Issue 0077 — Atom 69 (roadmap 3.4): fragmented windows walk rows in place; contiguity scanned once per chunk visit

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) phase 3, atom 3.4
(S3, S7, §9). Labels: `ai`, `area:core`, `kind:feat`. Contract: none.

## Changes

- **The remap scratch is deleted** (S7, §9): `Population.scratch`
  (capacity × 4 B — 4 MiB at FF-1M) and `ChunkSeam.Pull`/`Push` die. The
  fragmented walk reads and writes `rows[i]` **in place**, touching
  `slots[i].Slot` only for the clamp's bounds — one pass instead of
  pull + walk + push, and no capacity-sized buffer. The math per row is
  unchanged (`clamp.Apply(SaturatingAdd(before, delta), slot)`), so
  results are bit-identical.
- **Contiguity is scanned once per chunk visit** (S3): the cursor's
  `Current` runs `TrySlotRange` once and carries `{start, count}` inside
  the `ChunkWindow`; `IsContiguous`, `Apply` and `ApplyEnabled` consume
  the cached classification instead of each re-scanning the chunk's
  `SvSlot` span (a host doing probe + apply + enabled apply scanned it
  three times per frame).
- **Why not cached at reconcile:** Frent 8.0.0-beta exposes no
  world-level structural version, and a single destroy is enough to
  spoof every O(1) fingerprint — `[5,6,7,8]` minus the slot-6 entity
  swap-removes to `[5,8,7]`, whose (first, last, length) looks
  contiguous while the middle is not. Without an invalidation signal a
  reconcile-keyed cache is unsound; once per chunk visit is the sound
  floor. (3.3's any-offset windows retire the 64-slot padding at the
  same seam — bus-gated.)

## Tests

- Existing `PopulationTests` fragmented suites (values + `applied` +
  saturation) carry the in-place walk unmodified.
- `FragmentedChunk_PullPushFallback_MatchesOracle` rewritten onto the
  window driver (`Population.Chunks` + `Apply`) — it was the only
  consumer of the deleted `Pull`/`Push`.
- New: one window classified fragmented serves `IsContiguous` +
  `Apply` consistently (single classification, counters agree);
  fragmented `ApplyEnabled` rows outside the word window keep their
  value and report `applied == 0`.

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **300/300 passed** (298 + 2 new,
  1 rewritten); 3× repeat stable.
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 300/300.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27.
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok
  (ff1m p50 2,753 µs, board 150 MiB, alloc 0 B; no bench scenario drives
  `Population`, so no bench delta is claimed).
- S7 receipt: the capacity × 4 B remap scratch is gone by deletion —
  −4 MiB at a 1M-slot population, and the fragmented path is one pass
  over the rows instead of pull + walk + push (results bit-identical;
  the existing fragmented oracle suites passed unmodified).
- S3 receipt: `IsContiguous` + `Apply` + `ApplyEnabled` on one window
  share the cursor's single `TrySlotRange` scan — a probe + apply +
  enabled-apply host scanned the chunk's `SvSlot` span three times per
  frame before, once now (counter-consistency test pins it).
- FMA grep clean; no production comments.

## Provenance

`main@6d77315` + this atom.

Bus note: `/home/i/GitHub/agent-bus` does not exist on this host
(`/home/os`), so the pre/post-atom bus checks could not run; recorded
here per AGENTS.md discipline. Atoms 3.2/3.3/3.5/3.6 stay bus-first
(clause 7/5/17/13(b) wording) for the same reason.
