# Issue 0076 — Atom 68 (roadmap 3.1): sparse change rows — `changedTouched` exposed per stat

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) phase 3, atom 3.1
(§4.6, S4). Labels: `ai`, `area:core`, `kind:feat`. Contract: additive —
new accessors beside `ChangedWords`, no existing signature moves.

## Changes

- **`changedTouched` becomes per-stat.** The backing array keeps its size
  (`statCount × LiveWordCount` ints) but partitions into one region per
  stat holding that stat's **local** word indices (unique, arrival order,
  appended when a word transitions 0 → nonzero — the dedup `MarkChanged`
  already had). A new `changedTouchedCounts` array (statCount ints,
  arena-tracked) replaces the single global count. Clearing walks only the
  touched entries, exactly as before.
- **Public sparse accessors** beside `ChangedWords`:
  - `int* ChangedWordIndices(StatId)` — indices into that stat's
    `ChangedWords` span;
  - `int ChangedWordCount(StatId)` — how many are live.
  A condition consumer walks O(changed words) instead of `LiveWordCount`
  words per stat (16,384 at FF-1M). Dense `ChangedWords` is unchanged and
  stays the clause-5 word-span shape.
- **FF-1M `RunConditions`** switches `WalkChanged` to the sparse walk —
  the S4 receipt.
- **State format v9 → v10**: the changed-mask section persists the
  per-stat counts array plus each stat's index prefix. Broadcast values,
  slot assignment and every digest-relevant bit are untouched — the
  cross-host digest pin must not move; that is itself a receipt.

## Tests

- New `SparseChangeRowsTests`: sparse ≡ dense under the random
  add/remove/expire program (every frame, both stats); indices unique;
  `SaveState → LoadState → SaveState` byte-stable and the sparse view
  (counts + indices + words) survives the round trip exactly; a stat
  nobody touches reports count 0; acquired masks keep their behavior.
- `CrossHostDigestTests` pin unchanged (bit-identical values law).

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **298/298 passed** (294 + 4 new);
  3× repeat stable.
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 298/298.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27.
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok:
  ff1m p50 2,847 µs (eager intrinsics passes dominate; the walk saving is
  inside run noise), board 150 MiB unchanged, alloc 0 B everywhere.
- **S4 receipt, measured at 1M rows** (temporary bench probe, since removed):
  the 4-stat conditions walk over a busy frame's masks — sparse **0.270 µs**
  (24 touched words) vs dense 20.299 µs (65,536 words scanned) — **75×**.
- Values stayed bit-identical through the v10 bump: the cross-host digest pin
  `0x73D6FCED95A8BA9D` passed unmodified — that is the additive-contract
  receipt.
- FMA grep clean; no production comments.

## Provenance

`main@6ee40f6` + this atom.

Bus note: `/home/i/GitHub/agent-bus` does not exist on this host
(`/home/os`), so the pre/post-atom bus checks could not run; recorded
here per AGENTS.md discipline.
