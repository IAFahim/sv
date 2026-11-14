# 0160: lanes: table stride, journal order, duplicated types (v3 A1 findings)

Status: closed (2026-11-14, decided by the lead dev under the owner's standing delegation)

Found while writing `src/Sv.Core` (branch v3/a1-lanes). Built with `dotnet build src/Sv.Core/Sv.Core.csproj -c Release`, net10.0 and netstandard2.1, 0 warnings.

1. **Table stride is not the pack's lane count.** Plan 3.2 says a chunk is its lane references plus one live word; 9.1 says live words and the
   Dying set rewind as lanes. Implemented as two meta lanes per chunk after the pack lanes (live, dying, template; death epoch per row).
   The table stride is `WorldView.LanesPerChunk = pack lanes + 2`. Generated code must index through `WorldView`, never through the pack's
   `Layout.LanesPerChunk` (13.1), which is the pack lane count only.
2. **Journal order.** A table index may be journaled more than once in an epoch (retarget to a template lane, then a write). Rewind walks
   each epoch backwards, so entries must reach the journal in the order they happened. Worker writers must be absorbed
   (`World.Absorb(ref writer)`) after each phase and before any main-thread `Acquire`, `Release`, `Write` or `LoadChunk`.
3. **JournalEntry size.** 13.3 prints `long TableIndex + uint Previous` (16 B); 9.1 counts 8 B. Implemented as two `uint` (8 B); a table
   index fits 32 bits (65,536 chunks x at most 8,064 lanes).
4. **Rewind(e) meaning.** Implemented as "the state at the start of epoch e" (journals newest..e undone, frame = begin of e). 9.1's
   "back to e+1" reads as the same point with another numbering.
5. **Duplicated public types.** `Bits`, `CellDigest`, `Memory` (public) and `XxHash64` are linked into Sv.Core from src/Sv, so they exist
   in both assemblies. An assembly that references both Sv and Sv.Core (old-vs-new tests, Sv.Bench) gets CS0433. Fix at integration
   (extern alias, or move them out of Sv) or at M11. A2 linking the same files into the same csproj gives a duplicate Compile item.
6. **BoundOf twice.** `Clamp.cs` depends on `Board`, so it cannot be linked. `LaneMath.BoundOf` restates the law of `Clamp.BoundOf` (NaN 0,
   saturate, truncate); a test must pin them equal.
7. **Slot 2^22 - 1 is the "no source" key value** (4.1), so it is never handed out (`Slot.Reserved`).

## Decision (2026-11-14)

Readings confirmed as implemented (stride through `WorldView.LanesPerChunk`, journal order with `World.Absorb`, 8-byte journal entries, Rewind = start of epoch e, slot 2^22-1 reserved). Item 5: a project references either `com.sv.core` (legacy) or `com.sv.engine` (v3), never both from one assembly (docs/PRODUCTION-CHECKLIST.md). Item 6: accepted; both are the same law.
