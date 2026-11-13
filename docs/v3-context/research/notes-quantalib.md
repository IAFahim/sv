# QuanTAlib study notes (data and speed side)

Repo: D:\GitHub\_work\research\QuanTAlib (commit c6ffb9f4, 2026-09-27). Read-only study; nothing was built, run, or benchmarked.
Root path used below: `L = D:\GitHub\_work\research\QuanTAlib\lib`.

Scope honesty: I read in full: `core/ringbuffer/RingBuffer.cs`, `core/simd/SimdExtensions.cs`, `core/AbstractBase.cs`, `core/ConfirmOnClose.cs`, `core/CompareOp.cs`, `trends_FIR/sma/Sma.cs`, `trends_IIR/ema/Ema.cs`, `docs/architecture.md`, `docs/benchmarks.md`, `perf/Benchmark.cs` (head and EMA section). Skimmed: `Wma.cs`, `Rsi.cs`, `Bop.cs`, `Relu.cs`, `Sigmoid.cs`, `Jma.cs`, `Cg.cs`, `Alma.cs`, `ErrorHelpers.cs` (parts), `BiInputIndicatorBase.cs`, `MultiInputBase.cs`, `TValue/TBar/TSeries/TBarSeries`, python `Exports.Generated.cs`. About 450 indicator files exist; I did not read most. Numbers about the repo are from grep counts (exact); numbers about memory per instance are my estimates.

## 0. What QuanTAlib is (and is not) relative to our goal

It is a technical-analysis indicator library: ~447 indicators, each a class (`AbstractBase`) with `Update(TValue, bool isNew)` streaming, `static Batch(ReadOnlySpan<double>, Span<double>, ...)` span mode, `Update(TSeries)` series mode, and event chaining (`Pub +=`). It is NOT an entity state store. One instance = one indicator on one series. There is no "many entities in one contiguous block" anywhere. The reusable parts are a few micro-patterns, not the architecture. Double only (zero uses of `float`), managed collections and events everywhere, per-instance heap objects.

## 1. Memory layout

- Time series: Structure of Arrays at the batch layer. `lib/core/types/tseries/tseries.cs`: `protected readonly List<long> _t; protected readonly List<double> _v;`. `TBarSeries` = six parallel `List<>` (t,o,h,l,c,v). Exposed to SIMD via `CollectionsMarshal.AsSpan(_v)` (`Values` property). `docs/architecture.md` states the SoA rationale (cache lines full of one field).
- Single values are AoS structs: `TValue(long Time, double Value)` 16 bytes, `TBar` 48 bytes, both `readonly record struct` with `[StructLayout(LayoutKind.Auto)]` (CLR may reorder fields; NOT a stable binary layout, bad for blit save/load; we need `Sequential`/`Explicit`).
- Batch kernels take separate spans per field: `Bop.Batch(open, high, low, close, destination)` (SoA, one `Vector` load per field per iteration). This is the right shape for "SIMD across entities".
- Per-instance streaming state: small `record struct State(...)` held twice, `_state` and `_p_state` (current and previous). Sma: `State(double Sum, double Compensation, double LastValidValue)` = 24 B x 2. Ema: `State(double Ema, double E, bool IsHot, bool IsCompensated)` x 2 plus `_lastValidValue` x 2. Wma: `State(7 fields)` x 2 (about 56 B each). So scalar state is a few doubles, copied by plain struct assignment.
- Ring buffer: `lib/core/ringbuffer/RingBuffer.cs`, a sealed CLASS over `double[] _buffer = GC.AllocateArray<double>(capacity, pinned: true)`, fields `_head,_count,_sum` + single-slot `_savedHead,_savedCount,_savedSum,_savedValue`. Wrap uses `%` (modulo, not mask). So each Sma instance is at least 4 heap objects: indicator, RingBuffer, pinned array, handler delegate (+ Name string). My estimate: ~300+ bytes per Sma(14), not the "40-60 bytes per indicator instance" claimed in `docs/architecture.md`. At 1M entities this layout (object per thing, pointer chasing) is exactly what we must avoid.
- Alignment: no explicit alignment anywhere (`grep NativeMemory|AlignedAlloc` = 0 hits). `pinned: true` on the POH is only about not moving; it gives no 32/64-byte alignment (my knowledge of .NET, not verified here). The doc comment "Pinned memory for SIMD compatibility" is misleading. All loads are unaligned (`Vector256.LoadUnsafe`, `new Vector<T>(span)`), which is fine on modern CPUs but cache-line splits are possible.
- Only large-state indicator I found: `Jma.cs` has three pinned 128-element arrays (1,536 B) and instead of copying them for rollback it keeps an undo journal (see 3).
- Python/FFI layer (`python/src/Exports.cs`): `private static ReadOnlySpan<double> Src(double* p, int n) => new(p, n);` zero-copy views over native buffers. Good model for exposing our contiguous chunk to other languages.

