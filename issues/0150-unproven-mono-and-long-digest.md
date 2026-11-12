# Issue 0150 - Not proven: Mono float bits, IL2CPP, long digest at 1M rows

Labels: `ai`, `area:unity`, `open`, `proof-gap`.

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
