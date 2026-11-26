# Issue 0085 — Atom 77 (roadmap 6.1a): unmanaged `Clamp` — the managed `Board` reference dies (S9)

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) phase 6, atom 6.1,
first slice (§5.5, S9). Labels: `ai`, `area:core`, `kind:feat`. Contract:
none — the public factories keep their signatures; only the struct's
fields change.

## Changes

- `Clamp` holds no managed reference anymore: `Board? board`,
  `minStatIndex`, `maxStatIndex` die. The factories still take a `Board`
  at construction (that is S9's shape — validate at construction, then
  hold only unmanaged state), and the `SV_CHECKED` staleness check reads
  the board's generation counter **through a pointer**
  (`int* generationSlot` captured from the arena-backed
  `boundGeneration` array — native memory, never moves), comparing it to
  the generation captured at construction. Semantics identical: storage
  moved or rebound ⇒ generation differs ⇒ `sv: stale binding — storage
  moved; rebind`.
- `Board.BoundGenerationPtrAt(statIndex)` (internal) exposes the slot
  pointer.
- With that, `Clamp` satisfies the `unmanaged` generic constraint — the
  S9 receipt: the type compiles in a `where T : unmanaged` context, the
  shape Burst can hold. Data-path behavior is untouched (values already
  flowed through raw pointers).

## Tests

- New `UnmanagedShapeTests`: `RequireUnmanaged<Clamp>()` — compilation
  under `where T : unmanaged` is the receipt; the same for
  `ClampShift`.
- Existing clamp suites (constant/columns/stat/mirror, stale-binding
  throws under `SV_CHECKED`) carry value behavior unchanged.

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **334/334 passed** (333 + 1
  new); 3× repeat stable.
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 334/334
  (the generation-pointer staleness check is exercised here, including
  the stale-binding throw suites).
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27.
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios
  ok, alloc 0 B (rollback-resim 561 µs, keyframe 404 µs — unchanged).
- S9 receipt: `Clamp` compiles under `where T : unmanaged` — no managed
  field remains; the `SV_CHECKED` staleness check reads the
  arena-backed generation counter through a captured pointer.
- FMA grep clean; no production comments.

## Provenance

`main@a4a1c10` + this atom. Next: 6.1b — the board state as an unmanaged
struct + static kernels (`Sv.Core`), then the owner split for
`EventStreams`/`InputState`.

Bus note: `/home/i/GitHub/agent-bus` does not exist on this host
(`/home/os`), so the pre/post-atom bus checks could not run; recorded
here per AGENTS.md discipline.