## 2. SIMD approach

Counts (non-test .cs under lib): ~476 files; only 26 reference Vector/Avx/NEON (RingBuffer, SimdExtensions, ErrorHelpers, Sma, Wma, Alma, Hma, Rsi, Cmo, Bop, Ad, Cmf, Variance, StdDev, Covariance, Correl, Skew, Kurtosis, Jb, Slope, Accel, Jerk, Lineartrans, Relu, Sigmoid, Aberr). Roughly 5%. The "447 vectorized indicators" impression is wrong: most are scalar loops. `Math.FusedMultiplyAdd` appears 1,536 times though, mostly scalar.

Three styles coexist:
1. Portable `Vector<double>` (the majority): guard `if (Vector.IsHardwareAccelerated && len >= Vector<double>.Count)`, loop `for (; i <= len - vectorSize; i += vectorSize)`, then a scalar `for (; i < len; i++)` tail. Width is whatever the runtime picks (2/4/8 lanes), which matters for determinism (section 5).
2. Hand-dispatched intrinsics for a few hot kernels. Dispatch order (Sma.Batch, Wma.Batch, DotProduct): `Avx512F.IsSupported` -> `Avx2.IsSupported` -> `AdvSimd.Arm64.IsSupported` -> scalar/`Vector<T>`. Preconditions in Sma/Wma: `len >= 256 && !source.ContainsNonFinite()` (a full extra SIMD pass to check NaN/Inf), else scalar path with NaN substitution.
3. Fake vectorization: `Sigmoid.CalculateVectorCore` loads a vector, then loops lanes calling `Math.Exp` scalar into a `stackalloc` buffer and reloads it. No transcendental SIMD at all. `Divide` in SimdExtensions is explicitly scalar ("no branch-free SIMD zero-guard worth the complexity").

Tails/remainders:
- Plain scalar tail loop after the vector loop (SimdExtensions all methods, RingBuffer.SumSpanSimd).
- Sma/Wma: `simdEnd = period + ((len - period) / VectorWidth * VectorWidth)` then scalar tail with plain `sum += newVal - oldVal` (non-Kahan, so the tail rounds differently from the vector body and from the scalar-path Kahan version).
- No masked loads/stores, no padding of arrays to a multiple of the vector width. Padding the chunk to a multiple of 8 and skipping tails would be strictly better for us.

