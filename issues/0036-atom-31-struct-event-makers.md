# Issue 0036 — Atom 31: struct event makers (`IEventMaker<T>`) — no entry gate on .NET

Filed as GitHub issue #36 (pending — see receipts). Labels: `ai`, `area:events`,
`kind:feat`, `contract:proposed`. Contract: bus 0036 amendment 1 (clause 17,
additive) — sv's vendored contract stays v1.0.3 until the integrator acks.

## Problem

Every `EmitFrom*` call per emitted event crossed a `delegate* unmanaged` boundary into
an `[UnmanagedCallersOnly]` maker: a managed->native transition plus the reverse stub.
At 15k events/frame that was ~30 ns per event and >90% of `events-emit`.
`SuppressGCTransition` cannot legally target an `UnmanagedCallersOnly` method.

## Shape

- `public interface IEventMaker<T> { T Make(int row, int value); }` — an *instance*
  interface (C# 9 / Unity-compilable, no `static abstract`).
- Six new overloads `EmitFromMask/EmitFromBools x2/EmitFromNonZero x2<T, TMaker>(...,
  ref TMaker maker) where TMaker : struct, IEventMaker<T>`. The JIT specializes per
  struct, so `Make` inlines into the word loop. Passed by `ref`: makers may carry state
  (counters, lookup pointers), and that state is visible to the caller afterwards.
- The six existing `delegate* unmanaged` emitters are unchanged in signature and
  behavior; internally they wrap the pointer in a private `PointerMaker<T>` struct and
  share the same generic core (one code path, two front doors).

## Receipts

- `StructMakerTests`: struct and pointer makers produce identical streams across all
  six emitters (ranges incl. empty, tails, unaligned starts; masks incl. 0/~0/edge
  bits; three offsets); a stateful maker sees events in emit order through `ref`;
  warm struct emit + swap + drain allocates 0 B over 1000 frames.
- Bench (100k rows, ~15k events/frame): `events-emit` (pointer) 450 us,
  `events-emit-struct` 98 us (4.6x).
- Tests default: 156 + 21 + 6 passed, 0 failed. SV_CHECKED: 158 + 21 + 6 passed.
- no-FMA grep clean.
