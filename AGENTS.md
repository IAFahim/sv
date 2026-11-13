# sv repository rules (AGENTS.md)

Start here: read `unity-cli-field-notes` (`SKILL.md` and `references/layers-and-library-roles.md`) before touching code or managing agents. Preserve existing repo invariants strictly: never edit `contract/` files, existing test bodies, pinned digests, or `tests/Sv.Tests/StrictFloatOracle.cs`. Keep all C# code comment-free, zero-FMA (`grep`-enforced), deterministic in order, and 0 B allocated on warm runtime paths. Keep `Sv.Core`, `Sv.Rules`, and emitted kernels C# 9 compatible and warning-free on `netstandard2.1` and `net10.0` (`#if NET` for `Vector256` intrinsics only, always paired with a scalar law).

## What sv is

`sv` is the standalone, host-independent deterministic game-state engine in the three-library composition (`tl` owns time, `sv` owns state and rules, `grid-influence` owns space). Libraries never reference each other; hosts connect them through plain column data and generated glue.

## What it builds

- `src/Sv.Core` (`netstandard2.1;net10.0`, C# 9, `AllowUnsafeBlocks`): chunked lane storage (`World`, `Layout`, `Lanes`, `LaneMath`, `LanePool`, `ChunkImage`), 16-byte `Effect` records, `RawArena`, `CanonicalArena`, `Seal`, `Route`, integer `StatLaw`, `StatOp`, `StatMachine`, and time travel (`Journal`, `InputLog`, `Replay`).
- `src/Sv.Rules` (`netstandard2.1;net10.0`, C# 9): pack schema/JSON codec (`PackReader`, `PackWriter`, `PackPlan`), 10-pass rule compiler and C# kernel emitter (`PackEmitter`), and slow reference oracle (`Sv.Rules.Reference`).
- `src/Sv.Host` (`net10.0`): runtime frame loop (`FrameLoop`, `HostPack`, `Workers`, `Collect`, `Net` fold/scan front doors).
- `packages/com.sv.engine` and `packages/com.sv.unity.v4` (Unity 7): `eng/engine-sync.sh sync|check` copies Sv.Core, Sv.Host and Sv.Rules into the engine package; the adapter runs each frame phase as a generated Burst job and has an editor pack authoring window. Gate: `eng/m8-run/gate.sh` (see `docs/M8-HANDOFF.md`). The twin journals under `packages/com.sv.unity.v4/Tests/Editor/Twin` come from `tests/Sv.Twin.Fixtures` (`SV_UPDATE_TWIN=1` rewrites them); rerun it after any change to a pack or to `src`.
- tl glue (M9): `sv pack --glue tl` emits outbox columns, kinds, clips and tracks for `timeline` channels; `Sv.Host.Outbox.Send` turns outbox cells into `Origin.Timeline` records; `tests/Sv.NorthStar3` is the example and gate (status as an entity, target lane holds slot+1).
- `src/Sv.Pack` (`net10.0`): CLI and build tool (`sv pack|run|trace`) and scenario harness (`ScenarioReader`, `ScenarioRunner`).
- Legacy phase 1/2 code (`src/Sv`, `src/Sv.Compose`, `src/Sv.Frent`, `src/Sv.Burst`, `src/Sv.Generators`) remains untouched side by side until M11 (`CONTRACT-v1.0.3.md`, `CONTRACT-v2.1.0.md`).
- Contracts (`contract/CONTRACT-v3.0.0.md` through `v3.2.0-stat-layers.md`): normative law for math, layouts, layers, errors, and determinism.

## Data shape and who uses it how

- **Memory layout**: Entities occupy slots `(chunk, row)` in `64`-row chunks (`Lanes.Rows = 64`). Every `Stat` (signed `int` with fixed-point `scale` 1..1,000,000 and basis points 10,000 = 100%), `Intrinsic` (signed `int`), and `Event` (bit-packed in `ulong`/word lanes) lives at a compile-time fixed lane offset (`Layout`, default-shared per-chunk lanes or dense identity table).
- **Frame pipeline**: Sends during frame `N` write `16`-byte `Effect` records lock-free into per-writer `RawArena` blocks. At frame `N+1`, `Seal` merges and sorts them into canonical order per chunk, `Stats` updates `core` (including `"derive"` edges) and `external` modifier layers, `Decide` runs compiled gate/absorb/remove/emit/flow kernels, `Commit` folds deltas, updates bounds and event hysteresis, and marks the `32`-epoch `Journal` + `InputLog` ring for deterministic server rewind.
- **Who uses it**: Designers author pure JSON packs (`values.sv.json`, `rules.sv.json`, `cards.sv.json`, `templates.sv.json`) and test them in `.scenario.json` runs; `sv pack` compiles packs at build time into `Values.g.cs`, `Kernels.g.cs`, `HostKernels.g.cs`, and binary `pack.svb`; hosts (`.NET`, `Sv.Host`, Unity 7 in M8, browser WASM) feed inputs, step `FrameLoop`, and read resolved values/events.

## Layer table (skill rule 19)

Every layer has one stated job, plain-data inputs and outputs, an alone-test, a readable preview dump, and a slow reference form (`Sv.Rules.Reference` or scalar law). Compiler fusion may run adjacent passes in one loop only when bit-identical; keep this table updated in the same commit as any layer change.

| Layer | Job | Input -> Output (plain data) | Alone-test | Preview dump | Reference form |
|---|---|---|---|---|---|
| 0 storage | Hold every stat, intrinsic, and event at its enum offset across shared or dense `64`-row chunks | `Layout` + template defaults -> chunk lane arrays & chunk images | Codec round trip, `Lanes` address law, slot/lane-keyed digest (`Sv.Laws.Tests`) | Dump chunk rows & lanes (`ChunkImage` / snapshot) | Dense flat chunk snapshot |
| 1 inputs | Capture sends in frame `N` as 16-byte records and seal them to canonical deterministic order | Writer `Effect[]` -> `RawArena` -> `CanonicalArena` sorted `Effect` spans | Arrival-permutation invariance & overflow conservation (`Seal`, `Sv.Laws.Tests`) | Records per chunk/target in canonical order | Sorted list in `Reference.Interpreter` |
| 2 stats | Fold active `StatOp` and `CardTerm` modifiers (`core` including derived edges, then `external`) into stat lanes | `StatOp[]` + `CardTerm[]` + `StatSource[]` -> stat lane writes & `StatUndo` log | Layered scalar `StatLaw.Fold` vs `BigInteger` & `Vector256` twin; `StatMachine` rewind (`Sv.Laws.Tests`) | Per-stat core/external terms and resolved value | `ReferenceLaw.StatFold` + `Interpreter.Stats` |
| 3 rules | Evaluate stratified gates, absorb budgets, removes, triggered emits, and flow deltas in one deterministic pass | Canonical records + chunk lanes -> intrinsic deltas & next-frame `Effect[]` | Rule/expression & gate-group laws (`Sv.Laws.Tests`) | Rule trace: fired gates, absorb splits, emits, denied records (`sv trace`) | `Sv.Rules.Reference.Interpreter` step |
| 4 commit | Apply channel/flow/bounds fold rules and update event level/edge hysteresis | Intrinsic deltas + bounds/event specs -> committed chunk lanes & event bitmasks | Bound/clamp fold laws & event enter/stay hysteresis (`Sv.Laws.Tests`, `Net` twins) | Before/after values and raised edges per changed cell | Reference fold & event step |
| 5 time travel | Retain `32`-epoch lane deltas, input logs, and stat undo records for exact rollback and late-input replay | Epoch writes + frame inputs -> `Rewind(epoch)` & deterministic `Replay` trace | Epoch rewind + replay digest equality (`Sv.Laws.Tests`, `Sv.Host.Tests`) | Touched chunks/lanes and log byte footprint per epoch | Straight frame execution from epoch 0 |
| 6 tools | Parse, validate, canonically serialize, compile, and execute packs and scenarios | Pack JSON + `.scenario.json` -> C# kernels, `pack.svb`, `PackHash`, `ScenarioOutcome` | Compiler diagnostics, `PackHash` round trip, reference == compiled diffs (`Sv.Diff.Tests`, `Sv.Scenario.Tests`) | Canonical `PackWriter` JSON, `sv trace`, scenario assertion lines | Unfused `Reference.Interpreter` via `sv run` |
| 7 hosts | Drive workers, `RawArena` scaling, `Collect`, and frame sequencing without game logic | Game sends/`StatOp`s/spawns/kills -> `FrameLoop.Step()` & world digest | Equal digests across `Shared`/`Dense`, `1/2/4/7/11` workers, AVX2/scalar ISAs, 0 B warm (`Sv.Host.Tests`, `Sv.Lego.Tests`) | Phase timings (`FrameLoop.Marks`) & memory/digest summary (`Sv.Bench`) | Single-thread `ScenarioRunner.Reference` |

## Issues and verification

- Track findings under `issues/NNNN-*.md` with `Status: open|closed`, exact reproduction commands, and observed receipts; update issue status before finishing when fixed or impacted.
- Verify in layer order before pushing any code change: `Sv.Core` & `Sv.Rules` build with 0 warnings on `netstandard2.1` and `net10.0`; run `Sv.Laws.Tests`, `Sv.Scenario.Tests`, `Sv.Host.Tests`, `Sv.Lego.Tests`, `Sv.Diff.Tests` under default, `DOTNET_EnableAVX2=0`, and `DOTNET_EnableHWIntrinsic=0`; confirm `Sv.Bench` digests and 0 B warm allocation (`--ms 1000`). Note: after modifying `Sv.Rules` or `Sv.Host`, rebuild `src/Sv.Pack` and clear `tests/Sv.Kernels.*/obj/SvPack` before regenerating kernels.
