# sv — state variables

One noun — dense columns over entity slots. Three verbs over it: **tl** (authored
motion), **ecs-kernels** (derived math), **sv** (settings/state: stats, intrinsics,
events, input).

`sv` is the standalone state library negotiated on the agent-bus: float **stats**
(recalculated, modifier-driven), int **intrinsics** (directly written, clamped),
**events** (condition-column ingestion), and **input** (bit-captured axes, button
edges, replay ring). Entity-free at the core; ECS hosts adapt through one chunk shape.

- Contract: **The Composition Contract v1.0.3** below, vendored verbatim
  ([local copy](contract/CONTRACT-v1.0.3.md); merge point:
  `agent-bus/contract/CONTRACT.md`). Negotiated under the name "board"; shipped as
  `sv` — same library, owner's pick. Changes are bus messages first, then a version
  bump at the merge point; the full changelog lives in the vendored header.
- Verification: clause conformance is proven by the north-star fused test
  (`contract/NORTH-STAR.md`, seated as [issues/0002](issues/0002-north-star.md)) plus
  this repo's own suite; mask/run semantics by `contract/mask-fixtures.json`
  (extracted bit-exact from ecs-kernels editor-verified facts — the fleet conformance
  set). The north-star leg consumes the published tl pair only (Tl.Runtime /
  Tl.Gen.CSharp / tlb 1.3.0). Since atom 13 the harness rides `Sv.Compose`'s
  `Frame.Run`; bit-identity to the pre-migration manual loop is pinned by baked
  digest constants (`MigrationTests`), and whole-run determinism rides the tape
  (`Record`/`Playback`: per-frame digest always compared, full body byte-compared
  under `SV_CHECKED`).
