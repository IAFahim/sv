# Changelog

## v3.0.0 (2026-11-26)

The first stable release of sv v3. Nothing a game can see changed since rc.4, and every pinned digest is the same. Upgrade: change `#v3.0.0-rc.4` to `#v3.0.0` in both Unity package URLs (README chapter 6). Package versions stay 3.0.0 (engine `com.sv.engine`) and 4.0.0 (adapter `com.sv.unity.v4`). The contract is final from v3.0.0 to v3.2.0: the owner approved v3.2.0 (stat layers and derived stats) on 2026-11-26 (`issues/0390-stat-layers-v3.2.0-readings.md`).

### Preview: sv v4 columns
- `src/Sv.Columns` and the Unity package `com.sv.columns` (`4.0.0-preview.1`) ship in this tag as a preview. They stand beside v3 and change nothing in it: their own pack format, their own pinned digest, no contract yet, and their API may change before 4.0.0. A v3 game does not need them.
- What it is: one integer fold per entity per frame from compiled per-template ops and 8-byte records, bit-exact against a slow reference interpreter, 0 B allocated warm. 1,000,000 entities x 500 column ops take 22 ms on 4 cores (v3: about 15 s as records); in Unity 7 on jobs, 20.3 ms. Spec `docs/v4-spec.md`, design and numbers `docs/v4-design.md`.
- Unity: `"com.sv.columns": "https://github.com/IAFahim/sv.git?path=/packages/com.sv.columns#v3.0.0"`. Its CI job is `.github/workflows/unity-columns.yml` on the self-hosted runner.

### Fixed
- Unity: `SvStepSystem` frees the sv world handles of its ECS world when the system is destroyed, so disposing an ECS world no longer keeps its sv world alive (IAFahim/sv#107).
- Tests: `MalformedPackDiffTests` compiles each accepted mutation on its own thread with a 60 s hang limit, so one stuck compile can no longer stall CI.
- Docs: the repository is public; README chapter 6, `docs/API-BRIEF.md` and `docs/DEMO.md` no longer say it is private.

### Faster
- Seal: a chunk made of a few long sorted runs plus some unsorted records (the usual shape: a sender's sorted stream plus last frame's reaction records) is merged instead of fully re-sorted. 20k entities x 500 records per frame on 4 workers: Seal.Order 125 -> 52 ms, frame 295 -> 207 ms. `docs/BENCH-REPORT.md`, update "scale".

### Added
- `docs/DEMO.md` (a 10-minute demo script with real outputs) and `docs/API-BRIEF.md` (the full API and architecture, with the runnable `samples/Sv.ApiTour`).
- v4 columns rollback: `new ColumnWorld(..., history: H)` keeps the last H frames' start state and inputs; `Rewind(frame)`, `Inputs(frame)` (insert, replace or remove actions and records of a past frame) and `Resimulate()` (also `ColumnRunner.Resimulate()` and Unity's `ColumnStepper.Resimulate()`). The resimulated world equals a fresh run of the corrected inputs bit for bit (sends refused at send included in `Dropped`); keeping history changes no digest. A resimulate replays only the chunks an edit can reach (an edit that spawns or despawns replays everything): one late input over a 32-frame window at 1M entities takes 0.24 s instead of 2.1 s. Sample: `samples/Sv.Columns.Rollback`. `docs/v4-spec.md` section 9.
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
