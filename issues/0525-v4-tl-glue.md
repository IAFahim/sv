# 0525 v4 and tl: generated glue for column packs

Status: open

## Need
v3 has `sv pack <folder> --glue tl`: an outbox column per timeline channel, clips, tracks and `TimelineRequests`, so a ticking status is a tl timeline whose ticks reach sv as records (README chapter 7). v4 column packs have no equivalent. Today a game connects them by hand (`docs/v4-guide.md` section 6): a timeline sets a card lane, or game code turns an outbox column into `SendFrom` calls.

## Proposal
1. Prefer cards: most v3 timeline statuses (poison, burning, regen) become v4 cards whose lane a timeline sets on start and a timer channel ends, so no records are sent at all. Glue would emit, per card, the tl request that sets its lane.
2. For timelines that must emit amounts, emit an outbox column per channel and one `Send` pass before the step, matching v3's `Outbox.Send` (source = the status slot, target from an aim lane).
3. Prove it as v3 did with NorthStar3: one scenario run through tl and through hand-written sends must give equal digests, in .NET and in Unity.

## Progress
- The owner chose cards first as the default, with the outbox kept for ported v3 code.
- Issue 0528 added the Entities runtime both paths need: `ColumnsState.Sets` (lane edits between frames), `ColumnsCardSystem<T : IColumnsCardCell>` and `ColumnsOutboxSystem<T : IColumnsOutboxCell>`, proved against a hand-stepped `ColumnWorld`. What is left here is the generator (cells and tl request rules per column pack) and the .NET proof.

## Not done yet because
It changes tl's generator and the v4 pack format together, and tl is changing in parallel; it needs the owner's choice between 1 and 2 as the default.
