# Issue 0090 — Atom 82 (roadmap 7.2): schema as code — source generator, compile-time DAG check, typed accessors

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Roadmap [issues/0058](issues/0058-roadmap-1m-entities-5us.md) phase 7, atom 7.2
(F22). Labels: `ai`, `area:core`, `kind:feat`. Contract: none (purely additive).

## Changes

- **`src/Sv/Schema.Attributes.cs`** — four runtime attribute types:
  `[SvSchema]` (marks a `partial` class as a schema), `[SvStat(name, base)]`,
  `[SvDerived(name, base, sources[], coefficients[])]`, `[SvIntrinsic(name)]`,
  all `AllowMultiple` where needed, all on `AttributeTargets.Class`.
- **`src/Sv.Generators/`** (new project, netstandard2.0 Roslyn component,
  `Microsoft.CodeAnalysis.CSharp` 4.14.0) — `SvSchemaGenerator`, an incremental
  generator driven by `ForAttributeWithMetadataName("Sv.SvSchemaAttribute", …)`.
  It reads the `[SvStat]`/`[SvDerived]`/`[SvIntrinsic]` attribute list in
  declaration order, validates the schema, and emits the other half of the
  partial class:
  - `public static Sv.Schema Schema { get; }` — built through the exact same
    `SchemaBuilder` chain the user would have written by hand (so
    `ContentHash` is identical by construction);
  - `public static Sv.StatId <Name> { get; }` / `Sv.IntrinsicId <Name>` —
    resolved once in the static constructor, zero per-access cost;
  - `public static float Get<Name>(Sv.Board board, int slot)` typed accessors
    over `Board.Output`.
  The validation is the runtime `SchemaBuilder` rule set mirrored at compile
  time, as error diagnostics (the atom's receipt: unknown stat names fail to
  compile):
  - SVSGEN01 derived source declared later than the derived stat (forward
    reference = the cycle case; the DAG is "sources must already be declared");
  - SVSGEN02 source name not declared anywhere (the unknown-name receipt);
  - SVSGEN03 duplicate stat/intrinsic name, or a name used both ways;
  - SVSGEN04 schema class is not a top-level `partial` class;
  - SVSGEN05 name is not a valid C# identifier (names become members);
  - SVSGEN06 `sources.Length != coefficients.Length`;
  - SVSGEN07 non-finite base or coefficient;
  - SVSGEN08 schema declares nothing;
  - SVSGEN09 name collides with a generated member (`Schema`).
  The incremental pipeline is equatable-model-then-render (attribute data is
  folded into a value-equal model record before the caching boundary; the
  rendered text depends only on that model — deterministic output).
- **`sv.slnx`** — `Sv.Generators` added; **`tests/Sv.Tests`** — generator wired
  in twice: as a compile-time analyzer (real consumption) and as a normal
  project reference (harness instantiates `SvSchemaGenerator` directly);
  `Microsoft.CodeAnalysis.CSharp` 4.14.0 as a test-only package for the
  compilation harness.

## Tests

- New `SchemaGeneratorTests` (Roslyn harness, compiles real source text against
  the real `Sv` assembly): happy path compiles with zero errors and emits the
  expected members; unknown source, forward reference, self reference,
  duplicate, stat/intrinsic clash, non-partial class, nested class, bad
  identifier, empty name, length mismatch, NaN coefficient, empty schema,
  reserved name — each produces its error diagnostic and `HasErrors`, i.e. the
  schema fails to compile (receipt).
- New `SchemaCodegenTests` (real consumption via the analyzer reference): the
  generated `GameSchema.Schema` has the same `ContentHash` as the identical
  hand-built `SchemaBuilder` schema (bit-identical schema receipt); generated
  `StatId`/`IntrinsicId` members resolve through `Schema.Stat`/`Intrinsic`;
  typed accessors equal `board.Output(stat)[slot]`; a derived stat folds
  through a live board end-to-end (spawn → modifier → recalculate → accessor).

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **368/368 passed** (348 + 20 new);
  3× repeat stable.
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 368/368,
  0 warnings.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27.
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok
  (ff1m 2,918 µs p50, board 151 MiB, spawn1m 5.59 ms, bulkpass 1.21×, alloc
  0 B everywhere) — unchanged; the atom is compile-time only.
- FMA grep clean; no production comments (`///` API docs in `BindingHandle.cs`
  predate this atom and are unchanged); `contract/` and `StrictFloatOracle.cs`
  untouched.
- **The roadmap receipt, demonstrated on a real build** — a schema referencing
  an unknown stat:
  `error SVSGEN02: sv: derived stat 'damage' source 'strength' is not a
  declared stat` → `Build FAILED.` (temp file, then removed; the harness tests
  assert the same). The DAG direction, same way:
  `error SVSGEN01: sv: derived stat 'damage' source 'power' must be declared
  before it` → `Build FAILED.`
- Schema equivalence receipt: generated `GameSchema.Schema.ContentHash` equals
  the identical hand-built `SchemaBuilder` schema's hash; every stat name,
  base, and source pair matches; derived values fold through a live board
  (`Getdamage` = 15.75 after a +2 power modifier).
- Determinism receipt: two generator runs over identical input emit identical
  text (tested); the pipeline is equatable-model-then-render.
- Harness pitfall recorded: an in-test `CSharpCompilation` referencing only
  corelib leaves attribute ctor binding silently broken (`argc=0`) —
  `System.Runtime.dll` from the framework directory must be a metadata
  reference; with it, primary-ctor attribute metadata decodes fine.

## Provenance

`main@f25a81e` + this atom. Next: remaining atoms are bus-gated (clauses 3/4/5/
7/13/17) — none executable on this host until the bus exists.

Bus note: `/home/i/GitHub/agent-bus` does not exist on this host
(`/home/os`), so the pre/post-atom bus checks could not run; recorded
here per AGENTS.md discipline.
