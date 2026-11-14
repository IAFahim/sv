# Code brief for agents

Standing text. The last section is the only part that changes per task.

## Who reads what you write

You are writing production C# for a family of deterministic simulation libraries (sv, tl and their Unity packages). The owner reads your code directly, top to bottom, and then hands it to other agents to build on. He judges it by reading. So the code has to explain the domain by itself: these repositories have no comments, and a file that needs one is a file whose names or structure are not finished.

One source runs in two places: .NET 10 on servers and tools, and Unity 7 under Burst. It simulates up to a million entities per frame and must produce the same bits on every machine, worker count and SIMD width. Most rules below exist because of that sentence.

Deliver the complete solution to the stated goal, including what a careful engineer adds without being asked: the edge cases, the test for each law, the callers that must change with an API. Do not widen the goal itself.

## What good code means here

**Data is plain; behaviour is separate.** State lives in unmanaged structs and flat arrays with a stated layout. Behaviour is static functions that take that data as arguments. Such data can be copied, saved, hashed and handed to Burst without translation, and a function with no hidden receiver is tested by calling it.

**A function's result depends only on its arguments.** No static that changes, no clock, no random source without a seed argument, no iteration over a hash-ordered container. A kernel may write into memory the caller passes in. When it does, the signature shows it (a pointer, `ref` or `Span` parameter) and it writes nothing else. "Immutable by default" is about values and API boundaries. It does not mean copying a million rows to avoid an in-place write.

**Every function is total.** Each possible input has a defined result. Out-of-range values are masked, saturated or clamped by a rule the name states (`Saturate`, `BoundOf`), or the function returns a small fault enum. Exceptions are for programmer errors at cold boundaries (setup, load), never for data on a hot path. Where a type can make a bad state impossible (a `StatId` struct instead of an `int`), use the type.

**Few primitives, each with a law.** Prefer a small set of operations that combine. For each, be able to state its law: this sum is commutative and associative, this pass is idempotent, this function depends only on the multiset of its inputs. Write the law as a property test, not as prose. If you cannot name the law of an abstraction, it is probably two things, or nothing.

**Order comes from data, never from timing.** Whatever must be ordered is ordered by a key built from ids in the data, never by thread, arrival, address or chunk position. Sums across entities are integers. Float operations happen in one fixed order inside one row.

**Names carry the meaning.** A name states the role in the domain (`totalPushThisTick`, not `total`). One concept has one name everywhere. A file reads in the order the code runs: inputs, steps, output.

**Deleting a piece shrinks the system.** No interface with one implementation, no option nobody asked for, no layer that only forwards. An abstraction stays when it removes a repeated decision or enforces an invariant.

**Declarative at the surface, plain inside.** The API and the data say what. The loop inside may be an ordinary imperative loop over a flat array; that is the form the machine and the reader both follow.

## Language features: the plainest thing that buys the property

You know the language. Do not demonstrate it. Use an advanced feature only when you can name what it buys (no allocation, a layout guarantee, compile-time specialisation) and the target can compile it.

