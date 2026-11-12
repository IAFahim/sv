# Issue 0170 - Records and Seal: four places where plan 4.4 / 13.2 does not compile or does not decide

Status: open

Labels: `ai`, `area:core`, `plan-discrepancy`.

Found while writing `src/Sv.Core/Effect.cs`, `Arena.cs`, `Seal.cs`, `Route.cs` (branch `v3/a2-records`).

1. **`IntrinsicLaw` cannot be linked alone.** It lives in `src/Sv/Fold.cs` next to `Fold.Word`, which needs `Clamp`
   (whose `Clamp.Stat(Board, ...)` pulls in `Board`) and `Inbox` (pulls `Column`). Linking `Fold.cs` into Sv.Core drags
   the old Board in. Sv.Core uses `EffectLaw.Saturate32`, the same body as `IntrinsicLaw.Saturate`, until `IntrinsicLaw`
   moves to its own file (needs an edit of `src/Sv`, which this wave may not do). Receipt: `Clamp.cs:82`, `Inbox.cs:28`.
2. **Overflow level 1 "amounts add, hits add, flagged `Merged`" (4.4).** Adding both breaks the conserved total
   (`(10 x 2) + (5 x 1)` is 25, `15 x 3` is 45), and the 16-byte record has no spare bit for a flag. Implemented:
   a merged group becomes `Amount = saturate32(sum Term)`, `Hits = 1`; the flag is `CanonicalArena.Overflow`
   (level). Totals are exact while a group's total stays inside `int`. Level 3 keeps the highest origin and depth of
   the group, so a merge never lowers a depth (termination still holds). If level 3 still does not fit, the tail is
   cut in canonical order and counted in `DroppedOverflow` (the plan says "until it fits" with no fourth rule).
3. **Raw arena full under a hard cap has no deterministic rule.** Which records do not fit depends on how workers
   took blocks. Implemented: capacity is counted logically (`records + intervals x writers x 256 > blocks x 256`),
   which depends only on record counts and the configured writer count, so the verdict is the same at any worker
   count. Grow policy grows and the caller reruns the layer; HardCap discards the whole layer and counts it.
   Sends from systems outside sv cannot be rerun; the host must size the raw arena for them.
4. **D2 24-byte layout.** The plan keeps it as an owner decision, not a compile switch; D2 chose 16 bytes. Not built.
   The plain-fields form `EffectFields` with its own `EffectLaw.Compare` is the oracle for the packed key.
