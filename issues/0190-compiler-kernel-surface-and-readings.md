# 0190 rule compiler: kernel call surface against issue 0200, and readings taken

Status: open (1-3 need reconciling with C2's `IPackKernels`; 4-7 are readings the emitter follows)

Found while writing `src/Sv.Rules/Passes` and `src/Sv.Rules/Emit` (branch v3/c1-compiler).

1. **Decide needs per-record scratch and the slot capacity.** Gate, Absorb and Remove keep one outcome per record (taken per
   hit, hits left, absorbed amount, survives); a hot target has no bound, so it cannot live in the fixed per-chunk scratch.
   Generated: `Decide(world, chunk, int capacity, Effect* effects, int count, int* scratch, byte* records, ref EffectWriter next)`;
   `records` holds `count * Kernels.RecordBytes` (28 B, 8-byte aligned). `capacity` is the world's entity capacity, the
   interpreter's `IsSlot`: `WorldView` does not carry it, and a cross-row read or emit must not index past the chunk table.
2. **EdgeEmits** is `EdgeEmits(world, chunk, int capacity, ulong* edges, int* scratch, ref EffectWriter next)`: an edge
   condition may read channel totals (scratch) and the owner's row (capacity). It sends only; it clears nothing.
3. **Host fold mask.** `Decide` returns the rows with a non-zero channel total (as 0200 asks, the interpreter's `HadTotals`).
   An absorb spends its budget through the delta even when every total is zero, so the host must fold
   `NonZero(delta)` without `& touched`. Intrinsics bounded by a stat but never written by a flow get a delta lane too
   (all zero) so `FloorStat`/`CeilingStat` refold them.
   Matched as asked: `ScratchLanes` (delta lanes, then channel totals as `long`), `DeltaLanes`, `EventCount`,
   `LanesWrittenPerChunk`, `DeltaTarget`, `FloorStat`, `CeilingStat` (stat lane numbers), `Bounds`, `EventId`, `Forwards`,
   `Forward`, `Events(world, ref writer, chunk, recheck, scratch, edges)` (no sends), `Layout.PackHash/CompiledAgainst`,
   `PackEmitter.Emit(PackPlan, string)` returning `List<KeyValuePair<string,string>>` (throws on a pack error).
4. **Template numbers.** `Forwards(template)` and `Forward` take the world's template index, read as the pack's template
   order (C2 adds templates in that order). `Forward` returns `EffectKey.NoSource` for an owner below zero, so Route
   drops the record as the interpreter does; `-1` would keep it on the forwarding entity.
5. **Stat reads in rules** are whole designer units, `StatLaw.Rescale(v, scale, 1)`, as issue 0210 item 2 reads it.
   `ExprType.Float` remains for float literals only and is emitted as single IEEE operations.
6. **Gate shapes** are collected per record and folded with `StatLaw.Canonicalize` + `StatLaw.Fold`, which equals
   `ReferenceLaw.StatFold` (sorted terms). More than 64 exclusive shape groups is a compile error (one `ulong`).
7. **Not emitted:** TlGlue and Jobs (out of scope), the Layer IR as a data file (the interpreter runs `PackPlan`), kind
   dispatch after channel (only channel dispatch). Edge emits carry the pack's origin; the reader defaults it to
   Triggered (0200 item 6 holds unless an author writes `"origin": "Direct"`).

Receipts: `dotnet build src/Sv.Rules/Sv.Rules.csproj -c Release`, net10.0 and netstandard2.1, 0 warnings. The reference
pack's generated `Values.g.cs` and `Kernels.g.cs` were compiled in a scratch project against Sv.Core (net10.0 and
netstandard2.1, C# 9, warnings as errors): 0 warnings, 0 errors. Nothing run (no tests this wave by owner order).
