# 0310 compiled host does not spend an absorb budget when every hit of a target is absorbed

Status: closed

Found by T2 (`tests/Sv.Diff.Tests`, branch v3/t2-diff). Compiled kernels differ from the reference interpreter.

Receipt (minimal, `HandwrittenTests.Absorb_spends_its_budget_when_it_absorbs_every_hit`):
- values: `Health` intrinsic default 100, `Budget` intrinsic default 2, channel `Hit`.
- rules: flow `Health -= Hit`; gate priority 1, no condition, `absorb {budget: Budget, per: Hit}`.
- scenario: one entity of template `base`; frame 0 sends `Hit` amount 10, hits 1.
- expected (reference): frame 0 Budget 1, Health 100, digest C5626E9F306CC260.
- got (compiled, 1 worker, shared): Budget 2, Health 100, digest C9D4F34A258FD60A.
- Same pack with hits 3 (one hit taken, so a channel total is non-zero) agrees on both hosts: Budget 0, Health 90.

Random packs: 35 of 300 seeds of the main property fail on Budget only (seeds 2, 4, 6, 15, 27, 38, 54, 60, 62, 65, 67, 85,
88, 90, 91, 102, 110, 111, 117, 154, 160, 182, 189, 218, 222, 223, 236, 244, 245, 252, 253, 260, 266, 269, 281); the same
generator with absorb switched off passes 300 of 300. Receipts are written to `tests/Sv.Diff.Tests/bin/Release/net10.0/receipts/`.

Suspected cause: `src/Sv.Host/Frame.cs:160`, `rows = touched == 0ul ? 0ul : LaneScan.NonZero(...) & touched;`. `touched` is
the Decide result (rows with a non-zero channel total). Issue 0190 item 3 says the host must fold `NonZero(delta)` without
`& touched`, because an absorb spends its budget through the delta while every total is zero.
Fixed 2026-11-13 (manager): absorb budget now folds via NonZero(delta) for chunks that hold records (Frame.cs Commit).
