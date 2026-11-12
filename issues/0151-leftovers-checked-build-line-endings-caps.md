# Issue 0151 - Small leftovers

Labels: `ai`, `area:core`, `open`.

1. **SV_CHECKED test (also in 0134):** `BindingGenerationTests.ExternalBind_Invalidate_Recalculate_CheckedThrows_ReleaseParks`
   fails under `-p:DefineConstants=SV_CHECKED` on main `ac0e065`: expects a throw, the binding parks. Owner decision: the
   checked contract is "park" (edit the test, needs approval) or "throw" (fix src).
2. **SHA256SUMS and line endings:** `packages/com.sv.core/SHA256SUMS` matches a Windows CRLF checkout; a Linux checkout fails
   `eng/unity-sync.sh check`. Fix: `.gitattributes` for `packages/com.sv.core` with `eol=lf` or `-text`, regenerate sums.
3. **Schema-level event cap:** the cap is per host at build (default 16,384, max 1<<24). A schema-level cap changes
   `Schema.ContentHash` and so the pinned digests; not done.
4. **Debug digest every tick:** `DigestCadence.Default` and `Install(..., digestEvery)` exist; the default in a Unity build is
   still 8 unless the project passes 1. Decide whether a development player defaults to 1.
5. **Hot target lanes:** `InboxLanes` is opt-in as an object; the send pass in Unity (`links.Send`) uses atomic adds. Measure
   10 percent to one slot through the Unity send pass and decide the default.
6. **Commit trailer:** phase 2 agents signed `Claude Opus 5.5`; BRIEF asked `Claude Sonnet 5.5`. Cosmetic; local history.
7. **Cleanup:** phase-1 and phase-2 worktrees and `proto/*`, `proto2/*` branches under `D:\GitHub\_work\sv-proto`.
