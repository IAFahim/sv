# 0500 red team report: coverage index

Status: open (index of red-team findings 0501-0510; close when they are closed)

Found by: red team (opus), branch `redteam/opus`, base sv `7f6bdb4`. Tests: `tests/Sv.Host.Tests/RedTeam/*.cs`, `tests/Sv.Diff.Tests/RedTeam/*.cs` (finding tests assert the correct behaviour and fail today; survived attacks stay as guards).

Run: `dotnet test tests/Sv.Host.Tests -c Release --filter "FullyQualifiedName~RedTeam"` (32 tests: 13 fail, 19 pass) and the same for `tests/Sv.Diff.Tests` (22 tests: 5 fail, 17 pass). Identical results under default, `DOTNET_EnableAVX2=0`, `DOTNET_EnableHWIntrinsic=0` and `taskset -c 0,1`. The crashing case of 0503 runs only with `SV_REDTEAM_CRASH=1`.

## Findings

| # | Severity | Kind | One line |
|---|---|---|---|
| 0501 | critical | bug | Rollback by hand (`Rewind` + re-step) then `Late`/`Resolve` replays every spawn and stat op twice: MaxHealth 200 not 150, `Resolve` true |
| 0502 | critical | bug | Game sends past the raw arena capacity are silently dropped under `ArenaPolicy.Grow` (131,072 of 200,000 arrive, no counter after `Step`) |
| 0503 | high | design flaw | `FrameLoop.Send` from 4 threads: AccessViolation crash 3 of 4 runs, half the records lost 1 of 4; `Sender(i)` is safe but undocumented |
| 0504 | high | bug | Compiled Seal keeps records with an unknown channel or a source outside the world; the reference drops them (O1) |
| 0505 | critical | bug | Reference orders Add before RemoveSource of one source in one frame; contract clause 7 and compiled do the opposite: buff refresh 5 vs 9 |
| 0506 | critical | bug | `EntityCapacity` not a multiple of 64: slots past it live but lose every emit (README's own `EntityCapacity = 100_000`) |
| 0507 | high | design flaw | `HardCap` canonical overflow past merge level 3: compiled truncates, reference keeps; contract undefined |
| 0508 | medium | design flaw | `InputLogFrameLimit` cap: `Resolve` returns true and replays less than was played |
| 0509 | medium | bug | Template names `orc-boss`/`orc_boss` and the name `value__` are accepted and break the generated C# (CS0102, CS0076) |
| 0510 | low | design flaw | Calls after `Dispose` throw NullReferenceException or "unknown template" instead of ObjectDisposedException |

## Attacks tried

Numbers and stats
- Extreme storms, lego and reference packs, 24 storms x 5 host settings (workers 1/4/7/32/11, parts 64/16/3/1/1000, Shared and Dense): amounts int.MaxValue, int.MinValue, +-1e9, -1; hits up to int.MaxValue; Direct and Timeline origins; kills mid chain; late spawns; 40 modifiers per storm with values +-int.MaxValue, -20000 bp multi, caps. Survived, except same-frame remove + add of one source: 0505.
- 120 stacked multipliers on one stat, compiled vs reference, Dense, 4 workers: survived.
- Same-frame RemoveSource + Add of one source: 0505.
- Removing a source that is not there, two modifiers with one source id: survived (inside the storms).

Chains and arenas
- Thorns, spite, mirror, leech and lego chains under the storms (depth limit, kill mid chain): survived.
- `HardCap` raw arena (RawCapacity 65,536, 4,096, 1) with 300 and 1,000 thorns reflections vs reference: survived.
- `HardCap` canonical merge levels 1-3 (limits 1,000,000, 40, 12, 8) vs reference: survived; past level 3 (limit 3): 0507.
- External sends past `RawCapacity` under `Grow`, Shared and Dense: 0502.

Lifecycle
- Kill twice, kill a far and a negative slot, send to a dead slot, to slot 4,000,000 and to -7: survived.
- Spawn into a full world (capacity 64): clean exception, world unchanged: survived.
- `EntityCapacity` 100 (not a multiple of 64): 0506.

Time travel
- Rewind to an epoch then replay the same inputs: every digest equal: survived.
- Rewind outside the ring, into the future, to epoch 0: refused, nothing changed; rewind to the current epoch twice (ring 2, 1 frame per epoch): survived.
- Rollback by hand then late input whose replay starts after the commands (late frame 3): survived; starting at frame 0 or 1: 0501.
- Input log cap then late input: 0508.

Events
- LowHealth flapping every frame on 70 entities with `EventCapacity` 16,384, 3, 0 at 1, 4 and 7 workers: events, dense counts and digests equal to the reference: survived.

Host misuse
- `Send` from 4 threads through `Sender(i)` with `ExternalWriters = 4`: survived; through `FrameLoop.Send`: 0503.
- Dispose twice: survived; calls after Dispose: 0510.
- Workers 32 (more than cores) and Parts 1 and 1000: inside the storms, survived.
- Records with unknown channels (2, 64, 200) and sources outside the world (300, 4,000,000): 0504.

Determinism
- Send order (O7): the storms' sends shuffled per seed give the same compiled trace: survived (24 storms).
- CPU modes (O4) and contention (O5): every red-team test gives the same result in all four modes.

Packs at the limits
- C# keywords (`class`, `int`, `event`, `default`), contextual keywords (`var`, `record`, `nameof`, `dynamic`), names like generated types (`Layout`, `Kernels`, `Intrinsic`, `Channel`, templates `Layout`, `Values`), case-only differences: survived.
- Value, channel and digit-first names that collide after mangling (`a-b`/`a_b`, `fire.dmg`/`fire_dmg`, unicode `feu`/`f_u`, `1st`/`_1st`): rejected by the emitter (SV0007), survived (note in 0509: `PackCompiler.Compile` accepts them).
- Template names colliding after mangling, `value__` as template or value name: 0509.
- 256 channels and 256 kinds with a 256-channel flow and a gate on channel 255: compile clean; 257 channels rejected: survived.
- Derive cycle through three stats: `CyclicStat`: survived.
- Hostile numbers in values (2^31, -2^31-1, 1e400, -0, 1e-7, NaN, strings, 1e26, true, null, []) as default, stat default, scale and channel id: diagnostics, no exceptions: survived.
- 20,000-deep nested `and` condition: diagnostic, no stack overflow: survived.

## Not checked
Unity (lead's), tl glue and NorthStar3, Twin fixtures, `Sv.Web`; 4,194,303 entities and the 22-bit limit at full size (memory); stale handles after slot reuse past the ring (design question, no test); `DigestEvery > 1` with rewind; `LatePolicy.Reject`; warm allocation of the paths above; roll, spend and assign modes beyond what the lego storms exercise; `sv pack` CLI on the bad-name packs (tested through `PackEmitter` in memory).
