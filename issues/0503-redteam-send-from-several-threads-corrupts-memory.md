# 0503 red team: FrameLoop.Send from several game threads corrupts native memory, even with ExternalWriters > 1

Status: open (found by red team, not fixed)

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
