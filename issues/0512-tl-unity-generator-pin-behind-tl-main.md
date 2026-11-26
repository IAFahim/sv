# 0512 Vendored tl Unity generator is pinned far behind tl main

Status: fixed (2026-11-26); the Unity Tl leg still has to run once on the owner's self-hosted runner with `TL_UNITY_ROOT` set

Found by: the v4 design survey of tl (`docs/v4-design.md` section 5), while reading how tl reaches millions of timelines.

## Promise at risk
The tl glue (M9) promises that `packages/com.sv.unity.v4/Tests/Tl` builds against tl's plain host inside Unity when `com.tl.unity` is in the project (`docs/UNITY.md`). That leg compiles only against the vendored generator, not against current tl.

## What was seen
- The vendored `Tl.Gen.Plain.dll` analyzer (`eng/tl-plain-sync.sh`) is pinned at tl `a15ea149`, about 136 commits behind tl main at the time of the survey.
- `packages/com.sv.unity.v4/Tests/Tl/Glue/NorthStar3Timelines.cs:46` calls `TlSystems.TlModule.Install()`. Current tl main no longer generates `TlModule.Install`, so moving the pin forward breaks this file.

## What to do
1. Move the pin to a current tl commit with `eng/tl-plain-sync.sh`.
2. Replace the `TlModule.Install()` call with whatever current tl generates for module setup, and rerun the Unity Tl leg on the self-hosted runner.
3. Keep `tests/Sv.NorthStar3` green on .NET with `TL_ROOT` at the same tl commit.

Not related to v4 and not blocking it (`docs/v4-design.md` section 8, decision 7). Needs a Unity editor, so it waits for the owner's runner.

## Fix (2026-11-26)
- Pin: tl `d87d1998` for both legs. It is the newest tl commit with a published plain generator (tl.unity `4b23471` pins it, `com.tl.unity/Tools~/generators.stamp`), 14 commits behind tl main `b1c26b05`. `eng/tl-plain-sync.sh sync` against tl.unity `4b23471` copied `Tl.Gen.Plain.dll` and rewrote `eng/tl-plain.stamp`; `check` passes. `.github/workflows/determinism.yml` checks tl out at the same commit (was `3ba5575e`).
- Module setup: tl.unity `0dab9f0` says tl no longer emits a `TlModule_<id>` class, so the plain generator stops emitting `TlSystems.TlModule` and its own `PlainHost` installs nothing. `NorthStar3Timelines` now installs nothing either; the generated `Tl.Generated.TlAuto_Sv_NorthStar3` it runs is unchanged in shape.

## Receipts
- `tests/Sv.NorthStar3` on .NET: 21/21 at tl `3ba5575e` (old pin), `d87d1998` (new pin) and `b1c26b05` (main).
- The Unity glue assembly (`Tests/Tl/Glue/*.cs`) compiled outside Unity with the analyzer, against tl's `src/Tl.Core`, C# 9: old DLL + old file at `a15ea149` builds; new DLL + new file at `d87d1998` builds; new DLL + old file fails with `CS0234 'TlModule' does not exist in the namespace 'TlSystems'` at `NorthStar3Timelines.cs:46`, the failure this issue predicted.
- The same build, run as a console port of `TlTimelineTwinTests` on `FrameLoop`: all 5 twin journals at 1 and 11 workers match every frame's outbox cells, every frame digest and the final digest.

## Follow-up (2026-11-26): tl 06a1c710, no placeholder system
- tl.unity `8c65f3f` pins tl main `06a1c710` (tl.unity #57) and its plain generator now runs on any assembly with a timeline declaration (tl.unity #58, `6d613db`). `eng/tl-plain-sync.sh sync` from a clean `8c65f3f` checkout rewrote `Tl.Gen.Plain.dll` and the stamp (`tl.unity=8c65f3f...`); `determinism.yml` checks tl out at `06a1c710`.
- The empty `[TimelineSystem]` struct in `NorthStar3Timelines.cs` is gone.
- Receipts: `tests/Sv.NorthStar3` 21/21 at `06a1c710`. The Unity glue without the struct, built outside Unity with the new analyzer against tl `06a1c710`'s `src/Tl.Core`, plays all 5 twin journals at 1 and 11 workers to the same outbox cells and digests; the same file with the previous analyzer fails with `CS0234 'Generated' does not exist in the namespace 'Tl'`, so the struct was needed before and is not now.

