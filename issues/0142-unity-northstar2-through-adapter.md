# Issue 0142 - U3 (digest half) + U1 twin: NorthStar2 through SvHost with tl's generated Burst kind jobs

`Tests/Tl/SvNorthStar2AdapterTests.cs` replaces the "real tl kinds only in a 30-tick hook test" gap.
The sv half is now the adapter itself, no managed glue: `SvBeginSystem` (scatter + Burst fold + condition
lane 3/10 enter, 4/10 stay), tl.unity's systems (`TlSceneRun.AddSystems`: kind tick + generated
Damage/Strike/ArmorEdge kind jobs writing the `[TlState]` columns `HpDelta`, `Outgoing`, `ArmorPulse`),
`SvEndSystem` (the issue-0141 send pass `Send<Outgoing, NsTarget>` moves Strike amounts to row
`(7r+13) mod R` through the inbox, derive, digest). The test then reads ECS columns and host state
(`InboxCell`, `EdgeWords`, `ConditionWords`) and computes D's NorthStar2 digest formula; ArmorPulse ->
StatOps is game code in the test (as in D's FrameHost).

.NET twin: new `tests/Sv.Unity.Twin` (references `tests/Sv.NorthStar2` `FrameHost`, compiles out without
tl) runs the same journal scaled to R rows for 6 ticks; ticks 0-2 at 1,000 rows equal the NorthStar2 pins.

| rows | workers | digests ticks 0-5 | where |
|---|---|---|---|
| 1,000 | .NET 1 and 11; Unity 1 and 11 | 939FBDEE2E7DEE50 E5CE5FA31824A22E 75035C43BBDC14AF E9C2A3B5AB8D2EC5 x3 | equal |
| 1,048,576 | .NET 1 and 11; Unity 1 and 11 | C02336ECA6D817FA 139FA001187F8423 08D2B1F0B05E6B55 2E7A61280D20C9F4 x3 | equal |

Ticks 3-5 repeat because the timelines are 3 ticks long (state is quiet after them).
Unity: `SvProbe` managed runs 0, tl `TimelineKindTick.ManagedRuns` 0. At 1M rows tick 0 scatters
2 x 1,048,576 stat writes through the 65,536-write buffer (issue 0140 chunking) and adds `SvSlotCleanup`
to rows instantiated from a template (cleanup components are not copied by `Instantiate`).

Receipts: `dotnet test tests/Sv.Unity.Twin` 2/2 (tl ca1c3309 at `_work/sv-proto/tl`); Unity 7000.0.0a16 EditMode
filter `SvNorthStar2AdapterTests` 4/4, compile 0/0/0, locks 16:16:53-16:17:28, log `p2-out/g2-*`.
Timing half of U3: issue 0143.
