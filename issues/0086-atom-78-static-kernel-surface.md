# Issue 0086 — Atom 78 (roadmap 6.1b): `Sv.Kernel` — the chunk verbs as static kernels, no `Board` needed

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) phase 6, atom 6.1,
second slice (§5.5). Labels: `ai`, `area:core`, `kind:feat`. Contract:
none — the `Board` verbs keep their signatures and delegate.

## Changes

- New `static unsafe class Sv.Kernel`: the six pointer chunk verbs —
  `IntrinsicsChunk` (uniform / per-row delta) and `EnabledChunk`
  (single-word / multi-word) — as `static` methods taking the
  **capacity explicitly**. Null guards and range guards run unchanged
  (`Guard.ChunkRange`/`EnabledRange` against the passed capacity), then
  the same `Simd.*` kernels. A caller that holds only pointers, masks
  and an unmanaged `Clamp` (atom 77) can run every kernel — no `Board`
  instance, nothing managed on the path.
- The twelve `Board.Run*` overloads become thin wrappers: validate
  against `Capacity`, delegate to `Sv.Kernel`. Behavior bit-identical;
  the `Span` twins keep their pin-and-forward shape.

## Tests

- New `KernelTests`: stackalloc'd columns + constant/stat clamps driven
  through `Sv.Kernel` alone equal the `Board` verbs' results for the
  same inputs (values + `applied`, uniform and per-row delta, single-
  and multi-word enabled); range violations throw identically against
  the explicit capacity.

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **337/337 passed** (334 + 3
  new); 3× repeat stable.
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 337/337.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27.
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios
  ok, alloc 0 B (every existing scenario drives the verbs through the
  delegation — results unchanged, no bench delta claimed).
- Receipt: `KernelTests` drives the full static surface from
  stackalloc'd columns with no `Board` on the kernel path (values and
  `applied` equal the `Board` verbs; range violations throw identically
  against the explicit capacity).
- Naming note: the class is `Sv.Kernel`, not `Sv.Kernels` — the vendored
  `StrictFloatOracle.cs` declares `namespace Kernels.Testing` inside the
  test project and shadows a plural-named class there; the oracle file
  is untouchable by AGENTS.md, so the singular wins.
- FMA grep clean; no production comments.

## Provenance

`main@0835da0` + this atom. Remaining 6.1 work (the state-struct owner
split, `Sv.Core` as an assembly) folds into 6.2's Burst shims, which
are bus-gated (clause 1 extension).

Bus note: `/home/i/GitHub/agent-bus` does not exist on this host
(`/home/os`), so the pre/post-atom bus checks could not run; recorded
here per AGENTS.md discipline.
