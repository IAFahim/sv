# 0530 v4: SendFrom from a source that is not alive dropped every scale rule

Status: closed (2026-11-26, fixed: a source that is not alive sends as no source)

Found by: the 2026-11-26 triage and review pass (`tests/Sv.Columns.Tests/SourceAndFrameTests.cs`).

## What was wrong
`SendFrom(source, ...)` packed the source gate's bits 8 to 15 as the record's source flags. A despawned or never-spawned slot, or one outside the world (`-1`), has an empty gate, so its flags were 0. A scale rule without a source condition (`"scale": { "times": 2, "when": "Wet > 0", "sourceWhen": null }`) holds for every source, and `Send` sets its bit through the pack's no-source flags, but a `SendFrom` from a source that had died got none: a wet target took 10 from `Send` and from a live source, and 5 from a dead one. The reference interpreter did the same, so the lockstep tests agreed with it.

## Fix
A source that is not alive reads as no source: kind 0 and the pack's no-source flags, exactly as `Send` (`ColumnWorld.SourceGateOf`, `ReferenceWorld.SendFrom`). Spec section 2 says so. A live source is unchanged. Tests: `SourceAndFrameTests.ASourceThatIsNotAliveSendsAsNoSource`, `ALiveSourceStillCarriesItsOwnSourceCondition` (both against the reference).

## Receipt
`dotnet test tests/Sv.Columns.Tests -c Release` green under default, `DOTNET_EnableAVX2=0` and `DOTNET_EnableHWIntrinsic=0`; canon digest unchanged (`CanonTests`).
