# Issue 0110 — Atom 102 (roadmap 6.4): Unity adapter v2 — SvRange, Δ-sync views, Burst ISystem frame

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) 6.4, §5.5 and §6 line S3.
Labels: `ai`, `area:unity`, `kind:feat`. Built in worktree `agent-102`
(main@6723d60); integrated onto main@c7fa211 (atoms 99/100 landed meanwhile —
no shared files, clean pick). The roadmap's "mask funnel" half of
this atom is intentionally NOT built: atom 94 ([issues/0103](0103-atom-94-any-offset-windows.md))
already made `SvChunk.RunIntrinsics` consume Unity's v128 enabled mask directly at
any slot alignment with a bounded tail — building a second funnel would duplicate it.

## Changes

- **`SvRange` chunk component** (`packages/com.sv.unity/SvRange.cs`):
  `IComponentData { int Start, int Count }` — the contiguous flag from the roadmap
  note is implied by reconcile's contract (the chunk's SvSlot rows are its row
  prefix and map to slots [Start, Start+Count)), so the shape stays two ints.
  - `UnityPopulation.Reconcile` ensures the component via
    `EntityManager.AddChunkComponentData<SvRange>(CreateEntityQuery(typeof(SvSlot)))`,
    enforces the row-prefix contract (a bound row after an unbound row inside one
    chunk throws — unreachable in real Unity, where chunks are archetype-homogeneous),
    and writes `{Start, Count}` per chunk; zero-bound chunks get `{0, 0}`.
  - `SvChunk.RunIntrinsics` now reads the chunk's SvRange instead of scanning
    `slots[row] == startSlot + row` per visit — the chunk resolves its slot window
    in **O(1)** (three loads: missing-component check, then extent + first/last-row
    anchor validation). A chunk with no SvRange (never reconciled, or spawned since)
    throws "reconcile at the structural boundary first"; a stale range (row count
    past the chunk's, or anchor mismatch) throws the same class. Count 0 is a clean
    no-op (reconciled chunk that binds nothing). The removed scan was ≤ 128
    compares per visit — at FF-1M's 8,192 chunk visits that arithmetic is ~0.8
    ms/frame of scan work deleted from the adapter (estimate, not a measurement).
    Residual gap, recorded honestly: a same-count mid-row rewrite between reconcile
    and the visit (host misuse — double `Bind` into a reconciled chunk) is outside
    the O(1) checks; real Unity bumps the chunk's `OrderVersion` on any such move,
    and pinning `SvRange` to an OrderVersion is the v2.1 hardening if ever needed.
  - `ChunkSeam.TrySlotRange` (Frent) is untouched — this atom is the Unity side.
- **Δ-sync views** (`packages/com.sv.unity/SvChanged.cs`):
  `SvChanged.ChunkMask(board, stat, chunkStart, chunkCount) -> v128` funnels the
  board's `ChangedWords` bits for the chunk's slot range into Unity's word-pair
  shape at **any alignment** (three board words read, two funnel shifts, tail
  masked to `chunkCount`), plus `SvChanged.WordCount(chunkCount)`. A host job
  re-reads only chunks whose mask is nonzero — sv's Δ becomes Unity's Δ.
  Measured 1.4 ns per misaligned call on a 1M-row board (see receipts).
- **Burst `ISystem` v2 frame order** (`packages/com.sv.unity/SvSystem.cs`):
  `SvIntrinsicsSystem.OnUpdate` now runs the clause-7 v2 order — structural
  boundary first (`StructuralChange` flag → `UnityPopulation.Reconcile`, SvRange
  written, board renumbered), the chunk job over intrinsics with enabled masks,
  `Board.ExpireModifiers(Tick)` with due crossings collected into the
  `NativeArray<long> DueCrossings` buffer (`DueCrossingCount` of them, (tick, id)
  order), then `Board.Recalculate`. Event drain is a documented host-side seam
  (hosts drain before calling Update); `ExpiredLastFrame` reports the expiry count.
  New fields: `Tick`, `StructuralChange`, `DueCrossings`, `DueCrossingCount`,
  `ExpiredLastFrame`, and a `SvRange` handle.
- **Facade kit** (`tests/Sv.Unity.Facade/Entities.cs`, test tooling — kept to
  Unity's real API names): chunk-component storage on `FakeChunk`;
  `ArchetypeChunk.HasChunkComponent/GetChunkComponentData/SetChunkComponentData`;
  `EntityManager.AddChunkComponentData<T>(EntityQuery, T)` and
  `CreateEntityQuery(params ComponentType[])`; `SystemState.EntityManager`;
  `NativeArray<T>.AsSpan()`; `AddComponent`/`RemoveComponent` preserve chunk
  columns. One deliberate constraint relaxation, recorded honestly: the facade's
  `CreateSystem<T>`/`GetUnsafeSystemRef<T>` drop `unmanaged` → `struct` — the
  system holds sv's managed `Board`/`UnityPopulation` (the unmanaged core is
  roadmap 6.1, not landed), so real Unity's `unmanaged` constraint would reject
  the current shape too; the facade now drives it. When 6.1 lands, the facade
  should re-tighten.

## Tests (Sv.Tests 434 after atoms 99/100; Sv.Unity.Tests 7 → 13; NorthStar 27 unchanged)

New (`tests/Sv.Unity.Tests/SvUnityV2Tests.cs`):
- `SvRange_ReconcileWritesPerChunkRanges_TwoArchetypes` — 70 + 60 bound plus an
  unbound archetype chunk: reconcile writes ranges {0,70}, {70,60}, and {·,0};
  first/last row slots verified per chunk (the O(1) mapping's ground truth).
- `SvRange_StaleAfterDelete_ThrowsUntilReconciled_RunsThroughRange` — after a
  mid-chunk unbind the stale range ({0,70} against 69 rows) throws on both the
  dense and enabled paths; after reconcile, dense and enabled (v128 tail bounded
  to the 5 tail rows) run through the range and every survivor matches the
  oracle math row by row.
- `SvChanged_ChunkMask_EqualsBoardChangedBits_UnchangedChunkReadsZero` — after
  modifier edits + Recalculate on a populated 130-slot board, the chunk mask
  equals `ChangedWords` bit for bit over the chunk's rows (misaligned start 70),
  an untouched chunk reads zero, and the previously-changed chunk reads zero
  once stable.
- `SvChanged_WordCount_CoversChunkRows` — word-count edges 0/1/63/64/65/127/128.
- `V2System_FrameOrder_ReconcileIntrinsicsExpireCrossingsRecalculate` — the
  facade frame over a 96-entity world: first Update reconciles and applies
  intrinsics (+2, oracle-equal), the stat folds 15 with the modifier live; at
  Tick 5 the expiry fires (ExpiredLastFrame 1, fold back to 10); at Tick 100 the
  crossing scheduled via `ClampShift.FirstTickAtLeast` (asserted == 100 before
  scheduling) is collected in (tick, id) order with the id intact.
- `V2System_IdenticalFrameProgramTwice_IdenticalColumnBits` — the same 6-frame
  program (random values/modifiers/expiries, two same-tick crossings) from
  identical state twice: value column bits, per-frame changed-word bits and the
  crossing id sequence are identical.

Updated: the 7 existing facade tests pass the new `SvRange` handle and reconcile
before running (chunks are re-fetched after reconcile — the facade snapshots
chunk records, unlike Unity's live handles); `FragmentedChunk_Throws_UntilReconcile`
now throws on "no SvRange" pre-reconcile, same class as before. The package's
editor tests (Unity-only, not compiled by dotnet) bind + reconcile and use the
new constructor.

## Receipts

Measured on the bus host (decision 0048): i9-14900K, 24C/32T, .NET 10 Release.

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors. `dotnet test` green in
  the worktree: **420 / 13 / 27** (Sv.Tests / Sv.Unity.Tests / Sv.NorthStar), 2×
  repeat stable; `-p:SV_CHECKED=true` rebuild + full retest green, same counts.
  Integrated-main battery (i9-14900K): Sv.Tests **434 / 13 / 27**, SV_CHECKED 434,
  bench `all scenarios ok`.
- `dotnet run -c Release --project benchmarks/Sv.Bench -- all` — "all scenarios
  ok": ff1m p50 3,237 µs (Δ=128), renumber1m 5,189 µs + 9.5 µs next-frame
  Recalculate, wheel-expiry 135.9 µs Expire / 262,793 pending — within run noise
  of atoms 94–97; this atom touches no kernel or board hot path.
- Facade-kit measurements (managed, warm, honest about what they measure):
  `SvChanged.ChunkMask` **1.4 ns/call** at any alignment over a 1M-row board;
  full `UnityPopulation.Reconcile` over 1,048,576 rows / 8,192 facade chunks
  including every SvRange write: 24–62 ms — dominated by the facade's managed
  per-row `HasComponent` walk (HashSet) plus RenumberSlots (~5.2 ms per the
  bench), **not** representative of Unity's native reconcile.
- **Roadmap receipt "1M-entity scene: sv stage ≤ 5 µs on the main thread" is
  BLOCKED-ON-EDITOR**: it needs a real Unity editor scene (Entities 6.7 +
  Burst), which cannot run headless in this timebox; the facade kit carries no
  Burst compiler. Built to the facade-test level instead, per instruction. What
  the blocked measurement will exercise: reconcile + 8,192 O(1) range reads +
  intrinsics + wheel + Recalculate inside one `[BurstCompile] ISystem.OnUpdate`.
- FMA grep clean; production code carries XML docs only (no inline comments);
  `contract/`, `tests/Sv.Tests/StrictFloatOracle.cs`, `README.md` and `src/Sv`
  production code untouched (no core blockers found).

## Provenance

`main@6723d60` + this atom, worktree `agent-102`; integrated onto main@c7fa211.
Bus checked before and after (inbox empty both checks).
