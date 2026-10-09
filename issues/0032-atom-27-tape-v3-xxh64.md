# Issue 0032 — Atom 27: tape v3 — XXH64 64-bit digest, whole-word body copy

Filed as GitHub issue #32 (pending — see receipts). Labels: `ai`, `area:compose`,
`kind:perf`, `breaking:tape-format`.

## Problem

Tape v2 digested each frame with FNV-1a one byte at a time (a serial multiply chain,
~1 byte/cycle) and gathered every live float individually, even when all 64 rows of a
live word were present. At 100k rows the tape cost ~0.58 ms/frame on top of the frame
itself. The 32-bit digest is also weak for desync detection over long runs.

## Shape

- `Tape.Version` 2 -> 3. Body layout unchanged (dt, axes, buttons, live stat rows
  ascending, live words). Fully live words copy 256 B in one `Buffer.MemoryCopy`.
- Digest is XXH64 (seed 0) over the finished body, stored as a 64-bit field in the
  frame header (was 32-bit). `Sv.Compose.XxHash64` is a self-contained
  implementation of the published spec (integer-only, little-endian reads) — clause 8
  (no runtime dependencies) holds.
- Playback bounds-checks the digest + length fields before reading them (v2 read them
  unchecked after the input words); mismatch messages print 16 hex digits.
- v2 tapes are rejected with the existing version error. The tape format is sv-local
  (not in the Composition Contract); no bus change needed. sv-examples does not use
  the tape API (no `Record`/`Playback` calls, no `.tape` files) — verified by grep.

## Receipts

- `XxHash64Tests`: published vectors ("", "a", "abc", "Nobody inspects the spammish
  repetition") and byte-for-byte agreement with `System.IO.Hashing.XxHash64` (test-only
  package) for every length 0-300. The cross-check caught a mistyped PRIME64_4 on the
  first run (lengths >= 8 diverged) — fixed before commit.
- Tape round-trip, corrupted-digest, future-version and body-compare tests green.
- Bench: tape-digest 1485 -> 762 us/frame, frame-run 909 -> 704 (other atoms), so
  the tape's own share fell ~576 -> ~58 us/frame (~10x).
- Tests default: 148 + 21 + 6 passed, 0 failed. SV_CHECKED: 150 + 21 + 6 passed.
- no-FMA grep clean.
