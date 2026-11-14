# 0210 integer stats (D11): gaps found while porting the stat machine

Status: closed (2026-11-14, decided by the lead dev under the owner's standing delegation ("think like a senior dev and take shots", 2026-11-14))

Found while writing `src/Sv.Core/Stats` and the integer fold of `src/Sv.Rules` (branch v3/c3-stats).

1. **Two stat-op models.** The oracle (`Reference/Records.cs` `RefStatOp`) adds and removes modifiers by handle, with no source and no
   expiry, and folds card modifiers by possession count. The machine (`Sv.Core/Stats/StatOp.cs`, plan 13 and legacy `StatOp`) has
   `RemoveSource`/`Add` with source ids and expiry ticks, and knows no cards. `compiled == interpreted` on stats needs one model.
   Reading taken: none; both are kept as written. Proposal: the oracle adopts `StatOp` and cards become StatOps sent on possession edges.
2. **Floats left in the rule language.** No state is float now, but `Expression` still has `ExprType.Float` (`FloatConst`, `ToFloat`,
   `BoundOf`, float `+ - *`). Reading taken: a stat read is `Int`, the stat in whole designer units (`Rescale(v, scale, 1)`, lossy below 1).
   Alternative: exact scaled reads with literals scaled by the reader. Removing the Float type is a C1/owner call.
3. **No derived stats in the pack.** The machine takes `StatSource` rows (linear in basis points, or a curve) and orders them as a DAG;
   `values.sv.json` has no row form for them, so packs cannot author derived stats yet.
4. **Contract 3.0.0 clause 24 said `BoundOf` after Shape**; with integer Shape there is nothing to convert. Superseded by v3.0.1.
5. **Duplicate public type names.** `StatOp`, `StatOpKind`, `StatOps`, `ModifierHandle` now exist in both `Sv` (legacy) and `Sv.Core`
   (same namespace `Sv`), adding to issue 0160 item 5 (CS0433 for assemblies referencing both).
6. **Host duties (C2).** Call `StatMachine.Kill(slot)` with `World.Release`, `MarkEpoch(world.Epoch)` after each epoch mark,
   `Rewind(e)` together with `World.Rewind(e)`, and fill template stat lanes from `FoldTemplate` before `World.AddTemplate`.

Receipts: `dotnet build src/Sv.Core/Sv.Core.csproj -c Release` and `dotnet build src/Sv.Rules/Sv.Rules.csproj -c Release`, net10.0 and
netstandard2.1, 0 warnings, 0 errors. Nothing run (no tests this wave by owner order).

## Decision (2026-11-14)

1: the machine's `StatOp` model (source ids, expiry) is the production model; the oracle's handle model stays a test oracle, and the Lego and Scenario suites check card and stat outcomes against it. Unify only if a diff test finds a divergence. 2: keep `ExprType.Float` for literals only (single IEEE operations, no float state); stat reads stay whole designer units. 3: done in contract v3.2.0 (derived-stat rows). 4: superseded by v3.0.1. 5: a project references legacy `com.sv.core` (namespace `Sv`, sv 2.1) or v3 `com.sv.engine`, never both in one assembly; stated in docs/PRODUCTION-CHECKLIST.md. 6: done by the host.
