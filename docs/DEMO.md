# sv in 10 minutes: demo script

Release `v3.0.0-rc.4`. Every output below was produced on 2026-11-14 on a Linux desktop (Ryzen 5 8500G, 6 cores, 12 threads). Run the steps in this order; each says what it proves.

You need: the .NET 10 SDK, a clone of this repository, and for step 4 Unity 7000.0.0a16. Step 3 needs the `wasm-tools` workload (`docs/WEB.md`).

## 1. A pack runs and every machine agrees (1 minute)

```sh
git clone https://github.com/IAFahim/sv.git
cd sv
dotnet run -c Release --project samples/Sv.Hello
```

```
frame 1: hero health 25, knight health 85 shield 0, hero low health True, digest D0391ABCE50C62DB
frame 2: hero health 75, knight health 85 shield 0, hero low health False, digest 9078B3C66F1DD3FE
```

What it proves: four JSON files (`samples/Sv.Hello/pack`) define the game rules. The program sends hits and sv decides the outcome: the fire ward blocks fire, and the shield soaks 15. The digest is a hash of every value; the same two digests print on Linux, macOS, Windows and ARM64 (checked in CI). README chapter 1 explains every number.

## 2. A designer changes the game without code (1 minute)

In `samples/Sv.Hello/pack/templates.sv.json`, change the knight's `"Shield":15` to `"Shield":30`, then run the same command:

```
frame 1: hero health 25, knight health 100 shield 0, hero low health True, digest 09B1345163C7F85C
frame 2: hero health 75, knight health 100 shield 0, hero low health False, digest C9F0CD5AEDD9697F
```

What it proves: the build recompiles the rules into C# by itself. The shield now soaks the whole hit, and the digest changes, which is how two machines notice they disagree. Undo the edit afterwards (`git checkout samples`).

## 3. The rules in a browser, layer by layer (2 minutes)

```sh
bash eng/web.sh publish
bash eng/web.sh serve
```

Open http://127.0.0.1:8123, press **Reset samples**, then **Simulate**:

```
worked_example: passed, digest E0F445186E2C3672, PackHash BD70627B1C671888
```

Below it is the full trace of every frame: which gate denied which hit, what the thorns reflected, and how health folded. Then change any rule id in `rules.sv.json` to `-1` and press **Check**:

```
rules.sv.json(417) error SV0009: rule id -1 is negative; ids are 0 or more
```

What it proves: designers get the same compiler and the same answers in a 5.6 MB browser page as the game gets in Unity, and a bad pack is refused with a line number, before any code is built.

## 4. The same game in Unity (3 minutes, with a warm Unity)

Add the two packages to a Unity 7000.0.0a16 project's `Packages/manifest.json` (with `com.unity.entities` 7.0.0, `com.unity.burst` 2.0.0, `com.unity.collections` 7.0.0):

```json
"com.sv.engine": "https://github.com/IAFahim/sv.git?path=/packages/com.sv.engine#v3.0.0-rc.4",
"com.sv.unity.v4": "https://github.com/IAFahim/sv.git?path=/packages/com.sv.unity.v4#v3.0.0-rc.4"
```

Then: Package Manager > **sv Unity Adapter v4** > Samples > **Hello sv** > Import. Create an empty GameObject, add the **Hello Sv** component, press **Play**. The Console prints the same two lines as step 1, with the same digests:

```
frame 1: hero health 25, knight health 85 shield 0, hero low health True, digest D0391ABCE50C62DB
frame 2: hero health 75, knight health 85 shield 0, hero low health False, digest 9078B3C66F1DD3FE
```

What it proves: one pack, same results in .NET and in Unity, where every frame phase runs as a Burst job. Proven tonight in a brand-new project made only from the two git URLs above: the sample imports with 0 compile errors, its test and a press-Play test pass (2/2, two runs). The designer edit of step 2 also works in Unity through **Window > sv > Pack Authoring** (verified at rc.3; the sample's README walks through it).

A ready project with the sample already imported is on the demo machine at `~/sv-proto/demo-walk` (open it in Unity 7000.0.0a16 to skip the first import, about 5 minutes).

## 5. Speed at a million entities (2 minutes)

```sh
dotnet build benchmarks/Sv.Bench -c Release
dotnet benchmarks/Sv.Bench/bin/Release/net10.0/Sv.Bench.dll v3-lego-b --ms 1000
```

Quiet machine, 1,000,000 entities, 1,000,000 records per frame, the 55-rule `lego` pack, 11 workers: **7.66 ms per frame** (budget 8.3 ms for 30 Hz with room for the game), 0 bytes allocated per frame, digest `6E7618BF0900D3F8` every run. A million records with random targets and sources: 17.6 ms (`v3-lego-r`, a stress ceiling of 19.0 ms). Full tables: `docs/BENCH-REPORT.md`.

## 6. How it is proven (show, do not run: about 10 minutes)

| Proof | Command | Result tonight |
|---|---|---|
| Every suite, three CPU modes (AVX2, no AVX2, no SIMD) and a checked build | `bash eng/v3-verify.sh` | `v3-verify: all green` (Laws 573, Scenario 80, Diff 58, Lego 120, Host 68 per mode) |
| Unity, Burst at 1 and 11 workers | `eng/m8-run/gate.sh` | 98/98, 0 compile errors |
| A built Linux player, Burst compiled ahead of time, from the git tag | `SV_GIT_REF=v3.0.0-rc.4 bash eng/player-smoke.sh` | 74 cases, 0 failures |
| Random packs against a slow reference engine | `bash eng/fuzz.sh <offset> 25 <mode>` | over 180,000 packs in four CPU modes since the last fix, 0 findings |
| An adversarial review (red team) | `issues/0500-redteam-report.md` | 10 findings, all fixed, 54 attacks kept as tests |

## Do not claim

- Windows, macOS, mobile, console or IL2CPP players: not run yet (IAFahim/sv#99). Only a Linux CoreCLR player is proven.
- CI on ARM64, macOS and Windows for the last two merges: GitHub Actions is blocked by account billing (IAFahim/sv#103). The last fully green CI is commit cb23af8.
- tl's ECS timeline path in Unity (SubScene, Playable Director): not built; tl runs in Unity on its plain host.
- A package registry release: install is by git URL with the tag.
