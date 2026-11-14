# 0503 red team: FrameLoop.Send from several game threads corrupts native memory, even with ExternalWriters > 1

Status: closed (2026-11-14, fixed by the lead; see Fix)

Severity: high (process crash or lost records on plausible game code)
Kind: design flaw (API footgun)

Found by: red team (opus), branch `redteam/opus`, base sv `7f6bdb4`

## Promise broken
README chapter 2: "the order your code sent records in never matters. That is what makes it deterministic across threads and machines". No crash or corruption on valid input.

## Smallest input
Reference pack, `HostOptions { Workers = 1, EntityCapacity = 256, ExternalWriters = 4 }`, one `player`. `Parallel.For(0, 4)`, each thread calls `host.Send(hero, -1, 0, GiveCharges, 1)` 25,000 times, then `Step()`.

Test: `tests/Sv.Host.Tests/RedTeam/RedTeamHostMisuseTests.cs`, `Sends_from_several_game_threads_with_as_many_external_writers_all_arrive(4, true)`. It kills the test host, so it runs only with `SV_REDTEAM_CRASH=1`. The same test with `host.Sender(thread)` per thread (`(4, false)`) passes: 100,000 of 100,000.

## What happens
Command: `SV_REDTEAM_CRASH=1 dotnet test tests/Sv.Host.Tests -c Release --filter "DisplayName~threads: 4, throughFrameLoopSend: True"`

Expected: `ImmuneCharges` 100,000.
Actual, 4 runs: 3 x `System.AccessViolationException ... at Sv.SourceChunks.TemplateValues ... at FrameLoop.Step()`, test host crashed; 1 x `ImmuneCharges 49671 of 100000`.

`FrameLoop.Send` always writes through `raw.Writer(0)`; `EffectWriter.Send` is not thread-safe (by reading: two threads can pass the `Fill == BlockRecords` check together and `Fill++` runs past the 256-record block end). `ExternalWriters` and `Sender(i)` are the safe way, but neither README chapter 5 nor `docs/PRODUCTION-CHECKLIST.md` names them, and `ExternalWriters > 1` does not change what `Send` does.

## Reproducible
Default mode, 4 of 4 runs wrong (3 crashes, 1 lost half; the 4th run with the gated command above: "Test host process crashed"). Not repeated in other modes (the test is gated).

## Fix direction
Document `ExternalWriters` + `Sender(i)` as the only multi-threaded send path, and make `FrameLoop.Send` refuse a second thread (owner-thread check) or pick a per-thread writer; a checked build could assert single-writer use.

## Fix (lead)

`FrameLoop.Send` claims the frame's sending thread with a compare-exchange and throws `sv: FrameLoop.Send was called from two threads in one frame; give each game thread its own writer (HostOptions.ExternalWriters and Sender(i))` for a second thread; the claim resets every `Step`. The red-team case that crashed the test host now runs ungated and is deterministic (second thread in the same frame throws, in the next frame works). README chapter 5 and the checklist name `ExternalWriters` + `Sender(i)` as the multi-threaded path.

Receipts: `bash eng/v3-verify.sh` -> `v3-verify: all green` (Laws 573, Scenario 80, Diff 48, Lego 120, Host 63 under default, AVX2 off and SIMD off, plus SV_CHECKED; Twin 32, NorthStar3 21, Web 6; Hello sample). Host tests 3 of 3 runs green under `taskset -c 0,1`. Unity 7000.0.0a16 gate with com.tl.unity: 98/98, 0 error CS, 0 warning CS, 0 error BC. Bench digests unchanged: B 6E7618BF0900D3F8, r 9CFD52083A9DA3FB, 0 B allocated.
