# 0524 v4: EdgeTests.SpawnAndDespawnChurnOnAFullChunkAllocatesNothing read 3576 B once on Windows

Status: open (2026-11-26)

Found by: `windows-latest / default` on PR #114, head dcf4ac3 (run 45743970273). The same test passed on `windows-latest` for the two heads before it (6d9ef52, 1698280), whose `src/Sv.Columns` is identical, and on every Linux and macOS leg of all three. It also passed on the #111 head a3725ad that added it.

## What we see
`Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before)` failed with 3576 after the 1000-iteration warm-up and a 100 000-iteration Despawn/Spawn loop on one 64-row chunk.

## What it is not
The code under test is the same on every leg and every head, and an allocation from `Spawn`/`Despawn` (the `openChunks` lists growing) would repeat on every run. So the bytes most likely come from the runtime on that run, such as tiering or on-stack replacement of the hot loop (unconfirmed).

## Ideas
- Run the warm-up loop as long as the measured one, or call the measured loop through a method that has already reached tier 1, so the measured window holds no tier-up.
- Measure the minimum of three windows.

The test belongs to the v4 hardening work (#111); its body is left untouched here.