Branch-free tricks found (all small):
- `Vector.ConditionalSelect(mask, a, b)` after `Vector.GreaterThan/LessThan`: `Bop.Batch` (`mask = range > epsilon`; `div = body/range` computed for all lanes then masked to 0, relying on no FP traps), `Rsi.Batch` (two nested ConditionalSelect for loss==0 and flat -> 50).
- `Avx.BlendVariable(lastValidVec, relu, isFiniteMask)` in `Relu.cs`, `Avx.Compare(x, x, OrderedEqualNonSignaling)` / `MoveMask` for NaN detection in `ErrorHelpers` (but NaN handling there is "bail out to scalar at first NaN chunk", not blend).
- `Vector.Max(change, 0)` and `Vector.Max(-change, 0)` for gain/loss split (Rsi) instead of branches.
- NaN check `Vector.Equals(v, v)` plus `Abs(v) > double.MaxValue` for infinity (SimdExtensions.ContainsNonFinite).
- Vectorizing a serial recurrence: SMA/WMA batch use a within-vector prefix scan of deltas (Hillis-Steele log-step: AVX-512 uses `PermuteVar8x64` + multiply by 0/1 mask constants; AVX2 uses `Permute4x64` + `Blend`; NEON does it scalar) with the running sum carried between vectors by broadcasting the last lane (`vSumState = Vector512.Create(vSums.GetElement(7))`). Clever but only needed because one series is the unit of work. In an SoA entity store the parallelism is across entities and none of this is needed. Also the mask-multiply trick is NaN-unsafe (NaN*0 = NaN), which is why they pre-scan for non-finite.
- Dot product (`SimdExtensions.DotProduct`): 4 independent accumulators, 4x unrolled (32 doubles per iteration for AVX-512, 16 for AVX2, 8 for NEON), `Avx512F.FusedMultiplyAdd` / `Fma.MultiplyAdd`, `bool useFma = Fma.IsSupported` hoisted out of the loop with duplicated loop bodies, accumulators merged pairwise, horizontal sum via `GetLower/GetUpper` + `Sse2.Add`, scalar tail with `sum += a*b` (non-FMA). Small-n fast path `len <= 3`.
- Lazy non-finite check in `SumSIMD`: compute the sum, only scan for NaN if the result is non-finite.

FMA use: ~1.5k `Math.FusedMultiplyAdd` calls, e.g. EMA `state.Ema = Math.FusedMultiplyAdd(state.Ema, decay, alpha * input)`, RingBuffer `_sum = Math.FusedMultiplyAdd(-1.0, removed, _sum + value)` (an FMA used as a subtraction, no benefit, odd). `Math.FusedMultiplyAdd` is correctly rounded everywhere (hardware FMA or exact software fallback), so the scalar usage is deterministic in value but has a perf cliff without FMA hardware (my knowledge). Explicit `Fma.MultiplyAdd` in AVX2 paths is the same single-rounding result per lane. The deterministic problem is which algorithm/width runs, see 5.

## 3. Streaming vs batch and isNew (pending/confirmed)

Modes (docs/architecture.md): Span (stateless static `Batch`), Batch (TSeries), Streaming (`Update(TValue, isNew)`), Eventing (`Pub`). Priming: `Prime(span)` rebuilds streaming state from history (Sma only replays the last `period` values).

isNew protocol, the canonical form (Ema.cs):
```csharp
if (isNew) { _p_state = _state; _p_lastValidValue = _lastValidValue; }
else       { _state = _p_state; _lastValidValue = _p_lastValidValue; }
double val = GetValidValue(input.Value);
val = Compute(val, _alpha, _decay, ref _state);
```
Meaning: on a new bar, save the current state as "previous"; on a correction (isNew=false) restore the saved previous state, then recompute from the new input. Any number of corrections of the same bar are fine because `_p_state` is only overwritten on the next `isNew=true`. `ConfirmOnClose.cs` is the same idea one level up: `State(TValue Pending, bool HasPending, bool HasConfirmed)`, publishes the pending value only when the next bar opens (always one bar behind).

How windowed state is rolled back:
- Sma: `UpdateNewest` in RingBuffer replaces the newest slot and patches `_sum` differentially (`FMA(-1, old, sum+new)`); then `_state = restoredState with { Sum = _buffer.Sum }`.
- RingBuffer has ONE saved slot (`Snapshot()` before Add saves head/count/sum and the single value about to be overwritten; `Restore()` puts them back). Used by Cg, Jbands, BiInputIndicatorBase etc. (85 call sites). Depth is exactly 1 tick.
- Jma (big state): an "undo journal" (`_undoOldC, _undoOldS, _undoNewS, _undoOldVal, _undoCoreSum, ...`) records only the few cells touched by one update, and `UndoVolatility()` reverts them. That is a delta-based rollback of a 1.5 KB structure instead of a copy.
- Inconsistency: Wma snapshots AFTER an isNew update (`_pState = _state`) and patches with `UpdateNewest`; Sma snapshots BEFORE; Cg snapshots the ring buffer. Each indicator hand-rolls its own variant (no shared mechanism), which is why bugs are possible.

