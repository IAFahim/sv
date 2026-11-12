# Issue 0131 - Event cap per channel at host build (default 16,384), bounded and tested

Core agent, branch `proto2/core`. Labels: `ai`, `area:core`.

State found: phase 1 (issue 0121) already set the cap per `EventDerive` (one per channel) at
host build, default `EventDerive.DefaultCapacity = 16384` after the sort kill rule, with
65,536/65,537 word-path tests. Not done at the schema: `Schema.ContentHash` feeds pinned
digests and the schema has no event channels, so a schema field would be a format change for
no gain. Host build is the configuration point; the Unity host passes its own value.

## Change

- `EventDerive.MaxCapacity = 1 << 24`; a cap outside [1, MaxCapacity] throws
  `ArgumentOutOfRangeException` naming the range (was: only < 1 checked; a huge cap failed
  inside the allocator).

## Tests (new): `tests/Sv.Tests/EventCapConfigTests.cs` (2, heavy collection, also portable)

- Two channels on one `EventStreams`, one asked for 65,536, one default (16,384), fed the same
  edge words at 16,384 / 16,385 / 65,536 / 65,537 / 0 edges: each decides on its own cap
  (all events sorted by slot, or exactly one `DenseEvent` with the full count and tick).
- Asked cap 65,536: 65,536 events per tick, warm ticks allocate 0 B; caps 0 and
  MaxCapacity + 1 throw.

Receipts in issue 0132 (same build).
