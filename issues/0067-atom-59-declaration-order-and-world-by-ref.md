# Issue 0067 — Atom 59 (roadmap 1.6): declaration-order chains, the world by `ref`, playback bounds

Roadmap [issues/0058](issues/0058-roadmap-1m-entities-5us.md) phase 1, atom 1.6 —
F14, F15, F19. Labels: `ai`, `area:compose`, `kind:fix`. Contract: none — sv-local
break, announced on the bus (integrator inbox 0050).

## Changes

- **F15** — all three builder chains recurse in post-order, so **declaration order
  is execution order and id order** (law L10): `.Event<A>().Event<B>()` gives A the
  lower channel id (channels register innermost-first now);
  `.Timeline<T1>().Timeline<T2>()` runs T1 first and takes `Effect(0)`;
  `.Kernel<K1>().Kernel<K2>()` runs K1 first. Clause 13(c) makes tl order semantic —
  the old reverse order was an accident, now it is the declared contract of the
  builder. Baked digests of multi-step scenarios change; the north-star's baked
  digests are single-of-each and survived unchanged.
- **F14** — the host world is ONE mutable instance: `GameHost.World` is a by-ref
  property over a field, `FrameRunner.Run` takes `ref` to it, and
  `IKernelStep.Run<THost>`/`IKernelChain.Run<THost>` take `ref THost` instead of
  `in THost`. Struct fields in the world now persist across frames; under the old
  by-value copy every field reset each frame and every kernel call re-copied the
  struct. Callers that only read are unaffected (`sv-examples` builds unchanged).
- **F19** — tape playback validates the input-shape header (including negative
  counts) and the axes/buttons byte length **before** reading the inputs; a
  truncated frame is rejected as `InvalidDataException` without touching memory
  past the pinned array.

## Tests (+3, `ComposeOrderTests`)

- `DeclarationOrderIsExecutionOrderAndIdOrder` — a frame with two timelines, two
  kernels and the four frame duties appends exactly `1234567`; the first-declared
  timeline observes `Effect(0)`, the first-declared event the lower channel id.
- `WorldFieldsPersistAcrossFrames` — a counter incremented in `RunIntrinsics`
  reads back 2 after two frames through `host.World`.
- `PlaybackRejectsEveryTruncationCleanly` — every prefix of a 3-frame tape either
  replays cleanly or throws `InvalidDataException`; the fuzz asserts at least one
  rejection (the guard fires) and no other failure mode.

## Receipts

- `dotnet test sv.slnx -c Release` — 255/27/6 passed; `-p:SV_CHECKED=true` — 255;
  north-star baked digests unchanged.
- `sv-examples` builds against the changed source (ProjectReference, SvRoot).
- Bench: `frame-run` 695.2 µs, `tape-digest` parity — the by-ref world and
  post-order recursion cost nothing.
- Bus: sv-local break announced to the integrator (message 0050).

## Provenance

`main@6507c2c` + this atom. Phase 1's bus-gated atoms (1.3, 1.5, 1.8) wait on the
v1.2 batch; phase 2 starts next.