| Need | Use | Where it compiles |
|---|---|---|
| specialise a loop by strategy, no virtual call | generic method over `where T : struct, IStrategy`, called through the instance | everywhere (C# 9, Burst) |
| the same, nicer | `static abstract` interface members | .NET only; never in a Unity-visible file |
| view memory without copying | pointers in the core; `Span<T>` at .NET edges | pointers everywhere; no `Span` as a job field |
| scratch memory | `stackalloc` for small fixed sizes; caller-provided buffers otherwise | everywhere |
| fixed binary layout | `[StructLayout(LayoutKind.Sequential)]` or `Explicit`, with `Size` | everywhere; never `Auto`, never `record struct` for stored data |
| code per content item | generation at build time that emits C# 9 | everywhere |
| a callback on a hot path | a struct strategy; `delegate* unmanaged` only as a .NET front door | per the contract |
| bit operations | one `Bits` helper that forwards to `BitOperations` under `#if NET` | everywhere |

Not on any simulation path: LINQ, lambdas and closures, `async`, reflection, `dynamic`, exceptions as control flow, `record` types for hot data, `Vector<T>`, any fused multiply-add, platform math functions (`MathF.Sin`, `Exp`, `Pow`; use tables or polynomials the repository owns), iteration over a `Dictionary`, a raw `float` to `int` cast. Tests and tools may use anything.

Unity-visible sources are C# 9 on `netstandard2.1`: no file-scoped namespaces, no `record struct`, no `required`, no collection expressions, no raw strings, no generic math. .NET-only code sits behind `#if NET` as a front door over the same core.

## SIMD: the rules that decide correctness

1. **The scalar function is the law.** Write it first, make it obviously right, keep it. Every wider path equals it bit for bit, and a test proves that on edge values (min, -1, 0, 1, max, NaN, infinities) and on random data.
2. **One lane is one entity.** Vectorise across the rows of a column. Lane i of the output depends only on lane i of the inputs. No horizontal float sums, no shuffles that mix rows. Horizontal integer sums are fine; they are exact.
3. **Branches become masks.** Compare, then mask, then select. Clamp with compare and select, not float `Min`/`Max`: their NaN and signed-zero results differ between instruction sets.
4. **Fixed widths.** `Vector128<T>` and `Vector256<T>`, chosen once outside the loop. Not `Vector<T>`; its width changes with the machine.
5. **The tail runs the same arithmetic.** Pad the data to the widest vector, or finish with the scalar law. A tail that computes differently from the body is a determinism bug.
6. **No fused multiply-add, no estimates.** Nothing named `Fma` or `FusedMultiplyAdd`, and no API documented as platform-dependent (names ending in `Estimate` or `Native`; check the current docs for the list). One IEEE operation per step.
7. **One float-to-int conversion.** The repository's pinned function (`Clamp.BoundOf` in sv): NaN gives 0, out of range saturates, otherwise truncate. Raw casts differ between x64 and ARM.
8. **In Unity, write the loop and let Burst vectorise it.** Plain branch-free loops under `[BurstCompile(FloatMode = FloatMode.Strict)]`. No `System.Runtime.Intrinsics` in a Unity-visible file.
9. **Count bytes before widening.** At a million rows most kernels are limited by memory bandwidth, not arithmetic. Touching fewer bytes (skip unchanged chunks, one pass instead of two, narrower columns) beats a wider vector. Say which of the two your change is.
10. **Prove it in the machine code, when you can run things:** `DOTNET_JitDisasm` or the Burst Inspector; the forced legs `DOTNET_EnableAVX2=0` and `DOTNET_EnableHWIntrinsic=0`, each asserting the flags it sees first; a search for `vfmadd` with a known positive control.

## When you cannot run anything

Some tasks say: open nothing, run nothing, answer in one pass. The risk then is code that looks finished and does not compile. So:

- Write whole files, in dependency order, each complete. No fragments, no "rest unchanged", no stubs.
- Use only APIs you are sure exist on the stated target. Where you rely on memory for an outside library (Unity, Burst, Entities, tl, a test library), keep the call and list it under Unverified.
- Compile it in your head before you finish. Every call matches a signature defined in your answer or in the provided sources. Check precedence around `&`, `|`, `==` and `?:`. Check overflow in every multiplication and shift. Check that nothing exceeds the target's language version.
- Do not say it works. Say it was not compiled, and give the commands that would prove or break it.

## What to hand back

1. Three to six lines: what you built and each decision that matters, with its reason.
2. The files, each with its path, complete.
3. The public surface: every public type and function, one line each.
4. The laws: each property the code is meant to satisfy, and the test that checks it.
5. Unverified: every API, number or assumption you could not check. A number that was not measured is marked as an estimate.
6. The first commands to run.

If the provided context contradicts this brief or itself, say so first and state what you assumed. Do not pick silently.

## This task

```
Goal:
Build on (repository, files, existing functions to reuse):
Targets (.NET 10 / netstandard2.1 C# 9 / Unity 7 Burst):
Must not change (public API, pinned digests, existing tests):
Scale and budget (rows, records per frame, ms, bytes):
May you open files and run things: yes / no
Deliver (plan / full code / both):
```
