approved 2026-11-14 (decision D21, decided by the lead dev under the owner's standing delegation ("think like a senior dev and take shots", 2026-11-14)); additive, every existing PackHash and digest unchanged

# The Composition Contract - v3.2.0 (stat layers and derived stats; amendments over v3.0.1 and v3.1.0)

v3.2.0 keeps every clause of v3.0.1 and v3.1.0 that is not named below. It adds two stat modifier layers (`core`, then `external`)
and the pack row form for derived stats (`issues/0330-lego-brick-set-gaps.md` and `issues/0210-integer-stats-gaps.md` item 3).
Both additions are strictly additive: a pack or scenario that omits `"layer"` and `"derive"` reads, hashes (`PackHash`), emits,
compiles and runs identically to v3.1.0 (every existing pack hash and scenario digest stays byte-identical). The reference
interpreter (`src/Sv.Rules/Reference`) and `StatLaw` remain the oracle for every clause; compiled execution equals them.

Words used below. A *layer* is one of two ordered folds over a stat's modifiers: `"core"` (layer `0`, the default) and
`"external"` (layer `1`). *Derived terms* of a stat `T` are the `Added` terms produced by clause 32 from other stats of the same
entity. *Canonical term order* within a layer is unchanged from clause 31 (`ShapeKind` ascending: `Added`, `Additive`, `Multi`,
`Cap`, tie-broken by `Value` ascending). *Saturation* (`sat`) and *division toward zero* follow clause 29 (`StatLaw`).

> **Clause 39, new. Two stat layers (`core` and `external`).**
> - **Syntax.** Every card modifier in `cards.sv.json`, every scenario modifier in `*.scenario.json` and every host stat operation
>   (`StatOp` / `StatMachine.Add`) accepts an optional layer: `"layer": "core"` or `"external"` in JSON (`StatLayer.Core = 0`
>   or `StatLayer.External = 1` in C#). When `"layer"` is omitted, it defaults to `"core"` (`StatLayer.Core`).
> - **Two-pass fold (amends clause 31 for stats).** For an entity `e`, stat `T` with template/stat base `b`:
>   1. **Core layer.** Gather all derived-stat `Added` terms of `T`, every held card modifier on `T` with layer `core` (clause 36
>      per-count if marked), and every active host `StatOp` modifier on `T` with layer `core`. Canonicalize these core terms
>      (clause 31) and compute `afterCore = Fold(b, coreTerms)`.
>   2. **External layer.** Gather every held card modifier on `T` with layer `external` (per-count if marked) and every active
>      host `StatOp` modifier on `T` with layer `external`. Canonicalize these external terms (clause 31) and compute
>      `after = Fold(afterCore, externalTerms)`. When no external terms apply, `after = afterCore` (identical to v3.0.1).
> - **Canonical serialization and determinism.** `PackWriter` omits `"layer"` on card modifiers when it equals `"core"` and writes
>   `"layer": "external"` when `External`. Host `StatOps.Compare` includes `Layer` (`Core < External`, placed immediately after
>   `Stat`) and reference `RefStatOp.Compare` provides a deterministic total order across layers so arrival permutation never changes
>   results. `StatMachine` includes `Layer` in `ModifierRecord`, in its undo log (`StatUndoEntry`), and in `KeyOf` hashes so
>   `Rewind(epoch)` restores the exact active modifiers and their layers and recalculates identically.
> - **Errors.** In JSON (`cards.sv.json` / `*.scenario.json`), a non-string `"layer"` raises `WrongType` (or `ScenarioError`);
>   a string other than `"core"` or `"external"` raises `BadValue` (or `ScenarioError`). At the host (`StatMachine.Apply` /
>   `StatMachine.Add` / `StatMachine.ctor` for cards) and reference (`Interpreter.Stats`), an invalid layer above `External` increments
>   `Rejected` / `StatOpFault` (or throws `ArgumentException` for invalid `CardTerm`s at construction) without mutating modifiers.

> **Clause 40, new. Derived-stat row form in packs (amends clause 32).**
> - **Syntax in `values.sv.json`.** A row with `"class": "stat"` may carry `"derive"`: either a single source object or a
>   non-empty array of source objects. Each source object names `"from"` (string name of a declared `"stat"` row in the pack) and
>   either:
>   1. **Linear edge:** `"coefficient"` (exact decimal at basis-point scale `10000`, e.g., `1.0` = `10000`, `-0.5` = `-5000`),
>      computing `StatLaw.Derived(sourceVal, coefficient, scale(from), scale(T))`.
>   2. **Curve edge:** `"curve"` (non-empty array of exact decimals at `scale(T)`), `"min"` (exact decimal at `scale(from)`), and
>      `"step"` (strictly positive exact decimal at `scale(from)`), computing `curve[CurveBucket(sourceVal, min, step, curve.Length)]`.
> - **Canonical serialization.** `PackWriter` omits `"derive"` when a stat has none, and otherwise writes `"derive"` as an array
>   `[{...}, ...]` using canonical scaled numbers (`10000` for linear `"coefficient"`, `scale(T)` for `"curve"` entries,
>   `scale(from)` for `"min"` and `"step"`).
> - **Semantics and evaluation order.** Derived sources belong to the **`core`** layer (computed as `Added` terms inside the core
>   fold before any `core` additive, multi, cap or `external` modifiers). Each derived edge reads `from`'s final resolved value
>   (after both `core` and `external` layers of `from` have folded). On spawn, template derived stats fold cleanly from template
>   bases before frame-0 card/modifier recalculation (`FoldTemplate` in compiled host, matching baseline initialization in reference).
>   `HostPack.From` compiles all pack derive definitions into `StatSource[]` and `int[]` curves and passes them to `StatMachine`.
>   Both `StatMachine` and `Reference.Interpreter` evaluate stats in topological dependency order so every change cascades in one step.
> - **Errors.** `"derive"` on a non-stat row raises `BadValue`. An empty `"derive"` array, empty `"curve"`, having neither or both of
>   `"coefficient"` and `"curve"`, `"min"`/`"step"` on a linear edge, missing `"min"`/`"step"` on a curve edge, or `step <= 0` raises
>   `BadValue` / `MissingKey` / `WrongType` / `NotExact`. An unknown `"from"` stat raises `UnknownName`. A dependency cycle among
>   derived stats (including a self-edge) is rejected at plan time with `DiagnosticCode.CyclicStat` (and `ArgumentException` if built
>   directly on `StatMachine`).

> **Clause 7 and Clause 27, reaffirmed.** Canonical order in `StatOps` separates `Core` before `External` as tie-breaker without
> changing `RemoveSource` ordering; `ModifierRecord` undo entries save and restore `Layer`; `Rewind(epoch)` restores the exact set
> and layers of active modifiers and recomputes both layers cleanly.

## Readings taken (the code follows them; owner to confirm)

- **Derived terms live in the `core` layer.** Clause 32 defines a derived source as "computed before T's own modifiers"; placing derived
  terms in `core` ensures `core` additive/multi/cap and `external` modifiers stack on top of derived values as expected.
- **Dependent stats read final (post-`external`) source values.** When stat `B` derives from stat `A` and an external modifier
  changes `A`, `B` reads `A`'s resolved value (after both layers of `A`) in the same recalculation pass, keeping the single-value
  stat invariant per slot.
- **Canonical `"derive"` writer form vs reader flexibility.** `PackReader` accepts both a single object (`"derive": {...}`) and a
  non-empty array (`"derive": [{...}]`), while `PackWriter` canonically writes an array so multiple sources roundtrip losslessly
  through `PackHash`.
- **Scaling of linear coefficients vs curves.** Linear `"coefficient"` uses basis-point scale `10000` (matching `StatLaw.Derived` and
  card/gate `"additive"`/`"multi"`), while curve `"min"` and `"step"` use `scale(from)` and curve table entries use `scale(target)`.
- **Template spawn baseline.** Both `Host` (`FoldTemplate`) and `Reference` (`ApplyCommands`) compute initial derived-stat values from
  template bases (without cards or runtime ops) upon entity creation so frame-0 `bound moved` and dirty-stat detection match cleanly.

## Not in this version

Per-layer cap or additive merging across layers without an intermediate fold, third custom stat layer, or expression read of
pre-external `core` sub-value.
