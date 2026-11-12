# 0117 - V0-a census, V1/V5 portability: src/Sv on net10.0 + netstandard2.1 at C# 9

Branch `proto/b-portable` off sv main `a0963c8`. Agent B. Plan `plan-sv.md` SV-Q4, steps V0-a, V1, V5.

## Baseline (before any change, this Windows host)

`dotnet test sv.slnx -c Release -p:NuGetAudit=false`: Sv.Tests 446/446, Sv.Unity.Tests 13/13,
Sv.NorthStar 27/27. All 446 green here (not 442/446: the 4 path-artefact failures did not
reproduce in this worktree).

## Census (V0-a)

Method: copy of `src/Sv/*.cs` built as `netstandard2.1`, `LangVersion 9`, nullable on,
warnings as errors. The compiler stops binding bodies while declaration errors exist, so the
census ran in passes, removing each blocker class in the copy before the next build.

Pass 1, syntax and declarations: 140 errors.

| Blocker | Errors | Files |
|---|---:|---|
| file-scoped namespace | 30 | every file but XxHash64 (31 with it after pass 1) |
| `System.Runtime.Intrinsics` | 87 | Simd.cs, Simd.Chunk.cs |
| `record struct`, `record`, `init` (`IsExternalInit`) | 10 | Schema.cs |
| `delegate* unmanaged` (CS8889) | 8 | EventStreams.cs |
| primary constructors | 3 | Schema.Attributes.cs |
| `static abstract` | 2 | StateCodec.cs |

Pass 2, API surface (namespaces fixed, Simd files dropped, codec and pointers patched): 188 errors.

| Blocker | Errors | Files |
|---|---:|---|
| `NativeMemory` | 87 | BlockTable, Board.Keyframe, Board.Stats, Columns, Drift, EventStreams, InputState, NativeArena, SetTable, SvState, UndoLog |
| `TCodec.Reading` (static abstract use) | 27 | BlockTable, Board, Board.Stats (16), EventStreams, InputState, SetTable |
| `BitOperations` | 27 | Board, Board.Keyframe, Board.Stats, Drift, EventStreams, SetTable, XxHash64 |
| `BitConverter.SingleToUInt32Bits/UInt32BitsToSingle` | 17 | Board.Keyframe, Board.Stats, InputState, Schema, SetTable |
| `Simd` dispatch (vector-only file) | 16 | Board, EventStreams, InputState, Kernel |
| `Unsafe.AsPointer/ReadUnaligned` | 5 | StateCodec, XxHash64 |
| `ObjectDisposedException.ThrowIf` | 4 | Drift |
| `Environment.TickCount64` | 2 | Bulk |
| `Interlocked/Volatile` over `nint` | 2 | EventStreams |
| `OperatingSystem.IsLinux` | 1 | NativeArena |

No hidden C# 10-12 syntax beyond these (no raw strings, collection expressions, list patterns,
`required`). No pass 3 errors: after the fixes below both targets build with 0 errors, 0 warnings.

## Fixes (at source, one source, no fork)

- `Sv.csproj`: `TargetFrameworks net10.0;netstandard2.1`, `LangVersion 9`, warnings stay errors.
- Block namespaces everywhere. `Kernel.cs`, `Simd.Chunk.cs`, `Board.Stats.cs` keep body
  indentation (2-line diff) so agent A's fold work merges; a whitespace-only reindent can follow.
- New `Bits.cs`: `Tzcnt`, `Lzcnt`, `PopCount`, `RotateLeft`, `OfSingle`, `ToSingle`,
  `ReadUnaligned64/32`; BCL under `#if NET`, compare sequences / pointer casts / `Buffer.MemoryCopy` else.
- New `Memory.cs`: `Alloc`, `AllocZeroed`, `Realloc`, `Free`, `AllocAligned`, `FreeAligned`,
  `Clear`, `Copy`; `NativeMemory` under `#if NET`, else `Marshal.AllocHGlobal` + manual clear +
  pointer-stash alignment (64 B for large arena blocks; 2 MiB + `madvise` stays .NET only).
- New `StatMath.cs`: the only stat float math (fold, derived multiply, curve ceiling, curve
  bucket); every add, subtract, multiply, divide is an explicit `(float)` cast (IL `conv.r4`),
  per agent C's Mono finding (5.9 % of 1M sets off by 1-2 ulp). .NET bits unchanged.
