# Issue 0088 — Atom 80 (roadmap 6.5a): aligned columns — 64 B for every sv-owned column, huge-page advice for the big ones

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) phase 6, atom 6.5,
first slice (§4.10, §5.6). Labels: `ai`, `area:core`, `kind:feat`.
Contract: none.

## Changes

- `NativeArena` gains aligned allocation: `AllocAlignedZeroed<T>(count)`
  and `FreeAligned<T>(pointer, count)`. Alignment is 64 B by default and
  **2 MiB once the request reaches 2 MiB** (an sv-owned column at 1M
  rows is 4 MiB), and on Linux the big blocks also get
  `madvise(MADV_HUGEPAGE)` — best-effort; the receipt is the alignment
  itself, THP backing is the kernel's call. Arena `Live` accounting
  counts requested bytes exactly, as before — alignment padding and
  advice never enter `MemoryBytes`.
- The sv-owned hot surfaces allocate through it: lazy owned columns
  (both the on-first-read and the on-load paths) and bound mirrors
  (bind + load paths). Host-bound columns are host memory — untouched.
- Eight-line wires across five sites in `Board.Stats.cs`; behavior,
  values and the exact `MemoryBytes` bills are unchanged.

## Tests

- New `AlignedColumnTests`: materialized owned columns and mirrors are
  64-aligned; a ≥ 2 MiB column is 2 MiB-aligned; `MemoryBytes` delta on
  materialization is still exactly `capacity × sizeof(T)` (alignment
  never billed); the existing exact-bill suites carry the rest.

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **345/345 passed** (342 + 3
  new); 3× repeat stable.
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 345/345.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27.
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios
  ok, alloc 0 B.
- Alignment receipt: materialized owned columns and bound mirrors are
  64-aligned; a ≥ 2 MiB column is 2 MiB-aligned (huge-page-backable);
  `MemoryBytes` delta on first read is still exactly
  `capacity × sizeof(float)` — alignment padding and `madvise` never
  enter the bill, and the exact-bill suites passed unmodified.
- FMA grep clean; no production comments.

## Provenance

`main@227f10a` + this atom. Next: 6.5b — the persistent pinned worker
pool for bulk passes (the L9 scaling receipt), then `Vector512`.

Bus note: `/home/i/GitHub/agent-bus` does not exist on this host
(`/home/os`), so the pre/post-atom bus checks could not run; recorded
here per AGENTS.md discipline.