Does it generalize to rewind? Only the principle: "state = small value struct; keep previous copy; revert = struct assignment". It is depth-1 and per-indicator. For our rollback (N ticks, whole world) it generalizes as: (a) flat blittable state, so a snapshot is a memcpy of the chunk; (b) ring of snapshots or a per-tick delta journal like Jma's. QuanTAlib offers no multi-step history store, no serialization, no checksum.

Bit-level caveat (code inference, not run): Sma isNew=true uses the Kahan-compensated `_state.Sum`, while isNew=false sets `_state.Sum = _buffer.Sum` (the ring buffer's non-Kahan FMA running sum). So "tick X then correct to Y" is not bitwise equal to "tick Y directly". Their test `Sma_IterativeCorrections_RestoreToOriginalState` asserts `Assert.Equal(expected, actual, 1e-10)`, i.e. tolerance, never bit equality. For rollback determinism we need revert paths to be bit-identical, so this pattern would hurt.

Batch vs streaming parity: not exact either. `Ema.Batch(span)` for len >= 256 and NaN-free uses `CalculateCleanCore`, which seeds ema with the first value and has no bias compensation, whereas streaming `Compute` uses bias compensation `state.Ema / (1.0 - state.E)`. Different algorithms, so differing values (tests use tolerances / validate against Skender etc).

## 4. Generators and templating

None in the library. Greps: no `.tt/.t4/.scriban`, no `IIncrementalGenerator`/`ISourceGenerator`/Roslyn references, no `[Generator]`. The only generated code is `python/src/Exports.Generated.cs` (3,813 lines, header `// <auto-generated />`), one `[UnmanagedCallersOnly(EntryPoint = "qtl_abber")]` wrapper per indicator with null/length checks and try/catch -> status codes. The generator script is not in the repo (I searched .py/.ps1/.cs; not found), so I only see its output. Code reuse at compile time is done by C# generics instead: `ComparePredicateBase<TOp> where TOp : struct, ICompareOp` with `static abstract bool Eval(...)` and zero-field structs (`AboveOp`, `BelowOp`, `EqualOp`) so the JIT specializes per operator with no virtual call; the net48 branch uses `default(TOp).Eval(...)`. Relevant to us: static abstract interface members need C# 11, not available in Unity C# 9; the `default(TOp).Eval` instance form works in C# 9 and Burst-style generic struct specialization. Boolean results are carried as 1.0/0.0 doubles (predicates are averaged by `BiInputIndicatorBase` with period 1). Nothing here compiles rules from a description.

## 5. Determinism

Not a goal of the library. Evidence:
- `Directory.Build.props` sets `<Deterministic>true</Deterministic>`, which only means reproducible compiler output, not numeric determinism. Tests compare with `1e-10` tolerance and against Skender/TA-Lib/Tulip; no bit-identical test, no cross-ISA test, no forced-ISA test (`grep DOTNET_EnableAVX|COMPlus|PreferredVectorBitWidth` = nothing). They test internal scalar vs vector cores directly (internals visible to the test assembly) but with tolerance.
- Sources of cross-machine bit differences in the code:
  1. `Vector<double>.Count` varies (2/4/8) and horizontal reductions run in lane order (`for j<vectorSize: result += sum[j]`), so `SumSIMD`, `VarianceSIMD`, Alma, Wma vector cores give different bits on different widths. (On .NET 8+ `Vector<T>` stays 256-bit on AVX-512 hardware unless configured: my knowledge, not verified.)
  2. ISA-branching algorithms: Sma/Wma Batch pick AVX-512 (tree prefix), AVX2 (tree prefix, `Fma.MultiplyAdd(vSums, vInvPeriod, vZero)`), NEON (sequential), else scalar Kahan: four different rounding orders, plus `len >= 256` and NaN-presence switching between them. The same input gives different bits depending on length, NaN presence and CPU.
  3. `DotProduct`: AVX-512 (8 lanes x 4 accumulators), AVX2 (4 lanes x 4, FMA or non-FMA depending on `Fma.IsSupported`), NEON, scalar (4 FMA accumulators) = different summation trees; the tail is non-FMA. 
  4. Transcendentals: 49 files call `Math.Exp`, 44 `Math.Log`, 26 `Math.Sin`, 55 `Math.Cos`, 12 `Math.Pow`, 11 `Math.Atan`. These go through the platform math library in .NET and are not guaranteed bit-identical across OS/CPU (my knowledge). Fatal for lockstep multiplayer; we must write our own polynomial/table versions.
  5. Kahan compensated summation is used in Sma/Wma (`y = delta - c; t = sum + y; c = (t - sum) - y`); safe under RyuJIT (no reassociation), but under Burst fast-math or any reassociating compiler it can be optimized away (Burst's default FloatMode I believe is not reassociating, to verify before relying on it).
- What is good: no `float`/`decimal` mixing, no `Parallel`/threads in indicator code (7 files mention Parallel/lock/Interlocked, all outside the hot indicators I looked at), the GBM feed is seeded, `Math.FusedMultiplyAdd` is exact in value everywhere. RyuJIT does not auto-contract `a*b+c` into FMA (my knowledge), so scalar code is stable; the risk is only explicit ISA-specific paths.

## 6. Benchmarks (skeptical reading)

Setup (`perf/Benchmark.cs`, `perf/perf.csproj`, `docs/benchmarks.md`): BenchmarkDotNet 0.15.3, `Job.ShortRun` (few iterations, wide variance), net10 JIT, one machine with AVX-512 (F+CD+BW+DQ+VL+VBMI per the doc), 500,000 bars from a seeded GBM, period 220, input in a pre-filled `double[]` (4 MB), output reused (`_quantalibOutput`), data NaN-free. A second harness `Progressive.cs` runs 1M bars with period 10..5000. Headline: SMA span 287.9 us for 500k = 0.58 ns/value, EMA 423.8 us = 0.85 ns, WMA 294.7 us, claims 1.2-1.7x faster than TA-Lib.

What it really covers / concerns:
- The measured path is the best case: contiguous NaN-free spans, AVX-512 box, inputs hot in L2/L3 (4 MB in + 4 MB out), no per-element object work. The SMA number is an O(1)-per-element prefix-scan kernel at roughly memory/L2 speed: plausible, not magic. "Faster than an L1 access" is marketing.
- EMA is a serial FMA dependency chain (`CalculateCleanCore`, 4x unrolled but still one chain); 0.85 ns is about FMA latency (~4 cycles at ~4.5-5 GHz). So it is at the latency floor, which makes the number believable, but it also means the doc's explanation "SIMD + FMA explains the speedup" is wrong for EMA (no SIMD there). And the EMA batch benchmark takes the "clean" path that skips bias compensation, so it is not computing the same thing as streaming QuanTAlib or TA-Lib (TA-Lib seeds EMA differently); comparison is not apples to apples.
- "TA-Lib (C library via P/Invoke)" per the docs, but `perf.csproj` references `TALib.NETCore 0.5.0` which (to my knowledge, not verified) is a managed C# port, not the native C library.
- ShortRun + a single machine + no cross-hardware data: "relative ratios hold across hardware" is asserted, not shown.
- `Sma_SpanBatch_ZeroAllocation` unit test does not measure allocation ("allocation is measured by BenchmarkDotNet, not unit tests"); zero alloc comes from `MemoryDiagnoser` in the benchmark only for the Span API. Streaming allocates 176 B per run (the instance); Batch(TSeries) allocates 16 MB; Eventing 16.8 MB.
- Streaming: 1,536 us / 500k = ~3 ns per `Update` for EMA with no corrections (`isNew=false` never benchmarked), no event subscribers. That is the one number relevant to "per-entity scalar state update" and it is decent, but it includes the 16-byte `TValue` return and `Pub?.Invoke` null check.
- `docs/architecture.md` tables (SoA vs AoS 7-8x; "0.4 us per bar RSI"; "40-60 bytes per instance") have no benchmark in the repo behind them (searched for the figures; only the doc contains them). Treat as unverified illustrations. `docs/benchmarks.md` Python tables likewise not reproduced.
- Nothing measures rollback, save/load, or 1M independent entities.

## 7. What to copy, and what to avoid

Copy (ideas, with adaptation):
1. Two-slot state with `isNew` flag: `if (isNew) prev = cur; else cur = prev;` as the minimal revise-without-advancing idiom; but in our design replace with whole-chunk snapshot ring (memcpy) or delta journal (Jma pattern: record only touched cells, undo in reverse) so revert is bit-identical.
2. SoA spans + a `Span<double>`-in/`Span<double>`-out kernel signature with no allocation; pointer-to-span zero-copy bridge (`new Span<double>(p, n)`) for FFI/Unity native arrays.
3. Branch-free select: `ConditionalSelect(mask, valueIfTrue, valueIfFalse)` after a comparison mask (Bop, Rsi) and compute-both-then-mask for guarded division. Use `Vector.Max/Min` for clamps and sign splits.
4. Hoist ISA/FMA checks outside loops and duplicate loop bodies (DotProduct's `useFma`), 4 independent accumulators to hide FMA latency, merge pairwise at the end.
5. Lazy NaN check (compute first, validate result) rather than a pre-pass.
6. Zero-field generic struct "operators" for compile-time specialization (`ICompareOp`), using the instance-method form `default(TOp).Eval` for C# 9/Burst.
7. Undo journal for large-state revert (Jma) as a model for per-tick deltas in a rollback log.
8. `[SkipLocalsInit]` on hot classes and `ArrayPool`/`stackalloc` threshold (`StackAllocThreshold = 256` doubles) for scratch buffers.

Avoid / would hurt us:
- Class-per-instance with `RingBuffer` class, pinned array, delegates, events (`Pub`), `Name` strings, `List<double>`: heap allocation per entity, pointer chasing, GC, not blittable, not savable in one memcpy.
- `[StructLayout(LayoutKind.Auto)]` everywhere (layout unspecified), `record struct` equality/ToString overhead; need `Sequential`/`Explicit` with fixed offsets.
- `double` only (we want float stats and int intrinsics); `Vector<T>` width variability; hardware-dependent algorithm selection and length-dependent switching; platform `Math.Exp/Log/Sin/Cos/Pow` (49/44/26/55/12 files).
- `%` for ring wrap (use power-of-two mask), `record struct` `with`-copies, event dispatch inside the update.
- Vectorizing within one series via prefix scan; we vectorize across entities instead (one lane = one entity, state in SoA columns).
- Tail loops with different arithmetic than the vector body (Sma tail uses non-Kahan add): in our library every lane, including remainder, must run the identical operation sequence; pad to the vector width instead.
- `Math.FusedMultiplyAdd` used for plain subtraction (RingBuffer), mixing FMA and non-FMA in the same reduction.
- Tolerance-based "restore" tests; we need bitwise tests (hash the chunk after rollback+replay == hash from straight-through run), run under forced ISA settings (`DOTNET_EnableAVX2=0`, `DOTNET_EnableAVX512F=0`, `DOTNET_PreferredVectorBitWidth`) and ideally fixed-width `Vector128/256`-only code or explicitly width-independent reduction order.

## Not verified / open

- Never executed any benchmark or test; all performance numbers are the repo authors' claims.
- Did not inspect JIT assembly, so which `Vector<T>` calls actually become FMA/blend, and the real Vector<T> width on AVX-512, is from memory of .NET behavior.
- Did not read the remaining ~430 indicators; patterns generalised from ~15. Some may use better or worse conventions (e.g. dedicated SIMD for Hma, Variance, Correl not read in detail).
- Did not find the generator for `Exports.Generated.cs`; may live outside the repo.
- Did not verify TALib.NETCore is managed; did not verify Burst FloatMode behavior; Burst compatibility of the repo code was not checked (it uses `List<T>`, delegates, events, strings, `ArrayPool`, none of which Burst accepts).