- `IStateCodec.Reading` is an instance property; `fixed` replaces `Unsafe.AsPointer`.
- Struct makers are the portable core; the 6 function-pointer emitters, `PointerMaker`,
  `WalkNonZero` sit behind `#if NET`. Event locks are `long`.
- `Simd.cs` holds the dispatch and scalar twins on both targets; vector paths `#if NET`.
  `ValidBits`, `EnabledWord`, `EnabledWordScalar` moved there from `Simd.Chunk.cs`;
  `Simd.Chunk.cs` and `Bulk.cs` are wrapped whole in `#if NET`.
- Schema records and attribute primary constructors are plain types with constructors
  (`StatDef.WithSources` replaces `with`). `Guard.NotDisposed` replaces `ThrowIf`.

Note for agent A: `Simd.Chunk.cs` is .NET-only now. Put the scalar `Fold.Word` in `Fold.cs`
(portable) and only the 128/256 strategies in `Simd.Chunk.cs`, or Unity loses the fold.

## Receipts

- `dotnet build src/Sv -c Release`: net10.0 and netstandard2.1, 0 warnings, 0 errors.
- `dotnet test sv.slnx -c Release -p:NuGetAudit=false`: Sv.Tests 454/454 (446 existing
  unchanged + 8 new), Sv.Portable.Tests 338/338, Sv.Unity.Tests 13/13, Sv.NorthStar 27/27.
- `sh eng/isa-legs.sh`: each leg asserts its flags first, then the pinned digests.

| Leg | Setting | Flags observed | A2ACAA69E55F79D8 | 2B43BC1549AAE751 |
|---|---|---|---|---|
| default | none | Vector256 on | equal | equal |
| no-AVX2 | `DOTNET_EnableAVX2=0` | 256 off, 128 on | equal | equal |
| scalar | `DOTNET_EnableHWIntrinsic=0` | 128 off | equal | equal |
| ns2.1 | `Sv.Portable.Tests` on the netstandard2.1 asset | asset `.NETStandard,Version=v2.1`, `Simd.Has128` false | equal (struct maker) | equal |

- ns2.1 leg: links every Sv.Tests file that uses only the portable surface; excluded are the
  Compose/Frent/generator suites (other projects) and the files that call `Bulk`, the vector
  `Simd` entry points or function-pointer emitters. `LargeColumns_AreHugePageAligned` is
  filtered by `portable.runsettings` (2 MiB alignment is a .NET-only host optimisation).
- `StatMathTests`: 1M random sets and 1M derived/curve inputs bit-equal `StrictFloat`; IL
  `conv.r4` guard on the four primitives.
- FMA grep on src/Sv: 0. New production files carry no comments.

## Unity layout (for agent C; packages/com.sv.unity untouched)

Decision: Unity consumes src/Sv as an embedded package `com.sv.core`, synced verbatim by
`eng/unity-sync.sh sync <project>/Packages/com.sv.core` and verified by `... check`.
- Copies every `src/Sv/*.cs` unchanged into `Runtime/`; .NET-only files compile empty there
  because Unity compiles against the netstandard 2.1 ref (confirmed in C's 7000.0.0a16 log),
  where `NET` is undefined. No copy list to maintain, no transform.
- Writes `Runtime/Sv.Core.asmdef` (unsafe on, `noEngineReferences`), `package.json`,
  `SHA256SUMS` of the sources and `SOURCE_COMMIT`. `check` fails on any drift or extra file.
- Writes `Runtime/csc.rsp` with `-nullable:enable`: with nullable off, the sources give 38
  `CS8632` warnings (measured on a netstandard2.1 build with `Nullable=disable`). If Unity
  ignores a per-asmdef `csc.rsp`, put `-nullable:enable` in the project's `Assets/csc.rsp`.
- Not a symlink and not a `file:` package pointing at src/Sv: that folder holds `bin/` and
  `obj/` (generated `.cs`) and a csproj.
- `com.sv.unity` references `Sv.Core` by asmdef name.

Not verified here: the sync output compiled inside Unity 7000.0.0a16 (agent C's leg), C's own
-define list (must not contain `NET`), Mono-player StatMath bits (C's probe binaries).
