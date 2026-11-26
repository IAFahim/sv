# Issue 0023 — Atom 18: jb inspection hygiene pass (ReSharper CLI 2026.2.1)

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Filed as GitHub issue #23 (pending — gh unreachable this session). Labels: `ai`,
`area:core`, `kind:test`.

## Problem

`jb inspectcode` over sv.slnx reported ~700 findings. Most are style noise or
contractual surface (Unity facade stubs, StrictFloatOracle, unused-by-solution
public verbs the contract mandates). One was a real misuse: InputState.Create
passed a whole sentence to the single-string ArgumentOutOfRangeException
constructor — that overload takes a parameter name, so ParamName swallowed the
message ("Cannot resolve symbol" inspection).

## Taken (production src only)

- InputState.cs: ArgumentOutOfRangeException -> two-arg nameof form (codebase
  idiom); ringToAxes/ringToButtons -> PascalCase to match the file's own private
  methods.
- Board.cs: completed XML param tags on the four span RunEnabledChunk overloads
  (each documented only `applied`).
- Simd.cs: removed dead internal NormalizeBools dispatcher (width variants stay —
  tests exercise them); removed unused usings.
- Tape.cs: removed redundant `using Sv;`.
- EventStreams.cs: Grow dropped its unused `writer` parameter.
- Clamp.BoundOf, SetTable.ContentHash: internal -> private (no external refs).
- Compose.cs: dropped inferable `<THost>` type argument; redundant `(nuint)` casts
  before constant `sizeof(T)` removed across src (kept on `sizeof(byte*)` —
  pointer sizeof is not a compile-time constant; the compiler enforces this).

## Deliberately not taken

- Mass `_camelCase` field renames, primary constructors, collection expressions,
  "invoke as extension method" (static call form is the file idiom), foreach
  conversions: churn with no correctness value.
- "Never used" on public/contract surface (1-word verbs, seam methods, facade
  stubs, StrictFloatOracle): mandated surface or verbatim vendored copies.
- EventStreams.TryReadNext `out size` (always discarded by callers but part of
  the read protocol), Frent ChunkCursor disposal (ref struct + Dispose — foreach
  disposes it; jb cannot see compiler-generated calls).

## Receipts

- Build: 0 warnings, 0 errors (Debug, sv.slnx).
- Tests default: 126 + 16 + 6 passed, 0 failed. SV_CHECKED: 128 + 16 + 6 passed,
  0 failed.
- jb re-run: all targeted findings gone from the report.
- no-FMA grep clean.
