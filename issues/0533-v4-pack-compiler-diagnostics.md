# 0533 v4: pack compiler hid problems, crashed on nulls and misread some names

Status: closed (2026-11-26, fixed in the pack checks change)

Found by: a review agent in the 2026-11-26 triage pass, each item confirmed by a test. It also ran 1,500 random packs at and past the limits against the reference and found no wrong result; these are diagnostics and API edges.

## What was wrong
1. `Check` threw as soon as a name problem was found, so the rule problems after it were never listed (spec section 1: "listing every problem").
2. Null lists or names crashed: `FlowRule(add: null)` and `AbsorbRule(channels: null)` threw `NullReferenceException`, `Cond.Event(null)` threw `ArgumentNullException`, a null rule was skipped silently, and `ScaleRule(channels: null)` compiled as "every channel" while the reference crashed on it.
3. A card-op problem was reported once per template and named the template, not the card.
4. Repeated atoms (`A > 0 and A > 0`) were not removed, so that condition did not share an entry with `A > 0` (spec section 1.2 says atoms are deduplicated).
5. `CompiledPack.CardLane` read the caller's mutable `ColumnPack` on every call, so inserting a card after compile changed its answer.
6. `Bound.Of((string)null)` silently meant a constant bound of 0.
7. `CondText` could not read lanes named `not`, `event`, `entered` or `bit` (the compiler accepts those names), and did not take `+5`.

## Fix
Problems are collected through both phases (deduplicated) and thrown together; a crash caused by an earlier name problem turns into that problem list. Null entries and null lists are problems. Card ops carry their card in the message. `CondOf` drops repeated atoms after sorting. `CardLane` is computed at compile. `Bound.Of(null)` throws `ArgumentNullException`. `CondText` treats a keyword followed by a comparison as a lane. Tests: `tests/Sv.Columns.Tests/PackCheckTests.cs` (11, all fail before the fix).

## Receipt
`dotnet test tests/Sv.Columns.Tests -c Release` 115 of 115 green; canon digest unchanged. Left as is: `CompiledPack.Source` is still the caller's object (documented use is building a `ReferenceWorld` from it); `PackTables` stays public for the Unity jobs.
