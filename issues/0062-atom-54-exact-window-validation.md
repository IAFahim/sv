# Issue 0062 — Atom 54 (roadmap 1.1): exact window validation — F1, F2, F3, F23 closed

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) phase 1, atom 1.1. Labels:
`ai`, `area:core`, `area:adapter`, `kind:fix`. Contract: none (the seam contract
tightens; clause 5's shapes are unchanged — the guards reject what was never legal).

## Changes

- `src/Sv/Guard.cs` — every range guard compares in `long`: `ChunkRange`
  (`start + count`), both `EnabledRange` overloads (`offset + 64`,
  `offset + wordCount·64`), `SpanWindow` takes the window as `long` (**F23**).
  `EnabledTail` now rejects `validBits ∉ [1, 64]` before the shift — the masked
  `1ul << validBits` can no longer inspect the wrong bits (**F2**).
- `src/Sv/Board.cs` — the four `Span` twins pass `(long)offset + wordCount·64` to
  `SpanWindow`.
- `src/Sv.Frent/ChunkSeam.cs` — `RunChunk` (both arities) validates `deltas` and
  `applied` span lengths against the chunk (**F3**); the words `RunEnabledChunk`
  requires `wordCount == ⌈(start + count − offset)/64⌉` exactly (**F2** interior
  words — with the count exact, only the last word can be partial, so the existing
  tail police now covers everything) and validates the applied span against the
  window (**F3**); the single-word `RunEnabledChunk` validates the applied span
  against its 64-row window.
- `src/Sv.Frent/Population.cs` — `ChunkWindow.Apply` guards the applied span;
  `ChunkWindow.ApplyEnabled` now requires the offset 64-aligned inside the chunk and
  the exact word count **before** `EnabledTail` reads `words[wordCount − 1]` (**F1**:
  `wordCount ≤ 0` no longer reads out of bounds, and an offset outside the chunk
  throws instead of writing before the component array), plus the applied-span
  window guard.

The 64-alignment padding contract (atom 45) is preserved: a single-word window may
extend into a chunk's padding — the tail police keeps the foreign lanes disabled, so
those rows are never written; only `applied` (whose span the guard sizes) is.

## Tests

- `WindowGuardTests` (8 facts): short applied/deltas on both `RunChunk` arities,
  non-exact word counts at the seam and on a real Frent `Population`, offset outside
  the chunk / unaligned on `Population`, `int.MaxValue`-adjacent range overflow at
  the Board throwing instead of wrapping.
- Guard-page fuzz ([issues/0060](0060-atom-52-guard-page-fuzz.md)) table flipped:
  every defect case now expects a throw — F1's `population-enabled-wordcount-(zero|negative)`,
  F2's `seam-enabled-interior-past-chunk`, F3's `seam-runchunk-short-(applied|deltas)`
  and `seam-enabled-applied-window-overrun`, F23's `board-f23-(chunk|enabled)-overflow`.
  The three `rawapi-*` cases are **reclassified by design**: the raw `int*` Board API
  is length-blind per clause 5 (host storage is capacity-sized) — those guard-page
  crashes pin caller misuse forever and are documented as such in the harness.
- Found and fixed while landing: Frent 8.0-beta's static constructors mutate shared
  non-concurrent dictionaries — a test class outside the `"Frent"` xunit collection
  racing the collection corrupts them (`TypeInitializationException`). The new tests
  join the collection; all World-touching classes are now serialized.

## Receipts

- `dotnet test sv.slnx -c Release` — 240/27/6 passed, three consecutive runs green;
  `-p:SV_CHECKED=true` — 240 passed.
- Guard-page fuzz: all 22 outcomes match the flipped table (18 ok/threw, 3 pinned
  raw-pointer misuse crashes, 1 ok sanity) — the harness that failed yesterday's
  defects now proves them fixed.
- Bench (this box, `-- all --md`): every scenario at or better than the pre-atom
  numbers — `intrinsics-uniform` 16.5 µs, `enabled-runwalk` 31.4 µs, `frame-run`
  667.7 µs, `ff1m` p50 3,035.8 µs (was 3,102–3,134) — the O(1) guards cost nothing
  measurable.

## Provenance

`main@bbd3cfd` + this atom. F1/F2/F3/F23 were reproduced as crashes in issue 0060
before being fixed here; the fuzz table flip is the closure receipt.
