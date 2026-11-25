# 0512 Vendored tl Unity generator is pinned far behind tl main

Status: open (2026-11-25)

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
