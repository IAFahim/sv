# Fuzzing sv: guide for fuzzing agents

You are looking for an input where sv breaks one of its promises. You are **not** fixing sv. You find, shrink, prove and report. A lead fixes.

Read first, in this order: `AGENTS.md`, `README.md` chapters 2 and 8, this file. Do not start before you have read them.

## 1. Rules (break one and your work is thrown away)

1. **Never change an expected value to make a test pass.** Pinned digests, PackHashes, `expected.txt`, scenario numbers, journal files: never. If a pin disagrees with sv, that is a finding.
2. **Never edit `src/`.** No engine, compiler or host changes. You may add files under `tests/` and `eng/` for new harnesses (section 4), nothing else.
3. **Never weaken the generator to avoid a failure.** If seed 123456 fails, you do not skip it, filter it or change `PackGenerator` so it stops producing it. That seed is the finding.
4. **One finding = one issue file.** Do not bundle. Do not file a duplicate (check `issues/` first, section 6).
5. **Every claim has a receipt:** the exact command, the seed or file, the output line. "It seemed to fail" is not a finding.
6. **Do not run while someone benchmarks.** If `~/sv-proto/_work/bench.lock` exists, wait. `eng/fuzz.sh` checks this for you.
7. **No Unity.** Unity runs are the lead's (they take the Unity lock and minutes per run).
8. **Do not push to main.** Work on a branch named `fuzz/<your-name>`, commit your issue files and harnesses there, push the branch, report. Start every campaign from the latest `origin/main` (rebase your branch on it): a seed that fails only because your branch lacks a merged fix is not a finding.
9. **Stop and report** after 3 findings, or after your time budget, whichever comes first. Do not keep going with a broken tree.

## 2. What sv promises (the oracles)

A finding is any input where one of these is false.

| # | Promise | How it is checked today |
|---|---|---|
| O1 | Compiled kernels give the same values and digest as the reference interpreter, every frame | `tests/Sv.Diff.Tests` (`values`, `noabsorb`, `cardmodifiers`, `statops`, `bricks-values`) |
| O2 | Digests do not depend on worker count (1, 2, 4, 7, 11) or parts (16, 64) | `Sv.Diff.Tests` `workers`, `bricks-workers` |
| O3 | Dense storage gives the same as shared storage | `Sv.Diff.Tests` `dense` |
| O4 | Digests do not depend on the CPU's instruction set (AVX2 off, all SIMD off) | run any of the above with `DOTNET_EnableAVX2=0` or `DOTNET_EnableHWIntrinsic=0` |
| O5 | Digests do not depend on thread timing | run under `taskset -c 0,1` (2 CPUs, contended). This found a real bug (issue 0375) |
| O6 | Every generated pack is accepted and its kernels compile with 0 warnings | `Sv.Diff.Tests` `compile`, `warnings`, `bricks-compile` |
| O7 | The order you `Send` records in a frame never changes the result | **no harness yet** (section 4.1) |
| O8 | Rewind to an epoch, replay the same input: the same digests | partly in `Sv.Laws.Tests` `TimeTravelLaws`; random harness: section 4.2 |
| O9 | A malformed pack is rejected with a diagnostic: never a crash, a hang or an exception other than a pack error | **no harness yet** (section 4.3) |
| O10 | Writing a pack and reading it back gives the same `PackHash` | **no harness yet** (section 4.4) |
| O11 | Warm frames allocate 0 bytes on sv's threads | `Sv.Host.Tests` `AllocationTests` (fixed scenarios only) |

## 3. Campaigns you can run now

`eng/fuzz.sh <first-offset> <rounds> [mode]` runs the whole differential suite (O1, O2, O3, O6) on **840 new random packs per round** (300 + 300 + 40 + 40 + 160 brick packs). One round takes about 40 seconds on a 6-core machine (longer in `contended`).

```sh
bash eng/fuzz.sh 1000000 25 default
```

**Pick your own offsets.** Agent number N uses offsets `N * 10,000,000` onward, step 10,000 (the default step). Two agents must never use the same offset. Write your range at the top of your report.

