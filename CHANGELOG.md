# Changelog

## Unreleased (after rc.4)

No behaviour or digest changes; every pinned digest is the same.

### Fixed
- Unity: `SvStepSystem` frees the sv world handles of its ECS world when the system is destroyed, so disposing an ECS world no longer keeps its sv world alive (IAFahim/sv#107).
- Docs: the repository is public; README chapter 6, `docs/API-BRIEF.md` and `docs/DEMO.md` no longer say it is private.

### Faster
- Seal: a chunk made of a few long sorted runs plus some unsorted records (the usual shape: a sender's sorted stream plus last frame's reaction records) is merged instead of fully re-sorted. 20k entities x 500 records per frame on 4 workers: Seal.Order 125 -> 52 ms, frame 295 -> 207 ms. `docs/BENCH-REPORT.md`, update "scale".

### Added
- v4 columns rollback: `new ColumnWorld(..., history: H)` keeps the last H frames' start state and inputs; `Rewind(frame)`, `Inputs(frame)` (insert, replace or remove actions and records of a past frame) and `Resimulate()` (also `ColumnRunner.Resimulate()` and Unity's `ColumnStepper.Resimulate()`). The resimulated world equals a fresh run of the corrected inputs bit for bit; keeping history changes no digest. `docs/v4-spec.md` section 9.
- `v3-scale` benchmark: millions of entities x records per entity x live modifiers per entity.
- Unity CI on a self-hosted runner (`.github/workflows/unity.yml`, `eng/unity-ci.sh`, `eng/unity-bench/SvUnityBench.cs`): adapter compile, EditMode, PlayMode and a Unity-side million-entity benchmark. The job waits until a runner labelled `self-hosted, unity` is registered; `docs/CI-RUNNER.md` says how.

## v3.0.0-rc.4 (2026-11-14)

Fixes everything a fuzzing campaign and a red team found after rc.3. Upgrade: change `#v3.0.0-rc.3` to `#v3.0.0-rc.4` in both Unity package URLs (README chapter 6). Package versions stay 3.0.0 (engine) and 4.0.0 (adapter).

### Behaviour you can see
- `FrameLoop.Send` is single-threaded per frame and throws if a second thread calls it; give each game thread its own writer with `HostOptions.ExternalWriters` and `Sender(i)`. `Send` throws `ArgumentOutOfRangeException` for a kind or channel above 255.
- `FrameLoop.Rewind` (a rollback the game starts) drops the logged input after the rewound frame; play those frames again yourself. `Resolve` keeps the log.
- `Resolve` throws when the input-log cap (`InputLogFrameLimit`, `InputLogFrameBytes`) dropped records the replay needs.
- With one external writer and `ArenaPolicy.Grow`, `Send` grows the raw arena instead of dropping records; `ExternalDropped` counts drops on threaded or hard-capped paths.
- Records on a channel the pack does not have, or from a source outside the world, are dropped in Seal and counted in `RouteFaults` (the reference always did this).
- Calls after `Dispose` throw `ObjectDisposedException`.
- Packs are rejected when they compile: names that collide as C# identifiers (templates included), names that become `value__`, negative rule, card or template ids, value ids out of range. `sv run`, the web designer and `sv pack` now agree.

### Fixed (engine and reference now agree)
- 0401: `per: Frame/Source` emits whose `and` names different `taken`/`offered` channel sets could be skipped.
- 0505: the reference applied a buff refresh (remove and add one source in a frame) in the wrong order.
- 0506: entities in the chunk-rounded part of `EntityCapacity` lost their emits.
- 0507, 0511: hard-capped arenas merged and cut overflowing records differently in the engine and the reference.
- 0501, 0502, 0503, 0504, 0508, 0509, 0510, 0402: see the issue files.

### Proof added
- Fuzz harnesses: send order, rewind and replay, malformed packs (with C# compile of accepted mutations), pack round trip; hard-cap and odd-capacity differential checks; buff refresh shapes in the generator.
- Red-team and lead guard tests (`tests/*/RedTeam`): slot reuse inside the ring, sparse digests with rewind, `LatePolicy.Reject`, threads, arena limits, pack name limits, extreme numbers.
- Unity: Play-mode proof that the auto-created `SvStepSystem` steps sv in the default world; `eng/player-smoke.sh` can target Windows and IL2CPP (not yet run, issue IAFahim/sv#99).

## v3.0.0-rc.3 (2026-11-14)

First v3-only production-test candidate: the sv 2.1 engine removed; CI determinism on six hosts.
