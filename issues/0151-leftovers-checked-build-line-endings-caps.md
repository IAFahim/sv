# Issue 0151 - Small leftovers

Status: open

Labels: `ai`, `area:core`, `open`.

1. **SV_CHECKED test (also in 0134):** `BindingGenerationTests.ExternalBind_Invalidate_Recalculate_CheckedThrows_ReleaseParks`
   fails under `-p:DefineConstants=SV_CHECKED` on main `ac0e065`: expects a throw, the binding parks. Owner decision: the
   checked contract is "park" (edit the test, needs approval) or "throw" (fix src).
2. **SHA256SUMS and line endings (fixed 2026-11-12; 2026-11-13: `eng/engine-sync.sh` pins `LC_ALL=C` for the sort order and `.gitattributes` pins `packs/**` to LF because `Rules.map.txt` offsets depended on CRLF):** `.gitattributes` pins LF for `src/Sv`, `packages/com.sv.core` and `eng/*.sh`; sums regenerated from LF files; `eng/unity-sync.sh check "$PWD/packages/com.sv.core"` passes. Needs the absolute path.
3. **Schema-level event cap:** the cap is per host at build (default 16,384, max 1<<24). A schema-level cap changes
   `Schema.ContentHash` and so the pinned digests; not done.
4. **Debug digest every tick (moot since M11, 2026-11-13: `SvHost` is deleted; the v3 `HostOptions.DigestEvery` defaults to 1):** `DigestCadence.Default` and `Install(..., digestEvery)` exist; the default in a Unity build is
   still 8 unless the project passes 1. Decide whether a development player defaults to 1.
5. **Hot target lanes (moot since M11: `links.Send` is deleted; v3 records replace the inbox):** `InboxLanes` is opt-in as an object; the send pass in Unity (`links.Send`) uses atomic adds. Measure
   10 percent to one slot through the Unity send pass and decide the default.
6. **Commit trailer:** phase 2 agents signed `Claude Opus 5.5`; BRIEF asked `Claude Sonnet 5.5`. Cosmetic; local history.
7. **Cleanup:** phase-1 and phase-2 worktrees and `proto/*`, `proto2/*` branches under `D:\GitHub\_work\sv-proto`.
