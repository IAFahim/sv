# 0534 Unity 7000.0.0a22: 14 lego twin replays diverge from the .NET host

Status: open (cause is outside sv's code as far as can be checked without Unity; needs a run on a machine with a22)

Found by: the `sv Unity` workflow on the self-hosted runner, first on 2026-11-26.

## What happens
The adapter's EditMode suite runs 140 tests. On Unity 7000.0.0a22, 29 fail: `TwinReplayTests.Replay` for lego scenarios at 1 and 11 job workers (seen in the log: bricks 31, 32, 34, 36 and main 01, 05, 08; the log cuts off at 150 lines) and `Every_phase_runs_as_a_Burst_job_somewhere_in_the_scenarios`, each with "frame N: digest differs from the .NET host", N being 0, 1 or 2. Every other test passes, and the v4 columns Unity job (`sv Unity columns`, pinned canon digest) passes on a22.

## Why it is the editor, not sv
- The same sv commit, d929d03, passed 140 of 140 on 7000.0.0a21 (run 45780033938, 2026-11-26 10:13) and failed 29 on a22 (run 45853185255, 15:54). 507b43b fails the same 29 on a22 (run 46007872934).
- The Unity million-entity bench is deterministic (fixed seed, 32 frames) and its digests changed with the editor alone: 1M x 1 `828B215542B1D0DC` on a21, `4D3F7847A8C24121` on a22.
- The .NET side agrees with the journals on every leg (default, AVX2 off, scalar, checked) and on the six determinism hosts.
- Ruled out on .NET: reads of uninitialized native memory (every `Memory.Alloc` filled with 0xA5: Laws, Scenario, Lego, Host and Twin.Fixtures all green) and stack locals without zeroing (`SkipLocalsInit` on Sv.Core, Sv.Host and the lego kernels: Lego, Twin.Fixtures and Host green).

## The supported editor is not the CI editor
README chapter 6, `docs/UNITY.md` and tl.unity name exactly 7000.0.0a16 as the supported editor, but `eng/unity-ci.sh` runs the newest editor installed on the runner (a21, then a22). So CI has not tested the editor users are told to install since the runner upgraded. The owner should pick one: point the job at a16 (`UNITY=<path to 7000.0.0a16>` in `.github/workflows/unity.yml`, if a16 is still installed on sv-unity-llm), or move the documented editor to a22 once this divergence is fixed.

## Next step (needs Unity 7000.0.0a22)
1. Run one failing journal with Burst compilation off for the job types (Jobs > Burst > Enable Compilation off, or `BurstCompiler.Options.EnableBurstCompilation = false` in a test setup). Green means a Burst code generation change; red means the managed CoreCLR path or the engine mirror.
2. If Burst: compare the phase digests frame by frame (the twin journal's per-frame digest and `FrameLoop.Marks` probes) to find the first phase that differs, then the job.
3. File the Burst or editor bug upstream with the smallest repro, and add a regression test here.

## Receipts
`EditMode exit=2 total=140 passed=111 failed=29`, Unity 7000.0.0a22, Burst 2.0.0, Entities 7.0.0, runner sv-unity-llm.