**Modes:** run the same offsets in all four, in this order:

| Mode | Checks | Command |
|---|---|---|
| `default` | O1, O2, O3, O6 | `bash eng/fuzz.sh <offset> <rounds> default` |
| `noavx2` | + O4 | `bash eng/fuzz.sh <offset> <rounds> noavx2` |
| `scalar` | + O4 | `bash eng/fuzz.sh <offset> <rounds> scalar` |
| `contended` | + O5 | `bash eng/fuzz.sh <offset> <rounds> contended` |

**Reading the output:** one line per round.

```
ok   offset 1000000 (default):  Passed: 20
FAIL offset 1010000 (default):  Passed: 19 Failed: 1 -> fuzz-out/offset-1010000-default
```

On `FAIL`, the folder holds a **receipt per failing seed** (`<check>-seed<N>.txt`: what differed, then the four pack files and the scenario) and the generated C# (`seed<N>/`). The full test log is `fuzz-out/offset-<offset>-<mode>.log`. Copy the folder before you run again (each run clears the test's own receipts).

**Budget:** start with 25 rounds per mode (about 21,000 packs per mode). If all four modes are clean, report that with the offsets: a clean campaign is a result too.

## 4. New harnesses to build (after your section 3 campaign is filed)

Start these once every failure from your section 3 campaign is filed as an issue (an open issue does not block you). Rebase on main first (`git fetch origin && git rebase origin/main`) so fixes the lead already merged are in.

Each one is a new test class in a **new file** under `tests/Sv.Diff.Tests/` (it can use `PackGenerator`, `KernelCompiler`, `Runs` and `Seeds` there). Follow this shape exactly:

- seeds from `Seeds.Offset + <your base>` so `SV_FUZZ_OFFSET` moves them too;
- on a failure call `Seeds.Receipt("<check>", item, "<what differed>")` so it lands in `receipts/`;
- assert at the end with the count of checked seeds and failures, like `Verdict` in `DiffTests.cs`;
- it must pass on today's code before you use it to fuzz (if it fails at once on many seeds, your harness is probably wrong: show the lead).

### 4.1 Send order (O7)

For each generated case, run the compiled kernels twice: once with the scenario's sends as written, once with each frame's sends in a shuffled order (shuffle with `new Random(seed)`). Compare with `RunTrace.FirstDifference(first, second, true)`. Any difference is a finding.

### 4.2 Rewind and replay (O8)

Run a case for all frames, recording `LastDigest` per frame. Then, in a new host with the same options, run to frame k, call `Rewind` to an earlier epoch, replay the same inputs from there, and check every digest after the rewind equals the first run's. Use `HostOptions.FramesPerEpoch = 2` so short scenarios have several epochs. Read `FrameLoop.Rewind` and `TimeTravelLaws.cs` before writing this.

### 4.3 Malformed packs (O9)

Take a generated pack and mutate **one** thing per attempt: delete a row, duplicate a row, change a number to a negative, huge (`2147483648`), `NaN` or a string, remove a required key, rename a referenced value to a missing name, truncate the file, insert garbage bytes. Then call `PackCompiler.Compile(source)`. The only acceptable outcomes: `Ok` (the mutation was harmless) or `Diagnostics` with at least one entry. An exception that is not a pack error, a hang (use a 10-second timeout), or `Ok` for a pack that references a missing name are findings.

### 4.4 Round trip (O10)

For each generated case, `PackWriter.Write` the parsed pack, read the written text back, and compare `PackHash`. Any difference is a finding.

## 5. When something fails: triage, then shrink

**Triage (in this order, write each answer down):**

1. Rerun the same round. Does the same seed fail again? If not, run it 5 more times: a failure that comes and goes is a timing finding (O5), still a finding, and the run counts go in the report.
2. Does it fail in `default` mode too, or only in one mode? Write which modes.
3. Which check? (`values`, `workers`, `dense`, `compile`, ...). That names the broken promise.
4. Which frame first differs, and which value? The receipt's first line says it.
5. Is it the compiler or the oracle? The reference interpreter (`src/Sv.Rules/Reference`) is the oracle, but it can be wrong too. Read the contract clause for the rule involved (`contract/`, and the readings in issues 0180, 0350). If the compiled result matches the contract and the reference does not, say so: that is still a finding, against the reference.

