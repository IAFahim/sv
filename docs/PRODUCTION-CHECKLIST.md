# Production test checklist (sv v3, with or without tl)

For a first real-project test of sv v3 on .NET 10 or Unity 7. The previous engine (sv 2.1) was removed from this repository on 2026-11-14; it lives in git history and the tag `v3.0.0-rc.2`.

## What you are testing

| Piece | Version | Where |
|---|---|---|
| Engine (`Sv.Core`, `Sv.Host`, `Sv.Rules`) | 3.0.0 | `src/`, Unity copy `packages/com.sv.engine` |
| Unity adapter (Burst jobs, `SvWorld`, ECS systems, authoring window) | 4.0.0 | `packages/com.sv.unity.v4` |
| Contract | v3.0.0 + v3.0.1 (integer stats) + v3.1.0 (rule bricks) + v3.2.0 (stat layers, derived stats) | `contract/` |
| Editors and runtimes | .NET 10; Unity 7000.0.0a16, Entities 7.0.0, Collections 7.0.0, Burst 2.0.0 | |
| tl | tl main, tl.unity `com.tl.unity` (plain host) | `tests/Sv.NorthStar3`, `packages/com.sv.unity.v4/Tests/Tl` |


## Before the first frame

1. **Write the pack.** Four files: `values.sv.json`, `rules.sv.json`, `cards.sv.json`, `templates.sv.json`. Start from `packs/reference` (the worked example) or `packs/lego` (22 mechanics). In Unity use Window > sv > Pack Authoring.
2. **Generate the kernels.**
   - .NET: import `build/Sv.Pack.targets` in the game project and set `SvPackFolder` and `SvPackNamespace`; the build runs `sv pack`. It regenerates when a pack file or any dll of the pack tool changes.
   - Unity: `sv pack <folder> --host unity --namespace <ns> --out <dir>` or the window's Regenerate.
   - With tl: add `--glue tl` for `Glue.g.cs`: `[TlState]` outbox columns, `[TlKind]` feeds, clips, tracks and `TimelineRequests`.
3. **Pin `PackHash`** (`Layout.PackHash`) in your own test next to your game's state hash. The host throws `sv: the compiled kernels were built against another pack` if the kernels and pack differ.
4. **Create the host.** .NET: `new FrameLoop<HostKernels>(HostPack.From(plan), new HostOptions { ... })`. Unity: `PackWorld.Create(new HostOptions { ... })`, then `SvEntities.Publish` and `SvEntities.Attach` for ECS. Options that matter:

| Option | Default | Set it to |
|---|---|---|
| `EntityCapacity` | 65,536 | live entities plus margin (`sv: the world is full` past it) |
| `Workers` | 1 | physical cores plus SMT (measured best: 11 on a 6C/12T Ryzen 5 8500G; fewer is slower) |
| `Parts` | 64 | 64 (16 and 22 were measured worse for random load) |
| `DigestEvery` | 1 | 1 while testing; 0 turns the digest off |
| `RingEpochs`, `FramesPerEpoch` | 32, 2 | the rewind window you need (32 x 2 frames at 30 Hz = about 2 s) |
| `Arena` | `Grow` | `Grow` on servers and editor; a hard cap in the browser (decision D10) |
| `EventCapacity` | 16,384 | outward events per channel per frame; above it the channel is dense |
| `Late` | `Clamp` | `Reject` if late inputs must never be clamped |

## Every frame

The tested order (`tests/Sv.NorthStar3/Rig.cs`, the same in Unity):

1. tl: `world.Tick(...)`, then the generated `TlAuto_<Assembly>.Run(...)` writes the `[TlState]` outbox columns.
2. Collect: `Outbox.Send(host, cells, channel, kindLane, targetLane)` per timeline channel. Each non-zero cell becomes one `Origin.Timeline` record from the status entity to its aimed target (target lane value minus 1, or itself when 0), and the cell is zeroed.
3. Your own input: `Send(target, source, kind, channel, amount, hits, origin)`, `Modify(StatOp)`, `Spawn(template)`, `Kill(slot)`. Spawn, kill and rewind happen between frames only.
4. `Step()`.
5. Read results: `Read(slot, lane)`, `Level(slot, raised)`, `Events`/`EventCount` (`HostEvent`: event, slot, entered), then `TimelineRequests.Apply(players, Events, EventCount)` to start and stop tl timelines on event edges.
6. Log `(Frame, LastDigest)`.
7. Late inputs: `Late(frame, records, count)` and `Resolve()` right after `Step`, before the next frame's input.

Rules that keep it deterministic:
- Everything that changes state is an integer record, a `StatOp`, a spawn or a kill. Glue code does no float maths on state.
- Zero every column you hand to sv or tl. Seed every random source and never use wall-clock time in the glue.
- Never iterate a `Dictionary` or `HashSet` to produce sends.
- Origins: `Direct` for game input (it is logged and replayed); `Timeline` for tl (via `Outbox`); generated edge emits are Triggered or Reflected.

