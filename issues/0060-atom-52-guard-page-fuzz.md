# Issue 0060 — Atom 52 (roadmap 0.2): guard-page fuzz harness — F1/F2/F3/F23 reproduce as crashes

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) phase 0, atom 0.2. New
`tests/Sv.Fuzz` console child + `GuardPageFuzzTests` in Sv.Tests. Labels: `ai`,
`area:core`, `kind:test`. Contract: none.

## Design

- `Sv.Fuzz` (Linux) places every offered column in an `mmap` region with a
  `PROT_NONE` page on **both** sides — arrays end flush against the trailing guard
  (overrun crashes) and, where a negative index matters (the enabled `words`), start
  flush against the leading guard (underflow crashes). Any read/write outside the
  offered memory faults; the CLR fault handler turns it into a nonzero exit the
  parent reads.
- 22 cases drive the public window surface: `Board.RunIntrinsicsChunk` (uniform,
  varying) and `RunEnabledChunk` (word, words, `Span` twins), the `ChunkSeam`
  statics (`RunChunk` ×2, `RunEnabledChunk` words), and `Population`'s real-Frent
  `ChunkWindow.ApplyEnabled`. Short `applied`/`deltas`, interior words past the
  chunk, `wordCount ≤ 0`, and `start + count` / `offset + 64·wordCount` overflow
  are all in the table.
- The table lives in the child (`--list` → `id|expected-today`); the xunit fact
  spawns one child per case and asserts the observed outcome set **equals the pinned
  table** — the suite stays green while pinning today's defects. Atom 1.1 flips the
  crash expectations to `threw` and the same fact proves the fixes (law L16).
- The child runs under `RLIMIT_CORE=0` + non-dumpable `prctl`, so intentional
  guard-page faults leave no core dumps.
- Linux-only (mmap/mprotect); the fact skips elsewhere. macOS needs only a
  `MAP_ANON` constant switch — deferred until a macOS CI leg asks for it.

## Pinned defect set (today, measured)

Crash (11): `board-uniform-short-applied` (F3), `board-varying-short-delta` (F3),
`board-enabled-words-overrun` (F2), `board-f23-chunk-overflow` (F23),
`board-f23-enabled-overflow` (F23), `seam-runchunk-short-applied` (F3),
`seam-runchunk-short-deltas` (F3), `seam-enabled-interior-past-chunk` (F2 — the
roadmap's exact `words {0,1,0}` repro), `seam-enabled-applied-window-overrun`
(F3), `population-enabled-wordcount-zero` (F1 — reads `words[-1]`),
`population-enabled-wordcount-negative` (F1).

Throws as designed (8) / ok (3): capacity bounds, negative count, `wordCount < 1`,
the `Span` twins' window checks, `ChunkSeam`'s offset-in-chunk check, the atom-45
single-word tail police, and the three in-bounds sanity cases.

## Receipts

- `dotnet test sv.slnx -c Release --filter GuardPageFuzz` — Passed (0.5 s warm;
  23 process spawns). All 22 outcomes match the pinned table on the first run.
- Manual per-case run (`dotnet Sv.Fuzz.dll --case <id>`): crash cases exit 134
  (CLR fault handler on the guard-page SIGSEGV) with no stdout; sanity cases
  `ok`/`threw:<Type>`.
- Full suite: 221/27/6 passed, and again with `-p:SV_CHECKED=true`.
- Zero new coredumps after the run (`coredumpctl list --since` clean) —
  `RLIMIT_CORE=0` + `PR_SET_DUMPABLE=0` verified working.
- `Sv.Fuzz` added to `sv.slnx`; `Sv.Tests` references it so the child dll rides
  along (`dotnet <path>/Sv.Fuzz.dll`).

## Not covered (and why)

- The F1 underflow **write** (`rowPin − start` with `offset` outside the chunk)
  lands in Frent-owned memory, which the harness cannot wrap in guard pages. Its
  memory-safety primitive is pinned via the Board/seam cases; the seam-level
  offset check itself becomes a throw-test in atom 1.1 (issue 0061's successor,
  phase 1.1) where a real Frent world asserts `ApplyEnabled(offset outside chunk)`
  throws.
- Windows (`VirtualAlloc` guard pages) and macOS legs: skipped for now.

## Provenance

`main@566099d` + this atom. Every "crash" expectation was observed, not assumed.