- Usage: [sv-examples](https://github.com/IAFahim/sv-examples) — five runnable
  samples (core stats/intrinsics, events, input replay, the Frent adapter, and the
  frozen-frame composition with whole-run replay bit-identity), each printing a
  zero-managed-allocation receipt on its warm path. This repo also carries
  `samples/Sv.Samples` — three limit-pushing showcases (one million slots under every
  core verb; 100k entities on the composed frame with tape record → bit-identical
  playback; 100k Frent entities fragmenting and healing through `Reconcile`):
  `dotnet run -c Release --project samples/Sv.Samples`.

## Quick start

`Sv` targets `net10.0` with `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` — the data
path is unmanaged columns by design (clause 5: the host owns storage, sv owns the slot
space and the math). Define `SV_CHECKED` in debug builds to trap stale bindings and
use-after-rebind (clauses 12, 14).

All four verbs in one listing — schema, dense slots, stat modifiers, clamped
intrinsics, sparse events, bit-captured input:

```csharp
using System;
using System.Runtime.InteropServices;
using Sv;

internal static unsafe class Program
{
    public struct Hit
    {
        public int Slot;
        public int Amount;
    }

    [UnmanagedCallersOnly]
    static Hit MakeHit(int slot, int value) => new() { Slot = slot, Amount = value };

    static int Main()
    {
        // 1. Schema: float stats (modifier-driven) + int intrinsics (direct writes).
        var schema = SchemaBuilder.Create()
            .Stat("move_speed", 6f)
            .Stat("max_mana", 100f)
            .Intrinsic("mana")
            .Build();

        // 2. One board = one dense slot space; capacity rounds up to a 64 boundary.
        using var board = Board.Create(schema, capacity: 1024);
        var slot = board.Acquire();          // host maps entity -> slot; Release() at destroy

        // 3. Stats: stack modifiers, then Recalculate folds + broadcasts into columns.
        var speed = schema.Stat("move_speed");
        var haste = board.AddModifier(slot, speed, new Modifier(ModifierKind.Additive, 0.5f));
        board.Recalculate();
        Span<float> speeds = board.Output(speed);   // sv-owned column, no bind needed
        // speeds[slot] == 9  (6 base * (1 + 0.5) additive); clause-3 canonical fold
        board.RemoveModifier(slot, speed, haste);

        // 4. Intrinsics: host-owned unmanaged int columns, written through chunk ops.
        var mana = (int*)NativeMemory.AllocZeroed((nuint)board.Capacity * sizeof(int), sizeof(int));
        var applied = (int*)NativeMemory.Alloc((nuint)board.Capacity * sizeof(int), sizeof(int));
        mana[slot] = 40;

        var clamp = Clamp.StatMax(board, schema.Stat("max_mana"));  // bound = last broadcast
        board.RunIntrinsicsChunk(mana, 25, clamp, 0, board.Capacity, applied);
        // mana[slot] == 65, applied[slot] == 25

        // Masked run-walk: only set bits are touched; applied[] is 0 on skipped rows.
        board.RunEnabledChunk(mana, 7, clamp, 0b1011ul, offset: 0, applied);

        // 5. Events: sparse signals over a pooled double buffer (clause 17).
        using var streams = EventStreams.Create(writerCount: 1, bytesPerWriter: 4096);
        var hits = streams.CreateChannel<Hit>();
        hits.Write(new Hit { Slot = slot, Amount = applied[slot] });
        // ...or emit per live row off a condition column — applied != 0 -> one Hit each:
        streams.EmitFromNonZero(hits, applied, 0, board.Capacity, &MakeHit);

        streams.Swap();                          // publishes this frame's writes to readers
        while (streams.TryReadNext(out var type, out var payload, out _))
        {
            if (type == hits.Id)
            {
                var hit = *(Hit*)payload;        // drain handlers write intrinsics here
            }
        }

        // 6. Input: raw axis bits + button edges + a replay ring (clause 11).
        using var input = InputState.Create(axes: 2, buttonWords: 1, ringTicks: 128);
        var axes = stackalloc uint[2];
        var buttons = stackalloc ulong[1];
        axes[0] = BitConverter.SingleToUInt32Bits(0.5f);   // captured bit-verbatim
        buttons[0] = 1ul << 3;                           // button 3 down
        input.Capture(axes, buttons);
        // input.Pressed(3) / Down(3) / Released(3), input.AxisF(0) == 0.5f
        // input.BeginReplay(tick) + input.ReplayStep() walks the ring bit-identically

        NativeMemory.Free(mana);
        NativeMemory.Free(applied);
        return 0;
    }
}
```

### Beyond the core verbs (atoms 31–38; contract v1.1.0 proposed on bus 0036)

```csharp
var schema = SchemaBuilder.Create()
    .Stat("str", 10f)
    .Derived("atk", 1f, ("str", 2f))          // atk = 1 + 2*str, then atk's own modifiers
    .Stat("max_mana", 100f)
    .Intrinsic("mana")
    .Build();

board.AddModifier(slot, str, new Modifier(ModifierKind.Added, 5f), source: SwordItem, expiresAtTick: tick + 300);
board.RemoveSource(slot, SwordItem);         // unequip: every modifier the sword granted
board.ExpireModifiers(tick);                 // deterministic (tick, insertion id) order
board.Recalculate();
ulong* changedAtk = board.ChangedWords(atk); // rows whose atk bits changed — a run-walk mask

var clamp = Clamp.StatMirrorMax(board, maxMana);     // int mirror written at broadcast
var maker = new HitMaker();                          // struct IEventMaker<T>: inlined, no gate
streams.EmitFromNonZero(hits, applied, 0, board.Capacity, ref maker);

using var state = new SvState();             // rollback: save, simulate, load, resimulate
game.SaveState(state);                       // board + input + events + tl columns
game.LoadState(state);
```

To broadcast stats into columns the host already owns, bind once after allocation —
`board.BindOutput(stat, ptr)` or `BindExternal` for a stale-checkable handle — instead
of reading `board.Output(stat)`. For per-row bounds, `Clamp.Columns(min*, max*)`;
for constants, `Clamp.Constant(lo, hi)`.

### The composed frame

`Sv.Compose` runs the same pieces in clause-7 frozen order behind one call. The host
supplies a `struct World : IFrameWorld` with the four duties the composition layer
refuses to own — `Reconcile`, `DispatchEvent`, `RunIntrinsics`, `RunConditions`:

```csharp
using var input = InputState.Create(axes: 2, buttonWords: 1, ringTicks: 128);
var host = Game.Create(schema, capacity)        // Sv.Compose
    .Event<Landed>()                            // sparse channel, T : unmanaged
    .Timeline<Jump>()                           // tl step: Apply/Transform/Advance
    .Kernel<Fall>()                             // ecs-kernels step
    .Build(world, input);

host.Frame.Tick(dt, axes, buttons);             // input capture + one frozen frame
host.Record("run.tape");                        // per-frame inputs + digest + body
// fresh host later: replay.Playback("run.tape") — digest-checked bit-identical
```

Full listing — a real tl timeline, a gravity kernel, and tape replay on 100k entities —
in [samples/Sv.Samples/FrameSample.cs](samples/Sv.Samples/FrameSample.cs); the
entity-adapter walkthrough (`Sv.Frent`, `FrentBoard.Bind`/`Unbind`, `Reconcile`) lives
in [sv-examples](https://github.com/IAFahim/sv-examples).

## The Composition Contract — v1.0.3

Clause index (17), verbatim from the vendored master copy:

1. **Strict two-step IEEE float policy**; no FMA anywhere (grep-enforced); StrictFloat oracle vendored.
2. **Clamps are ternary/select** (compare+blend); never Min/Max on floats; ints may use Avx2.Min/Max.
3. **Canonical aggregation order**: (op-kind, insertion id) strict left fold, scalar per set; vertical-only vectorization, width-invariance receipts required.
4. **Recalc ends in a broadcast** into bound columns; per-entity board reads are tooling-only; no gathers.
5. **Dense slots**; adapter owns entity→slot at create/destroy; single chunk shape `RunChunk`/`RunEnabledChunk` (+ `Span` twins).
6. **Condition ingestion by run-walk** (tzcnt, `length == 64` guard); semantics conform to `mask-fixtures.json`.
7. **Frozen frame order**: input edges → drain prev events → stat recalc + broadcast → tl Apply/Advance → kernels → condition run-walk → write events → swap. Mid-frame structural changes apply next frame. Intrinsics writes happen inside event-drain handlers (step 2) and condition responses (step 6), and both read frame-frozen bounds from the previous broadcast.
8. **Runtime dependencies: none**; tl and ecs-kernels are test-only references.
9. **Bool normalization**: any nonzero byte = true; compare-≠0 → movemask before any bit trick; arithmetic OR of raw bytes banned.
10. **Clamp bounds**: Constant / Columns / Stat; Stat resolves at construction to the bound output column; bounds are frame-frozen; no clamp–modifier cycles.
11. **Axis bit-capture**: raw uint bits in the ring; replay is bit-verbatim.
12. **Pointer stability**: bound output pointers (`BindOutput`) and `Clamp.Stat`-captured columns are stable from warmup until an explicit rebind; any storage growth forces re-resolve before the next op; use-after-rebind detected in checked builds.
13. **tl effect columns accumulate** (`+=`): (a) one column, one writer kind — store-writers (stats broadcast) and accumulate-writers (tl) never share a column; (b) the host zeroes each tl effect column once per frame, immediately before the timeline step of clause 7; (c) tl's position in the frozen order fixes the add order.
14. **Warmup before parallel frames**: all timeline ids loaded and first-use folds completed during warmup; first-use inside a warm frame is a contract violation, asserted in checked builds.
16. **Pinned float→int conversion for stat bounds**: NaN → 0; saturate to [int.MinValue, int.MaxValue]; otherwise truncate toward zero. Implemented identically in scalar and vector paths (sv: `Clamp.BoundOf` / `Simd.BoundsOf`); raw `(int)float` casts on bound paths are banned — ECMA leaves out-of-range conversion unspecified and JIT tiers and ISAs disagree (x64 `cvttss2si` indefinite vs ARM `fcvtzs` saturating).
15. **Stats is float/int only; no double effect columns** (tl's double combine branch never exercised); any shipped `IBlend` follows clauses 1–2 and prefers pre-folded tables so Blend never runs at runtime.
17. **Events are sparse signals; dense per-row payloads move through columns**: emitters and makers exist for edge-triggered facts (threshold crossed, timeline finished, damage dealt), never as a per-row per-frame data channel — full-density payload traffic goes through broadcast/accumulate columns per clauses 4/13. Hosts must not build full-density make-call loops in perf-critical frames: on .NET each maker call pays the `UnmanagedCallersOnly` entry gate (~10 ns, IL stub + TLS, measured in sv issue #9); Unity/Burst hosts compile the same `delegate* unmanaged` signature natively via `BurstCompiler.CompileFunctionPointer` and pay no such gate. The maker signature stays fixed (portable-surface rule); a batched emitter may be added later only if a real scenario demands near-dense emission, as a versioned contract change.

**Portable-surface rule** (from Amendment B):

Unity hosts are C# 9: `static abstract` interface members are .NET-only wrappers.
The portable, Burst-compatible core for all walkers and emitters is
`delegate* unmanaged<int, int, T>`; Unity falls back to the fnptr core or mask-level
escapes (`WalkNonZero`). Same pattern tl.unity's fork uses for the same reason.

## Performance receipts

`benchmarks/Sv.Bench` (100k rows/frame, AVX2, Ryzen dev box, 2026-10-10 after atoms
22–38): intrinsics chunk ~6 G rows/s (bandwidth-bound; struct-specialized loops per
clamp kind × delta × applied); stat clamp ~4 G rows/s (`Clamp.StatMirror*` 5–10%
faster and stride-capable); enabled run-walk ~4.5 G rows/s on an alternating mask
(full words take the dense loop, partial words AVX2 masked load/store — disabled rows
are never touched); stat add+remove+recalculate 22 ns, and the buff-expiry pattern
(a slot rejoining a population-wide set) 16 ns — join-only broadcast writes one row,
not the set; input capture 6 ns; every scenario verified against a scalar reference
and prints `alloc=0B`. The composed one-call frame (`frame-run`) runs at parity with
the hand-rolled fused tick (~0.67–0.70 ms/frame at 100k rows — the composition layer
itself is free); the determinism tape adds ~0.06 ms/frame (tape v3: 64-bit XXH64
digest + body over live rows, fully live words copied whole; was ~0.6 ms with v2's
byte-serial FNV-1a). Events: dense emit (15k events/frame) 455 µs through
`delegate* unmanaged` makers vs 97 µs through struct `IEventMaker<T>` makers (no
`UnmanagedCallersOnly` gate); parallel writers scale (8 writers: 793 M events/s
aggregate, per-writer cache-line-padded locks). Standing `NoAllocationTests` assert
exact-zero managed bytes for every warm verb; the managed-state audit shows zero
managed collections outside `Schema` (cold authoring). The native bill is a receipt
too: `Board.MemoryBytes` reports everything the board owns (a 1M × 3-stat board
commits ~15 MiB empty — pair storage is paged and allocated on first touch — and
~78 MiB fully live) and `Board.RequestedCapacity` sits beside the 64-rounded
`Capacity`, so alignment padding is visible. Determinism is proven across ISAs: a
baked 64-bit scenario digest is identical on the AVX2, 128-bit and scalar paths, and
the `determinism` CI matrix runs it on x64, arm64, macOS and Windows.

<!-- bench:begin (auto-generated — CI refreshes this on every push to main; do not edit by hand) -->

Last refreshed 2026-10-21 23:30 UTC · `main@c7cf0b0` · AMD EPYC 9V74 80-Core Processor (2 cores) · sv-bench rows=100000 vector256=True vector128=True

| Scenario | µs/frame | rate | Managed alloc |
|---|---:|---:|---|
| intrinsics-uniform | 199.346 | 501.6 M rows/s | 0 B |
| intrinsics-varying | 23.934 | 4178.2 M rows/s | 0 B |
| intrinsics-statclamp | 32.668 | 3061.1 M rows/s | 0 B |
| intrinsics-statmirror | 21.851 | 4576.5 M rows/s | 0 B |
| enabled-runwalk | 70.145 | 1425.6 M rows/s | 0 B |
| stat-recalc | 0.057 | 57 ns/op | 0 B |
| stat-churn | 0.044 | 44 ns/op | 0 B |
| events-emit | 338.024 | 295.8 M rows/s | 0 B |
| events-emit-struct | 105.825 | 945.0 M rows/s | 0 B |
| input-capture | 0.011 | 11 ns/op | 0 B |
| fused-tick | 1084.842 | 92.2 M rows/s | 0 B |
| frame-run | 1062.792 | 94.1 M rows/s | 0 B |
| tape-digest | 1159.831 | 86.2 M rows/s | 0 B |
| ff1m | 2943.0 | p99 4801.0 µs · 1048576 rows · Δ=128 | 0 B |

<!-- bench:end -->

## Atoms

| # | Atom | Status |
|---|---|---|
| 1 | Schema + Board slots + `Clamp` (Constant/Columns) + `RunIntrinsicsChunk`/`RunEnabledChunk` (+ `Span` twins), ternary clamps, applied-delta — [issues/0001](issues/0001-atom-1.md) | done |
| 2 | Stats: insertion-id modifiers, content-deduped sets, canonical fold, `BindOutput` broadcast, `Clamp.Stat`, StrictFloat oracle vendored | done |
| 3 | Event streams: pooled double buffer, parallel writer, fnptr walkers, mask conformance via `contract/mask-fixtures.json` | done |
| 4 | Input: bit-captured axes, button edges, replay ring | done |
| 5 | SIMD pass: vector chunk clamps, scan words, ANDNOT edges — width-invariance receipts, pinned stat-bound float→int (NaN→0, saturate), zero FMA | done |
| 6 | Adapter: Frent chunk seam (`sv.frent`, SvSlot mapping, zero-copy contiguous fast path, Pull/Push fallback) + Unity 6.7 handoff record | done |
| 7 | North-star fused test — .NET leg complete: stage A ([issues/0008](issues/0008-atom-7.md)) + stage B real kernel bit-identical ([issues/0010](issues/0010-atom-7-stage-b.md)); Unity cross-host handed to the integrator ([issues/0015](issues/0015-atom-12.md), [issues/0016](issues/0016-atom-13.md)) | done (.NET) |
| 8 | Perf: benchmark harness, perf receipts, zero-allocation tests, managed audit, run-walk inlining (+81%) ([issues/0009](issues/0009-perf-bench.md)) | done |
| 9 | Binding generations + sv-owned output columns: `BindExternal` handle, `InvalidateBindings` stale-throw (checked), `OutputPtr`/`Output` self-owned broadcast — [issues/0012](issues/0012-atom-9.md) | done |
| 10 | Slot renumbering: `Board.RenumberSlots`, `Sv.Columns.Permute`, `Sv.Frent.Population` fused chunk windows + `Reconcile()` 64-aligned healing — [issues/0013](issues/0013-atom-10.md) | done |
| 11 | `Sv.Compose`: Game/GameHost/`Frame.Run` frozen-frame composition (clause 7 verbatim), static-generic step dispatch, tl naming seam absorbed, 0 B warm — [issues/0014](issues/0014-atom-11.md) | done |
| 12 | `Sv.Unity` adapter (com.sv.unity): SvSlot/UnityPopulation/SvChunk + svSystemBase pattern; .NET facade tests, editor leg is the integrator handoff — [issues/0015](issues/0015-atom-12.md) | done (.NET) |
| 13 | North-star on `Frame.Run`, bit-identical to the pre-migration manual digests (baked oracle test); determinism tape `Record`/`Playback`; sv-owned outputs replace the NativeMemory blocks — [issues/0016](issues/0016-atom-13.md) | done |
| 19 | Checked-only `BindOutput`/`BindExternal` column length (`columnLength: -1` legacy, >= 0 guarded per broadcast write, zero release cost) — [issues/0024](issues/0024-atom-19-bind-column-length.md) | done |
| 20 | O(1) set-member removal: per-pair member-position index, swap-remove with fix-up — bulk modifier migration off million-member sets drops from seconds to microseconds — [issues/0025](issues/0025-atom-20-o1-member-removal.md) | done |
| 21 | `Board.MemoryBytes` + `Board.RequestedCapacity`: the eager memory bill as a receipt, the 64-rounding loud — [issues/0026](issues/0026-atom-21-memory-receipt.md) | done |
| 22 | Bench rate label fixed (was rows/ns printed as M rows/s — 1000x understated); stray `SetTable` probe allocations removed — [issues/0027](issues/0027-atom-22-bench-rate-and-settable-probe.md) | done |
| 23 | Join-only broadcast: set values are immutable, so a slot joining a folded set writes one row instead of rebroadcasting the set; bind refills the column — churn 30.8 µs → 13 ns — [issues/0028](issues/0028-atom-23-join-only-broadcast.md) | done |
| 24 | Specialized chunk loops: struct strategies for clamp kind × delta × applied, branch-free per instantiation, C# 9 portable — statclamp −19%, varying −24% — [issues/0029](issues/0029-atom-24-specialized-chunk-loops.md) | done |
| 25 | Vectorized enabled run-walk: full words take the dense loop, partial words AVX2 masked load/store (disabled rows never touched), `Span` twins vectorized behind up-front length guards — run-walk 2.8× — [issues/0030](issues/0030-atom-25-vector-runwalk-and-span-twins.md) | done |
| 26 | Per-writer cache-line-padded event locks + word-batched emit — 8 parallel writers 2.7 → 793 M events/s — [issues/0031](issues/0031-atom-26-per-writer-event-locks.md) | done |
| 27 | Tape v3: XXH64 64-bit digest (verified against the reference at every length 0–300 + published vectors), whole-word body copy — tape overhead ~576 → ~58 µs/frame at 100k rows — [issues/0032](issues/0032-atom-27-tape-v3-xxh64.md) | done |
| 28 | **Fix**: recalc permuted stored modifier ids in place, so `RemoveModifier` could remove the wrong modifier (since atom 2); independent-oracle churn tests added — [issues/0033](issues/0033-atom-28-fix-modifier-id-permutation.md) | done |
| 29 | Paged pair storage (4096-slot pages, metadata on first acquire, modifier lists on first modifier, 16 B lists) — empty 1M×3 board 110 → 15 MiB; renumber leak of released slots' buffers fixed — [issues/0034](issues/0034-atom-29-paged-pair-storage.md) | done |
| 30 | Cross-host determinism: baked 64-bit digest over a full churn/clamp/run-walk/events scenario, identical on AVX2 / 128-bit / scalar paths; CI matrix x64 · no-AVX2 · scalar · arm64 · macOS · Windows — [issues/0035](issues/0035-atom-30-cross-host-determinism.md) | done (ARM legs on next CI run) |
| 31 | Struct event makers `IEventMaker<T>` (C# 9 portable, inlined, no entry gate) beside the fnptr emitters — dense emit 450 → 98 µs — [issues/0036](issues/0036-atom-31-struct-event-makers.md) | done (contract: bus 0036 pending) |
| 32 | **Fix**: pairs queued for a slot released before `Recalculate` made the dead slot a set member; after `RenumberSlots` it became member −1 and broadcast wrote `column[-1]` (heap corruption, since atom 10) — [issues/0037](issues/0037-atom-32-fix-dead-slot-queue.md) | done |
| 33 | **Fix**: `Clamp.Stat*` read stride-bound columns as contiguous (wrong bounds); now rejected at construction — [issues/0038](issues/0038-atom-33-fix-clamp-stat-stride.md) | done |
| 34 | Stat-bound mirrors: `Board.MirrorBounds` keeps an sv-owned int `BoundOf` column written at broadcast; `Clamp.StatMirror*` clamps through the int-column path and works for any binding stride (~5% over `Clamp.Stat`) — [issues/0039](issues/0039-atom-34-stat-bound-mirrors.md) | done (contract: bus 0036 pending) |
| 35 | Modifier lifetimes: source tags (`RemoveSource`) and expiry ticks (`ExpireModifiers`, deterministic (tick, id) heap), renumber-safe, 0 B warm — [issues/0040](issues/0040-atom-35-modifier-sources-and-expiry.md) | done (contract: bus 0036 pending) |
| 36 | Changed masks: `ChangedWords(stat)` — rows whose broadcast bits changed, clause-5 word span, cleared in O(changes); feeds the run-walk directly — [issues/0041](issues/0041-atom-36-changed-masks.md) | done (contract: bus 0036 pending) |
| 37 | Derived stats: `SchemaBuilder.Derived(name, base, (source, coefficient)...)` — synthetic canonical entries keep sets immutable and deduped; acyclic by declaration order — [issues/0042](issues/0042-atom-37-derived-stats.md) | done (contract: bus 0036 pending) |
| 38 | State save/load: `SvState` + `SaveState/LoadState` on Board, InputState, EventStreams and `Game` — exact set identities, 0 B warm, rollback resimulation bit-identical (100k × 3 stats: save 1.1 ms / load 0.9 ms) — [issues/0043](issues/0043-atom-38-state-save-load.md) | done (contract: bus 0036 pending) |
| 39 | **Fix**: idempotent `Game.Dispose` — the `Applied` column freed twice under `using` + explicit dispose (heap corruption) — [issues/0045](issues/0045-atom-39-idempotent-game-dispose.md) | done |
| 40 | **Fix**: `BeginReplay` seeds button state from the prior ring entry — held buttons replayed as freshly pressed across a rollback boundary, diverging resimulation — [issues/0046](issues/0046-atom-40-replay-button-seeding.md) | done |
| 50 | **Contract**: intrinsics integer adds saturate before the clamp (owner ruling on bus 0039: SATURATE) — full heal no longer wraps to death; total `clamp(saturate(before + delta))` oracle pinned across scalar/128/256/masked/enabled/remap paths — [issues/0057](issues/0057-atom-50-saturating-integer-add.md) | done (contract: amendment proposed) |
| 41 | **Fix**: byte-count `NativeMemory` allocations — the two-argument overload multiplies (count × size), so 28 sites over-allocated up to 16×; source-scan guard — [issues/0048](issues/0048-atom-41-byte-count-allocations.md) | done |
| 42 | **Fix**: `LoadState` keeps mirrors alive (cached `Clamp.StatMirror*` no longer dangles across rollback to a pre-mirror snapshot), refills local-only mirrors, and rebroadcasts bound stats from the restored state — [issues/0049](issues/0049-atom-42-loadstate-mirror-stability.md) | done |
| 43 | Set-table hash avalanche (murmur3 finalizer) + ≤50% load cap + state format v2 — value-only-distinct sets shared one bucket at ≤256 buckets (O(chain) Resolve) — [issues/0050](issues/0050-atom-43-set-table-hash-avalanche-load.md) | done |
| 44 | **Fix**: event arena growth validated — `bytesPerWriter: 0` hung the writer spinlock forever; deterministic 1 GiB cap instead of int-overflow `Realloc`; lock released on failure — [issues/0051](issues/0051-atom-44-event-stream-hardening.md) | done |
| 45 | **Fix**: `ChunkRange`/`EnabledRange`/`EnabledTail` always-on (release builds unchecked the vectorized pointer paths); single-word seam polices tail bits — full-ones word on a partial tail wrote past the chunk — [issues/0052](issues/0052-atom-45-always-on-guards.md) | done |
| 46 | **Fix**: spawn visibility — released rows zeroed (previous occupant's values leaked into spawns and change-detection), acquired slots change-marked, renumber clears stale masks — [issues/0053](issues/0053-atom-46-spawn-visibility.md) | done |
| 47 | **Fix**: `Columns.Permute` scratch zero-filled (padding slots read garbage); `Reconcile` validates capacity and bound-slot count before any rewrite — a failed reconcile no longer diverges world and board — [issues/0054](issues/0054-atom-47-permute-and-reconcile-atomicity.md) | done |
| 48 | Determinism surface: tape digest reads the effective (bound, strided) column — bound stats were digest-invisible; computed NaNs canonicalised to `0x7FC00000` at broadcast (x64/ARM64 sign-bit desyncs) — [issues/0055](issues/0055-atom-48-digest-coverage-and-canonical-nan.md) | done |
| 49 | `Game.Renumber(perm)` — timeline ids, clocks, applied and effect columns permuted with the board at reconcile; `Columns.Permute` count contract documented — [issues/0056](issues/0056-atom-49-game-renumber.md) | done |
| 51 | FF-1M benchmark (roadmap [issues/0058](issues/0058-roadmap-1m-entities-5us.md) §2.2): 1M entities × 16 stats × 4 intrinsics × 2 timed modifiers, 1 timeline, 4 channels, Δ=128 busy frame — **measured p50 3,134 µs/frame, board 887 MiB, snapshot 558 MiB** (the gap the O(Δ) design closes) — [issues/0059](issues/0059-atom-51-ff1m-bench.md) | done |
| 52 | Guard-page fuzz harness (`tests/Sv.Fuzz` child + pinned-outcome fact): every public window API against `PROT_NONE`-flanked columns — **F1/F2/F3/F23 reproduce as 11 pinned crashes today; atom 1.1 flips them to throws** — [issues/0060](issues/0060-atom-52-guard-page-fuzz.md) | done |
| 53 | The eager oracle, frozen: independent managed model of the canonical fold (synthetic entries, set-change propagation) and the saturating intrinsics step; random programs (600 ops × 5 seeds) and 240 frame rounds pin production bit-for-bit — the reference for laws L1/L7 — [issues/0061](issues/0061-atom-53-frozen-eager-oracle.md) | done |
| 54 | **Fix** (roadmap 1.1): exact window validation — F1 Frent enabled offset/wordCount, F2 masked `EnabledTail` shift + non-exact multi-word counts, F3 span-length trust at the seams, F23 int-overflow range guards; guard-page fuzz table flipped to all-throws (raw `int*` Board API pinned length-blind by design) — [issues/0062](issues/0062-atom-54-exact-window-validation.md) | done |
| 55 | **Fix** (roadmap 1.2): bijective `Reconcile` (seen-marking first pass — a counterbalanced duplicate+missing no longer rewrites the world before throwing, law L11), idempotent exception-safe `Bind`/validate-first `Unbind` (F6), idempotent `Population.Dispose` (F7) — [issues/0063](issues/0063-atom-55-reconcile-bijection-bind-unbind-dispose.md) | done |
| 56 | `AcquiredWords(stat)` (roadmap 1.4, F10): acquire marks leave `ChangedWords` (value change only — no more mixing with the previous frame's mask), a base-0 stat's spawn is visible in `AcquiredWords` and never in `ChangedWords` — [issues/0064](issues/0064-atom-56-acquired-words.md) | done |
| 57 | **Fix** (roadmap 1.7): state integrity — `Schema.ContentHash` (XXH64 over names/bases/sources) kills the state-shape count collisions (F18); the composed game validates its header before any component loads, so a mismatched `LoadState` throws with everything untouched (F17); `XxHash64` moved to Sv — [issues/0065](issues/0065-atom-57-state-integrity.md) | done |

Atom 1 scope note: `Clamp.Stat` and the StrictFloat oracle land with atom 2 (the first
float math); the walkers land with atom 3. Nothing ships half-wired.

## Discipline

Mirrors tl's `AGENTS.md`: issues carry the spec, atoms are short and green before the
next starts, receipts (commands + outputs) are recorded in the issue, production code
carries no explanatory comments, the data path is unmanaged, and every public
abstraction pays for itself. GitHub issues are the record; `issues/` keeps the
authored copies.

Before starting and before finishing any atom, check the agent bus:
`/home/i/GitHub/agent-bus/bin/check stats --body`. Reply and ack per
`/home/i/GitHub/agent-bus/README.md`. Cross-repo decisions never live only in chat.
