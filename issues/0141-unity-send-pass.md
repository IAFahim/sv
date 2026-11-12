# Issue 0141 - U1: cross-row send pass in the adapter

`SvLinkRegistry.Send<TEffect, TTarget>(intrinsic)` registers a send pass (max 4) over two 4-byte int
columns: a row-local effect amount and a target slot. `SvEndSystem` runs it first, after tl's systems:
`SvSendJob` (Burst, parallel `IJobChunk`, skipped on chunks whose effect column did not change) sends
every nonzero amount through the intrinsic's inbox (`Interlocked.Add`, integers only), zeroes the
effect, ignores targets outside `[0, Capacity)`. The fold of the next tick consumes the inbox.
Throws at registration when the intrinsic is not `remote: true`.

Tests (Unity EditMode, Burst, safety on):
- `TheSendPassMovesEffectColumnsThroughTheInboxDeterministicallyAtOneAndElevenWorkers`: 700 rows x 30 frames,
  25 % of rows send per frame (random target, 8/708 out of range), every hp equals the scalar law with a one-frame
  lag, the effect column reads 0 after every frame, digest 3C39EA1C64549841 at 1 and 11 workers, `SvProbe` managed 0.
- `ASendPassNeedsARemoteIntrinsic`.
- The .NET twin match is in issue 0142 (NorthStar2 through the adapter: Strike -> Outgoing -> send pass).
