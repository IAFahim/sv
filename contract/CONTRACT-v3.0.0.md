owner-approved, bus unreachable (owner decision D7, 2026-11-12)

# The Composition Contract - v3.0.0 (amendments over v2.1.0)

v3.0.0 keeps every clause of v2.1.0 that is not named below. Owner decisions in force: D1 both storage policies (shared lanes default,
dense identity table), D2 16-byte record, D3 ring of 32 epochs of 2 frames at 30 Hz (configurable), D4 sum then clamp once per frame,
D5 sv-only replay with the game deciding per retracted event, D7 this text, D8 sourced timeline effects as a status entity, D9 outward
event channel capacity 16,384, D10 arena full grows once and reruns, with a hard cap option.

> **Clause 7, replaced. Frame order.** A frame n+1 is, in this order: (1) Seal: the raw records of frame n become the canonical records;
> (2) Stats: expiry, then stat operations in canonical order, then recalculation; changed stats are written into their lanes; (3) Decide,
> per active chunk, reading persistent lanes only: Gate (each record gets its taken amount per hit), Remove (records nothing later needs
> leave the list), Emit (records aimed at other entities go to the raw records of frame n+1), Flow (channel totals and same-frame self
> triggers become one delta per intrinsic); (4) Commit, per active chunk, own row only: Fold (clause 19), then Events (level bits by
> hysteresis, edges become outward messages and on-edge emits); (5) Publish: outward events in ascending slot order, digest, epoch mark.
> Persistent state is frozen from the end of Publish to the start of the next Stats. Everything else (timelines, kernels, game systems)
> runs after Publish and affects sv only by sending records.

> **Clause 13, narrowed.** Accumulate columns exist only as outboxes of generated glue. Their single consumer is Collect, which turns every
> non-zero cell into a record and restores zero in that cell.

> **Clause 19, kept** as written in v2.1.0. The inbox term is zero; the delta is the Flow result of clause 7.

> **Clause 20, replaced. Connections are the content pack.** Values, channels, events, kinds, templates and rules are rows of a content
> pack with stored numeric ids that are never reused. The pack hash is part of state identity.

> **Clause 21, new. Records.** An input is a 16-byte record: `ulong Key | int Amount | int Hits`. Key bits 63..42 target slot (22),
> 41..20 source slot (22, all ones = no source), 19..12 source kind (8), 11..4 channel (8), 3..2 origin (Direct, Timeline, Reflected,
> Triggered), 1..0 depth. Records are ordered by (Key, Amount) as unsigned and signed integers. `Key >> 48` is the target chunk. Amount is
> one hit's integer amount; Hits counts identical hits. Zero amounts are not stored.

> **Clause 22, new. Canonical inputs.** A frame's inputs are a multiset of records. Seal's output is its only representation: sorted by
> clause 21, equal (Key, Amount) neighbours merged with Hits added in 64 bits and saturated once. When the canonical records exceed the
> host capacity they are merged in canonical order in three levels until they fit: equal Key; then across sources per (target, kind,
> channel); then across kinds. Totals per (target, channel) are conserved at every level and the world raises `Overflow` with the level.

> **Clause 23, new. Layers.** A layer reads only what an earlier layer finished; persistent cells change only in Commit (A4). Work for
> target t writes only t's cells; anything for another entity is a record for the next frame (A5). Two layers may run fused per chunk if
> and only if the later reads no cell of another chunk that the earlier writes (T5).

> **Clause 24, new. Gates.** Admit is decided by the applicable rule with the highest priority; no applicable rule admits. Equal priority
> with different Admit verdicts on overlapping conditions is rejected when the pack compiles. Shape applies the modifier algebra Added,
> Additive, Multi, Cap, then `BoundOf`, only to admitted records. Absorb consumes a budget in canonical record order.

> **Clause 25, new. Emission.** An emit to another entity is a record of the next frame with depth + 1, source = the emitting entity and
> kind = its `Kind`; a rule never emits past its depth limit and the world limit is 3. An emit to self is a same-frame delta, ordered into
> strata by read and write dependency; a cycle is rejected when the pack compiles unless one edge is marked next frame.

> **Clause 26, new. Lanes and epochs.** A value is one 4-byte cell at (slot, value id) with `slot = chunk * 64 + row`. Storage is in
> lanes of 64 cells (256 bytes); event lanes hold 32 words of 64 level bits. A chunk is a table of lane references plus its meta lanes
> (live, dying, template, death epoch). Policy shared: a lane equal to its template default is the template's read-only lane; the first
> write copies it. Policy dense: the table is the identity and a chunk is one contiguous block. The first write to a lane in an epoch
> journals what it replaces (the old reference, or under dense a copy of the old content); later writes in the epoch are plain. The ring
> keeps the journals of the last R epochs (R = 32 by default). Digests sum `CellDigest.Key(lane index, slot, bits)` over live cells that
> differ from their template default; lane references, pool addresses and free rows are never hashed. A chunk image is canonical: it
> does not depend on pool history and free rows read as defaults. A slot is Free, Live or Dying; a released entity is Dying: no layer
> visits it, its lanes stay, and its slot is not reused while its death epoch is inside the ring. A record aimed at a Dying or Free slot
> is dropped in Seal and counted.

> **Clause 27, new. Replay.** The input log keeps, for the ring's span, the canonical records with origin Direct or Timeline; derived
> records are never logged. `Rewind(e)` restores the state at the start of epoch e; replaying the log from there reproduces every digest
> of the straight run. Late inputs are inserted at their frames and replayed from the newest epoch at or before the earliest of them;
> inputs older than the ring are clamped to the oldest epoch or rejected by host policy. Outward events of replayed frames are compared
> with those already published, and the host receives the difference as Retracted and Added.

