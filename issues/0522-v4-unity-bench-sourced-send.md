# 0522 v4: the Unity bench still made the four-int sourced `Send` call

Status: closed (2026-11-26, fixed with the Entities and Burst layer)

Found by: reading `eng/unity-columns/SvColumnsBench.cs` while adding the Burst arms.

## What was wrong
Issue 0513 replaced `Send(target, source, channel, amount)` with `SendFrom(source, target, channel, amount)` and fixed six call sites, but the Unity bench is a seventh. Its sourced quarter, `writer.Send(target, slots[...], channel, amount)`, binds to `Send(target, channel, amount, hits)`: the source slot becomes the channel and is refused for any slot past 15, and the amount becomes the hit count. The Unity numbers since that change measured fewer records than the .NET bench, and the scalar twin agreed only because both sides made the same mistake.

## Fix
`writer.SendFrom(slots[...], target, channel, amount)`, as in `benchmarks/Sv.Columns.Bench/Program.cs`.

## Receipt
The Unity CI run on the branch that adds `com.sv.columns.entities` reports the records shapes with the scalar twin equal.
