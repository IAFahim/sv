# Issue 0121 - V9: events derived from edge bits, per-channel cap, Dense record

Status: closed 2026-11-12. Verified by an independent fresh clone of proto2/int at 4fcd3e3 (logs verify2-out). Remaining items live in 0149, 0150, 0151.

Agent D, branch `proto/d-frame`. Plan SV-Q7 / step V9, clause 17 of contract v2.1.0. Labels: `ai`, `area:core`.

## Change: `src/Sv/EventStreams.Derive.cs` (new, portable, C# 9, no `delegate*`)

- `EventDerive(channel, capacity = DefaultCapacity)`: build-time capacity, native scratch allocated once.
  `Begin()`, `AddCount(n)` (Interlocked, so per-partition fold counts sum commutatively), `Edges`,
  static `CountEdges` over words or units.
- `Derive(streams, channel, dense, tick, edgeWords, stateWords, words, ref maker)` (.NET form: words are
  already in slot order, no sort) and `Derive(..., ReadOnlySpan<EdgeUnit>, ref maker)` (chunked form:
  `EdgeUnit {int* Slots; ulong Edges; ulong State}` in any order; gather, stable 4-pass LSD radix on
  slot with uniform-digit skip, skipped when already ascending). Value passed to the struct maker =
  the new state bit (1 entered, 0 left).
- `count <= Capacity`: every event, ascending slot, appended in 64-event batches. `count > Capacity`:
  exactly one `DenseEvent {Tick, Count, Channel}` on the dense channel and no per-row events; edge
  words are never cleared by the derive (they stay readable until the next fold: the mask-form handler
  contract).

## Kill rule TRIPPED -> default capacity 16,384

derive bench at cap 65,536, 3 processes (AMD Ryzen 5 8500G, .NET 10.0.401 Release, 1 thread, 2^20 slots,
48-row units with a random slot map, bench.lock held, median of 40): sort (shuffled units minus
slot-ordered units) **0.540 / 0.499 / 0.682 ms** -> over 0.5 ms in 2 of 3. `DefaultCapacity` = 16,384.

| edges | words ms | units ordered ms | units shuffled ms | sort ~ms | branch |
|---:|---:|---:|---:|---:|---|
| 0 | 0.011-0.019 | 0.041-0.055 | 0.042-0.055 | 0.000 | events |
| 1,000 | 0.039-0.058 | 0.051-0.063 | 0.061-0.076 | 0.010-0.014 | events |
| 16,384 (cap) | 0.278-0.309 | 0.182-0.236 | 0.260-0.347 | **0.079-0.111** | events |
| 65,536 (old cap) | 0.559-0.705 | 0.414-0.482 | 0.913-1.164 | 0.50-0.68 | events |
| 65,536 at cap 16,384 | 0.008 | 0.033 | 0.032 | 0 | dense |
| 1,048,576 | 0.008-0.012 | 0.032-0.050 | 0.034-0.050 | 0 | dense |

0 B managed over the timed reps in every row.

## Tests (new): `tests/Sv.Tests/EventDeriveTests.cs` (13, also in the portable leg)

0 / 1 / 1,000 / 65,536 / 65,537 edges at capacity 65,536 and 16,384 / 16,385 at the default: every event
sorted by slot with the right state bit, or exactly one Dense record with channel, tick and count;
death wave 1M edges -> dense, 0 B over 37 warm ticks, edge bits untouched; below-cap warm derive
(both forms) 0 B; chunk units in 12 random orders with counts summed by `Parallel.For` = the word
stream byte for byte (1,000 and 16,384 edges); chunk units above cap -> one Dense; guards.
