# Issue 0045 — Atom 39: idempotent `Game.Dispose` (review C-4)

Filed as GitHub issue #25 (numbering offset — see issue #24 provenance note). Labels:
`ai`, `area:compose`, `kind:feat`. Source: design review 2026-10-20
([issues/0044](0044-2026-10-20-design-review.md), GitHub #24), finding C-4 — verified
against source before fixing.

## Problem

`Game.Dispose` freed `Applied` with no `disposed` guard (`Board` and `Streams` are
guarded). `using var host` plus an explicit `host.Game.Dispose()` — or any double
dispose — frees the same native block twice: heap corruption in the common C#
dispose-pattern composition.

## Shape

- `Game` gains a `disposed` flag; `Dispose` returns early when already disposed.
- No signature, math, or state-format change.

## Receipts

- `ComposeTests.GameDispose_IsIdempotent`: builds the full NorthStar host, disposes
  `Game` twice, then the host — passes (double free would abort or corrupt).
- Suite: `dotnet build sv.slnx -c Release` green; 206 tests pass
  (23 NorthStar incl. the new one + 6 Unity + 177 Sv.Tests).
