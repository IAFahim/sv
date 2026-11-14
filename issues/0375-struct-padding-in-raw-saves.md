# 0375 Legacy board saves carried struct padding garbage: "identical runs save identically" failed on contended and ARM64 machines

Status: closed (2026-11-14, fixed with guards)

Found by: the `determinism` workflow on PR IAFahim/sv#95 (and on main since 2026-11-12): legacy `Sv.Tests` failed on ubuntu-24.04-arm (default) and ubuntu-latest (no-avx2, scalar), never on a developer box. Failing tests: `WheelTests.SameProgram_Twice_ProducesIdenticalSaves`, `BlockTests.IdenticalSequences_WithSpawnChurn_SaveByteIdentically`, `ModifierPoolTests.IdenticalOpSequences_SaveByteIdentically`, `SetReclamationTests.SameChurn_TwoBoards_ProduceIdenticalSaves`, `CodecTests.Board_HashCodec_TracksStateNotIdentity`. Diffs were 3-byte runs after a zero byte (`0, 0, 189, 0, 0` against `0, 0, 2, 3, 11`; `255, 255, 255` against `0, 0, 0`).

## Cause

Structs written to saves and hashes as raw bytes had implicit padding that C# does not zero:
- `StatEntry` (`byte Kind` + `float Value`, 3 padding bytes), stored in `SetTable.contentArena` and saved with `codec.Bytes(contentArena, contentUsed * sizeof(StatEntry))`. Its constructor set only the two fields, so the padding held stack bytes. This was the cause.
- `Board.ModRecord` (padding after `Kind` and after `Source`) and `Board.KeyframeRecord` (after `Kind`): same class; their writers construct whole records from temporaries.

The garbage depends on the thread's stack contents, so it differed only when scheduling differed (2 contended CPUs, ARM64 runners).

## How it was found (elimination)

- Not uninitialized heap memory: the save was byte-identical under `MALLOC_PERTURB_` 0, 85 and 170 in separate processes.
- Not ISA dependent: identical under default, `DOTNET_EnableAVX2=0`, `DOTNET_EnableHWIntrinsic=0`.
- Reproduced locally with `taskset -c 0,1 dotnet test tests/Sv.Tests`: 3-5 failures per run (6 runs).
- Two boards alive at the same time in one test (`Board_HashCodec_TracksStateNotIdentity`) hashed differently, which ruled out address reuse and pointed at bytes inside the state.

## Fix

- Explicit padding fields, always zero: `StatEntry.Pad0/Pad1` (set in the constructor), `ModRecord.Pad0/Pad1/Pad2`, `KeyframeRecord.Pad0/Pad1` (set at every construction site). Layout and size are unchanged (8, 48 and 32 bytes), so saves equal the zero-padding case and the legacy pins hold.
- Guards: `tests/Sv.Tests/RawLayoutTests.cs` (StatEntry, KeyframeRecord, ModRecord) and `tests/Sv.Laws.Tests/RawLayoutLaws.cs` (v3: Effect, LoggedRecord, StatOp, JournalEntry) assert the field sizes add up to the struct size, so a new implicit gap fails the build's tests.
- v3 audit: every v3 struct that is hashed, logged or journaled as raw bytes has no padding (byte-sized enums, exact fill); `EventChange` and `OutwardEvent` have trailing padding but never reach a hash or a save.

## Receipts

- `taskset -c 0,1` legacy `Sv.Tests`: 8 of 8 runs 507/507 (default), 3 of 3 each under AVX2=0 and HWIntrinsic=0, 2 of 2 under SV_CHECKED (509/509). Before: 3-5 failures per run.
- `eng/unity-sync.sh` now accepts a relative destination (`check packages/com.sv.core` failed on a relative path before).