**Shrink.** A finding is only useful when it is small. Copy the four pack files and the scenario from the receipt into a folder, then repeat until nothing more can go:

1. Delete one rule. Still fails? Keep it deleted. Fails differently or passes? Put it back.
2. Then one template, one card, one value (fix references the compiler then complains about).
3. Then cut scenario frames from the end, then sends.

Check each step with this throwaway test (put it in `tests/Sv.Diff.Tests/ShrinkRepro.cs`, point `folder` at your copy, run `dotnet test tests/Sv.Diff.Tests -c Release --filter ShrinkRepro`, and delete the file before you commit). It fails while the bug is still in the input:

```csharp
using System.IO;
using Sv;
using Xunit;

namespace DiffTests
{
    public sealed class ShrinkRepro
    {
        [Fact]
        public void Repro()
        {
            var folder = "/absolute/path/to/your/copy";
            var item = KernelCompiler.Build(new[]
            {
                new GeneratedCase
                {
                    Seed = 0,
                    Namespace = "Diff.Shrink",
                    Values = File.ReadAllText(Path.Combine(folder, "values.sv.json")),
                    Rules = File.ReadAllText(Path.Combine(folder, "rules.sv.json")),
                    Cards = File.ReadAllText(Path.Combine(folder, "cards.sv.json")),
                    Templates = File.ReadAllText(Path.Combine(folder, "templates.sv.json")),
                    Scenario = File.ReadAllText(Path.Combine(folder, "scenario.json")),
                },
            })[0];
            Assert.True(item.Ready, item.Failure);
            var difference = RunTrace.FirstDifference(Seeds.Reference(item), Seeds.Compiled(item, Seeds.Options(1, 64, StoragePolicy.Shared)), true);
            Assert.True(difference.Length == 0, difference);
        }
    }
}
```

For a `workers` or `dense` finding, compare `Seeds.Compiled(item, Seeds.Options(1, 64, StoragePolicy.Shared))` with the failing options from the receipt instead of the reference. Stop when every remaining line is needed. Aim for fewer than 5 rules and 3 frames.

## 6. Reporting

Before filing, search: `grep -ril "<the rule name or check>" issues/`. If a matching issue exists, add a "Seen again" note with your seed to it instead.

New file `issues/04NN-fuzz-<short-name>.md` (take the next free number above 0400):

```markdown
# 04NN fuzz: <one line: which promise breaks, on what>

Status: open (found by fuzzing, not fixed)

Found by: <agent name>, eng/fuzz.sh <offset> <rounds> <mode>, sv commit <git rev-parse --short HEAD>

## Promise broken
O<n>: <the promise, from docs/FUZZING.md section 2>

## Smallest input
<the shrunk pack files and scenario, in full>

## What happens
Command: <exact command>
Expected: <value or digest, and why: oracle, contract clause>
Actual: <value or digest>
First differing frame: <n>, value: <name>

## Reproducible
<n of n runs>, modes: <default / noavx2 / scalar / contended>

## Not checked
<anything you could not check>
```

**Your final report to the lead** (short, in this order): offsets and modes covered, number of packs, findings (issue numbers, one line each), harnesses added (file names, and that they pass on clean seeds), anything you could not finish.

## 7. Facts that save you time

- `SV_FUZZ_OFFSET` moves every seed range of `Sv.Diff.Tests` at once; unset, the suite runs its usual seeds (that is what CI runs).
- The kernels for generated packs are compiled in memory by `KernelCompiler`. You never need `sv pack` for fuzzing.
- `bash eng/v3-verify.sh` must still say `all green` on your branch before you push: your new tests must not break the normal run.
- Contended runs are slow and that is fine. Do not lower the round count to make them fast; lower the number of rounds per command and run more commands.
- If the build fails after you add a harness, fix your file. Warnings are errors in this repository.
