# Issue 0048 — Atom 41: byte-count allocations — remove two-argument Alloc misuse (review atom 2)

Filed as GitHub issue #28 (numbering offset — see issue #24 provenance note). Labels:
`ai`, `area:core`, `kind:feat`. Source: design review 2026-10-20
([issues/0044](0044-2026-10-20-design-review.md), GitHub #24) §10 atom 2 / receipts —
verified against source before fixing.

## Problem

`NativeMemory.Alloc(elementCount, elementSize)` **multiplies** its arguments; it is not
an alignment parameter. 25 call sites passed `(byteCount, sizeof(T))` (and three passed
`(bytes, 16)`), so every such block was `sizeof(T)`× (up to 16×) larger than intended
until a `Realloc` shrank it. Memory-safe but wrong: `Board.MemoryBytes` undercounts
real usage, and the pattern is a porting trap — `UnsafeUtility.Malloc(size, align,
allocator)` in Unity *does* take an alignment, so the intent would be silently right
there and silently wrong here.

## Shape

- All two-argument `Alloc`/`AllocZeroed` calls reduced to the byte-count overload:
  `Board.cs` (freeSlots, live), `Board.Stats.cs` (outputColumn, svColumns, `Alloc<T>`,
  mirror create ×2), `SetTable.cs` (setMembers, `Alloc<T>`/`AllocZeroed<T>`),
  `EventStreams.cs` (arenas, capacities, offsets), `InputState.cs` (axes, buttons ×4,
  ring, ring ids), `Compose.cs` (Applied, playback body), `Columns.cs` (Permute temp),
  `Population.cs` (scratch, permutation), `Tape.cs` (body).
- `EventStreams` locks keep `AlignedAlloc(…, 64)` — the one place alignment is real
  (lock/offset pairs share a cache line).
- No API, math, or state-format change; block sizes only shrink toward the intended
  byte counts malloc already suitably aligns.

## Receipts

- `AllocationTests.NativeMemoryAlloc_CallsUseByteCountOverloads`: source-scan test —
  fails on any reintroduced two-argument `Alloc`/`AllocZeroed` (sizeof or literal-16
  second argument), with file:line output.
- Suite: build green; 211 tests pass (23 NorthStar + 6 Unity + 182 Sv.Tests) and
  green again under `-p:DefineConstants=SV_CHECKED` (184). `NoAllocationTests` and
  `StateRollbackTests` unchanged and passing — allocation sizing is outside the state
  format.
