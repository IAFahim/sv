owner-approved, bus unreachable (owner request 2026-11-12)

# The Composition Contract - v2.1.0 (amendments over v1.0.3)

**Step A0, ratify what is already built (no code change, no digest moves).** The code is ahead of the vendored v1.0.3: the v1.1.0 batch (bus 0036), clause 18 (saturating add, bus 0042), the seven v1.2 items of `issues/0097`, and the implemented v2.0 items (history-free fold, `Cap` kind, indirect stats, crossings on the wheel). Their pins already moved at atoms 90 and 98. v2.1.0 starts by folding that text in as written in `issues/0097`.

**Step A1, the new text.**

> **Clause 3, appended.** The canonical order of a modifier set is (op-kind, value bits) over the kinds Added, Additive, Multi, Cap; insertion ids are removal handles only. Accumulation into columns is ordered by clause 13, not by this clause.

> **Clause 4, appended.** A host whose stat storage is chunked binds a sink instead of a column. Recalculate appends one (slot, stat, value bits) record per changed cell in ascending (slot, stat) order, and the host stores every record before the intrinsic fold of the same frame. A record carries the canonical bits a bound column would have received. Template spawn appends nothing; the host fills spawned rows from the template.

> **Clause 7, replaced.** Frozen frame order: input edges -> commands and expiry, as modifier operations in canonical order -> stat recalc + broadcast -> intrinsic fold -> timeline step -> kernels -> condition and event derivation -> swap. Mid-frame structural changes apply next frame. Store columns (stat outputs, intrinsic values, condition bits, sense columns) are read-only from the end of the fold to the end of the timeline step. Intrinsic values change only in the fold, in event-drain handlers and in condition responses. The fold reads the bounds broadcast in the same frame; drain handlers read the previous broadcast. Accumulate columns written in the timeline step of frame t are consumed by the fold of frame t+1.

> **Clause 12, appended.** On chunked hosts no pointer into host storage outlives the job that resolved it. sv retains only slot-indexed arrays that it owns; they are fixed in capacity and address from warmup to dispose.

> **Clause 13, replaced.** Accumulate columns (`+=`): (a) one column, one writer kind, one consumer; store-writers and accumulate-writers never share a column; (b) every cell of an accumulate column is zero when the timeline step begins; the consumer restores zero in each cell it consumes, and a column without a registered consumer is zeroed by the host; (c) a row's cells are written only by the worker that holds the row, in (system, kind declaration, entry) order; (d) integer accumulation is exact 32-bit wrapping addition; each contribution is bounded (magnitude at most 2^20, at most 2^10 writer kinds per column) so that no sum wraps, and checked builds trap a wrap; (e) a contribution to another row is an integer, is added atomically to that row's 64-bit inbox cell, and is consumed by the same fold; (f) float accumulate columns are row-local.

> **Clause 17, appended.** Events raised by the fold are derived from edge bits after the parallel step, per channel in ascending slot order. A channel has a build-time capacity. When a frame's edges exceed it, the channel delivers one Dense record carrying the count and no per-row events; the edge bits stay readable until the next fold, and every handler of the channel provides a mask form. The portable maker is the struct maker.

> **Clause 19, new. Intrinsic fold.** For each live row of a dense intrinsic, once per frame: after = clamp(before + saturate32(delta + inbox), min(floor, ceiling), ceiling), computed without intermediate overflow; delta and inbox become zero; applied = saturate32(after - before). The result is a function of the multiset of the frame's contributions. An implementation of any width, on any host, equals the scalar reference bit for bit.

> **Clause 20, new. Connections are data.** Every use of a column by a kind is declared as (kind, column, mode, scope), with mode in {Read, Check, Accumulate, Delta, Modifier, Event} and scope in {Self, Remote}. The table is validated at load against the schema, and its hash is part of state identity. Modifier and Event connections act at clip edges through canonical batches; modifier operations are ordered by (slot, operation with remove before add, source, stat, kind, value bits).

> **Portable-surface rule, replaced.** Unity-visible sources are C# 9 on netstandard2.1. The portable core is static functions over pointers and plain structs, with struct-generic strategies. `static abstract` members, `delegate* unmanaged` and `System.Runtime.Intrinsics` are .NET-only front doors over the same core.
