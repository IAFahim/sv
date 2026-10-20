# Issue 0051 — Atom 44: event stream hardening — validated growth, lock release on failure (review C-10)

Filed as GitHub issue #31 (numbering offset — see issue #24 provenance note). Labels:
`ai`, `area:core`, `kind:feat`. Source: design review 2026-10-20
([issues/0044](0044-2026-10-20-design-review.md), GitHub #24), finding C-10 — verified
against source before fixing.

## Problem

- `EventStreams.Create` validated `writerCount` but not `bytesPerWriter`. With 0,
  `Round(0) = 0` sized every arena to zero bytes and the growth loop
  `capacity *= 2` never terminated — **while holding the writer spinlock**, hanging
  every other writer on that cache line.
- Arena growth (`capacity *= 2`) overflowed `int` past ~1 GiB: the negative capacity
  reached `Realloc((nuint)negative)` as a near-2^64 byte count — OOM instead of a
  deterministic failure.
- A `Realloc` failure (or the new overflow throw) inside `AppendBatch` left the writer
  lock held forever.
- `ReadState` accepted negative written/published offsets from a corrupt state.

## Shape

- `Create` rejects `bytesPerWriter < 1` (`ArgumentOutOfRangeException`); `Round` still
  lifts sub-8 sizes to 8, so growth always starts ≥ 8 and terminates.
- Growth requirement computed in `long`; requirement or restored size above
  `int.MaxValue / 2` throws a deterministic exception — `InvalidOperationException`
  on append (live path), `InvalidDataException` on load (state path).
- `AppendBatch` body wrapped in `try/finally` releasing the gate — every exit path,
  including throws from growth, releases the writer lock.
- `ReadState` rejects negative offsets with `InvalidDataException`.

## Receipts

- `EventStreamHardeningTests` (4 facts): zero/negative `bytesPerWriter` rejected;
  `bytesPerWriter: 1` rounds up and grows through 10k events without hanging; a
  crafted state with `written = int.MaxValue` fails `InvalidDataException` at the
  arena guard (not OOM); negative offsets rejected.
- Suite: build green; 222 tests pass (23 NorthStar + 6 Unity + 193 Sv.Tests).
