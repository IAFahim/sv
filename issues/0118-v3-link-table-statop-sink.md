# Issue 0118 - V3: link table, 24-byte StatOp batch, chunked-host sink

Status: closed 2026-11-12. Verified by an independent fresh clone of proto2/int at 4fcd3e3 (logs verify2-out). Remaining items live in 0149, 0150, 0151.

Plan `_work/plan-sv.md` SV-Q6, step V3 (clauses 4, 20 of `contract/CONTRACT-v2.1.0.md`).
Labels: `ai`, `area:core`, `kind:feat`.

## Changes

- **`src/Sv/Link.cs`** - `LinkMode`, `LinkScope`, `ColumnRole`, `LinkFault`, `ColumnDef`,
  `Link`, `StatWrite` as the plan (readonly structs gained constructors). `Links.Validate`
  is total and returns the first fault: duplicate accumulate column name or two inboxes on
  one consumer -> `SecondConsumer`; column index / mode / scope out of range ->
  `UnknownColumn`; Read/Check of Delta/Inbox/Effect/Signal -> `ReadOfAccumulate`;
  Accumulate/Delta on a store role, Modifier on a non-StatOutput, Event on anything but
  Signal/Condition -> `WriteOfStore`; remote write to a non-integer target (Effect, remote
  Event) -> `RemoteFloat`; remote Delta without an Inbox of the same consumer ->
  `RemoteWithoutInbox`. `Links.Hash` is a murmur-finalizer chain over every field, in
  declaration order (column order defines column ids).
- **`src/Sv/StatOp.cs`** - `StatOp` (24 B, sequential), `StatOpKind`, `StatOp.Add/RemoveSource`.
  `StatOps.Canonicalize(ops, scratch)`: atom 97's stable LSD counting radix on slot
  (4 x 8-bit passes, uniform digits skipped), then each slot run ordered by the total key
  (op remove-before-add, source, stat, kind, value bits, expires-at) - insertion sort up to
  24, heap sort above (ties are bit-identical records, so stability is not needed there).
- **`src/Sv/Board.Sink.cs`** (partial) - `BindSink`, `IsSinkBound`, `PendingBroadcast`,
  `TakeBroadcast` (ascending (slot, stat), resumable cursor, removes what it returns,
  canonical NaN bits, read from the last Recalculate's changed masks), `Apply(canonical)`
  (dead/out-of-range slot, unknown stat, bad kind, non-finite value -> skipped; returns
  ops that changed state). One edit in `Board.Stats.cs`: `ClearChangedMasks` calls
  `ResetSink()`.

## Tests (new only)

`tests/Sv.Tests/StatOpSinkTests.cs` (7) and `LinkTableTests.cs` (3): 80-frame twin boards
(bound columns vs sink + host columns, acquire/release/add/remove, 7-record drain buffer)
bit-equal every live cell; NaN sink record = 0x7FC00000; 100 shuffles -> one canonical
byte order; remove-before-add re-trigger refresh; Apply batch == the same public calls one
by one (30 frames incl. stale ops); 0 B warm for Canonicalize+Apply+Recalculate+Take;
every `LinkFault`; hash stability and field sensitivity.

## Receipts

`benchmarks/Sv.Bench/Statop10kBench.cs` (+1 line in `Program.cs`): 10,000 shuffled ops
(5k `RemoveSource` + 5k `Add`, clip source ids `0x40000000 | ...`) on 100,000 live slots,
4 stats, alternating sources so state stays steady; batch = `Canonicalize` + `Apply`,
loop = the same calls in canonical order on a twin board; Recalculate untimed; median of 40.
AMD Ryzen 5 8500G, .NET 10.0.401 Release, 1 thread, bench.lock held [measured]:

| run | batch ns/op | of which sort | apply | loop of calls |
|---|---:|---:|---:|---:|
| 1 | 34.8 | 11.8 | 23.0 | 26.1 |
| 2 | 46.8 | 15.6 | 31.2 | 35.8 |

Target <= 60 ns/op: pass. Kill rule (batch apply slower than the loop): clear
(apply 23-31 vs loop 26-36 ns). Twin boards bit-equal, 0 B over timed reps.

## Not done

- No refill on `BindSink` (host fills current rows itself, like template spawn).
- Undo-window rollback does not rewind the sink cursor.