## Verify on every machine you compare

- `bash eng/v3-verify.sh` (set `TL_ROOT` to your tl checkout). It runs the engine mirror and tl generator checks, Laws, Scenario, Diff, Lego and Host under default, `DOTNET_EnableAVX2=0` and `DOTNET_EnableHWIntrinsic=0`, then Twin.Fixtures, NorthStar3 and Web, the checked build (`-p:SvChecked=true`) and the README chapter 1 sample. Expect `v3-verify: all green`.
- Unity: `eng/m8-run/gate.sh` (set-up in `docs/M8-HANDOFF.md`, tl in `docs/UNITY.md`): 0 `error CS`, 0 `warning CS`, 0 `error BC`, every EditMode test green (97 with com.tl.unity, 87 without). Every generated job reports probe 1 (ran as Burst).
- Unity player: `bash eng/player-smoke.sh` builds and runs a Linux player; expect `failures 0` and `managedRuns 0`.
- Pins that must not move: worked example `E0F445186E2C3672`; reference PackHash `BD70627B1C671888`; lego PackHash `9A043301EA06000E`; NorthStar3 `A566FAEC8837BA57` and `2D6A06776AE25DCB`; bench digests A `DAF3A1D9BD73BA10`, B `6E7618BF0900D3F8`, C `D3C714D46555B7CE`, D `7413F619472566C4`, r `9CFD52083A9DA3FB`.

## Expected numbers (measured, Ryzen 5 8500G 6C/12T, 11 workers, 1,000,000 entities, lego pack)

| What | Value | Source |
|---|---|---|
| A: 100k random records into 10 % of chunks | 2.4-2.8 ms per frame | BENCH-REPORT Update P3 |
| B: 1M timeline records, one per entity (chunk-bucketed) | 7.7-7.8 ms (gate 8.3 ms: pass) | same |
| r: 1M uniformly random cross-entity records | 17.6-18.1 ms; stress benchmark, ceiling 19.0 ms (decision D20) | issue 0374 |
| C, D (2M, 3M records) | about 25 and 35 ms; reported only | same |
| Memory at rest | 41-46 B per entity | BENCH-REPORT |
| Warm allocation | 0 B on sv's threads (main and workers) | issue 0371 |

tl's own kind jobs are not included: tl's kind job alone was 8.45 ms at 1M rows (issue 0149). Budget it on top.

## Reading a digest mismatch

Record a twin journal on the side you trust (`tests/Sv.NorthStar3/Rig.cs` `Journal`: spawn, kill, send, digest per frame) and replay it on the other (`packages/com.sv.unity.v4/Tests/Editor/TwinReplayTests.cs`). Find the first differing frame, then:

| Differs between | Likely cause |
|---|---|
| two runs, same host and workers | an unzeroed column, wall-clock or unseeded random in the glue, dictionary order in the glue |
| worker counts (1 against N) | glue that sends from several threads in a non-canonical order, or a store column written during the tl step |
| .NET and Unity | sends collected in a different order or frame (compare the journal's `send` lines), a Burst job that did not run as Burst (probe 2), stale generated kernels |
| live and replay | something changed state outside sends, StatOps, spawns and kills; a `Direct` record sent from an edge rule |
| only after an emitter change | stale generated kernels: compare `obj/SvPack/Kernels.g.cs` time with the pack tool, delete `obj/SvPack/.svpack.key` and rebuild |

## Failure messages

| Message | Meaning |
|---|---|
| `sv: the compiled kernels were built against another pack` | regenerate the kernels for this pack |
| `sv: the world is full` | raise `EntityCapacity` |
| `sv: spawn, kill and rewind happen between frames, after every writer is absorbed` | moved into a frame: call them before `Step` and outside writer passes |
| `sv: resolve late inputs right after Step, ...` | call `Late`/`Resolve` before the next frame's input |
| `sv: templates are added in pack order` | the host was handed templates out of pack order |
| `sv: a stat scale is a power of ten from 1 to 1,000,000` and other `ArgumentException`s at load | the pack's values do not satisfy the contract; the pack reader names the row |

## Known limits (accepted, decision D21)

- A same-frame lethal check is not available: a revive acts one frame late (Lego 13).
- Masks and exclusive groups use 64 bits (Lego 7).
- Uniformly random cross-entity load runs at about 18 ms per 1M records (D20). Games whose records come from timelines and Collect are inside the gate.
- Unity: proven in the editor (EditMode, Burst, 1 and 11 workers, fresh clone and fresh project) and in a built Linux CoreCLR player with AOT Burst (`eng/player-smoke.sh`, 37 journals at 1 and 11 workers). Not covered: Windows, macOS, mobile and console players, IL2CPP (not shipped by this editor). tl in Unity runs on tl's plain host; the ECS/SubScene timeline path is not built.
- Unity 7000.0.0a16 is an alpha editor (CoreCLR only, C# 9).
