# Issue 0123 - V4: NorthStar2, the frame of plan 2.4 on .NET against tl 4.0

Status: closed 2026-11-12. Verified by an independent fresh clone of proto2/int at 4fcd3e3 (logs verify2-out). Remaining items live in 0149, 0150, 0151.

Agent D, branch `proto/d-frame`. Plan step V4 (clauses 7, 13, 17, 19, 20). Labels: `ai`, `area:test`.

## Consuming tl (decision, smallest choice)

tl 4.0 is consumed from source as a **test-only ProjectReference** (Tl.Core, Tl.Gen.Tlb for in-test
baking, Tl.Gen.CSharp as analyzer + its build targets), exactly like tl's own pack tests. No NuGet: the
cached `Tl.CSharp 4.0.0-preview.1` is a meta-package whose `Tl.Runtime` is not in the offline cache and
it would not carry ca1c3309. `TlRoot` = `-p:TlRoot`, else env `TL_ROOT`, else `../../../tl` next to the
worktree (`D:/GitHub/_work/sv-proto/tl`, a detached tl worktree at origin/main **ca1c3309**). Without tl
the project compiles no sources (0 tests), so `sv.slnx` still builds. The old `Sv.NorthStar` (tl 1.3.0)
is untouched. `-p:SvAsset=netstandard2.1` runs the suite on the netstandard2.1 sv asset.

## Frame (tests/Sv.NorthStar2/FrameHost.cs), 1,000 rows, public sv API only

1 commands (journal: timeline starts, StatOps, inbox sends, local delta contributions) -> 2 expiry
(`ExpireModifiers`) -> 3 `StatOps.Canonicalize` + `Board.Apply` (plus pulse StatOps of t-1) -> 4
`Recalculate` -> 5 sink scatter (`BindSink(stat, true)`, `TakeBroadcast` -> host float columns + int
ceiling via `Clamp.BoundOf`) -> 6 `Kernel.Fold` per worker partition with inbox and changed words, 30/40 %
hysteresis condition bits, edge counts summed -> 7 tl `world.Tick` + generated `TlAuto_Sv_NorthStar2.Run`
(TlWorkers when W > 1): kinds Damage (D, sum), Strike (pulse -> Outgoing effect), ArmorEdge (M pulse) ->
send pass (Outgoing -> `Inbox.Send` to `target[row]`, consumer clears) -> 8 `EventDerive` (edge bits ->
LowHp events, cap 65,536) and pulses -> StatOps for t+1 -> 9 `EventStreams.Swap`, digest (slot-keyed
`CellDigest` sums over hp, delta, both host stats, low bit, inbox, outgoing; event bytes; `Board.Digest`;
pending StatOps).

## Tests (9, new)

live == replay over 3 ticks and the baked pins; semantics (heal +10 / hit -20 on 95/100 -> 85; floor
clamp; expiry at tick 2; M pulse -> armor 15 next tick; remote write: row 141's strike lands on row 0
via the inbox at tick 2: 85 - 1 poison - 7 strike = 77); worker sweep 2, 4, 7, 11 =
1; 100 shuffles (journal order, fold partition order, send-pass row order) = reference; fold scenario
and link table pins; the sv asset assertion per leg.

## Baked pins (.NET 10, default ISA)

- NorthStar2 per tick: `939FBDEE2E7DEE50`, `E5CE5FA31824A22E`, `75035C43BBDC14AF`
- fold scenario: `0D945D8FD131DA2A` (4,099 rows, 8 ticks, rows bounds incl. crossed, inbox)
- link table hash: `A47434070C42E4AC`

## Receipts

`dotnet test tests/Sv.NorthStar2 -c Release`: 9/9; with `-p:SvAsset=netstandard2.1`: 9/9 (asset
`.NETStandard,Version=v2.1` asserted).

## Limits found

- No public `Bounds` over a host float stat column: the sink host keeps an int ceiling column
  (`Clamp.BoundOf` at scatter). A `Bounds.HostStats(float*, float*)` factory would remove it.
- tl kinds cannot address another row; remote writes go kind -> row-local int effect -> sv send pass.
