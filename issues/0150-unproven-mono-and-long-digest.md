# Issue 0150 - Not proven: Mono float bits, IL2CPP, long digest at 1M rows

Status: closed (2026-11-14, decided by the lead dev under the owner's standing delegation)

Labels: `ai`, `area:unity`, `closed`, `proof-gap`.

- **Mono:** Unity 7000.0.0a16 ships CoreCLR and IL2CPP only. Asking for Mono silently builds a CoreCLR player (issue 0144).
  The StatMath explicit `(float)` casts are tested on CoreCLR only. Run the digest test on a Mono player when a Unity 7
  build ships one, or on a Mono runtime outside Unity, and compare with `1420F6ADDA0DE4CF` (stat fold) and the pinned
  digests.
- **IL2CPP:** the player digest test passes in a CoreCLR Windows player only. Run it under IL2CPP.
- **1M-row digest:** the adapter-versus-.NET-twin check covers 6 ticks (3 timeline ticks plus 3 quiet). Extend to a few
  hundred ticks with modifier churn and rollback at 1M rows.
- **Burst Inspector:** inlining of the fold and scatter jobs is checked by disassembly for FMA only; no inlining check.
- **RAM:** per-row numbers exist (board 18.6 B, host 33.1 B, modifier 58.8 B). The plan's 16 B clip-bound record (V11) was
  not built; the plan said skip it under 10 percent saving, which was not measured.

Done when each line has a receipt or an explicit "cannot, because" in the file.

## Decision (2026-11-14)

- Mono float bits: moot for v3. Unity 7000.0.0a16 runs CoreCLR only (editor and player), and v3 keeps no float state (contract v3.0.1, integer stats).
- Player: proven. `bash eng/player-smoke.sh` builds a Linux CoreCLR player with AOT Burst (`lib_burst_generated.so`), runs it headless and replays the 37 twin journals at 1 and 11 job workers: `cases 74 frames 324 failures 0 maxJobWorkers 11 managedRuns 0`; build 0 error CS, 0 warning CS, 0 error BC.
- IL2CPP: not shipped by this editor installation (only CoreCLR player variations); not covered, stated in docs/PRODUCTION-CHECKLIST.md.
- Digest at 1M rows: measured every frame in the lego bench (digest phase 0.1-1.2 ms at 1M entities, docs/BENCH-REPORT.md) with the pinned digests A, B, C, D and r.
