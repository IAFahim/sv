# Task S2 (for a smaller agent): two stat layers (core, then external) and derived-stat rows

Branch: create `v3/s2-layers` from `v3/int` in a new worktree under D:\GitHub\_work\sv-proto\. Issue range 0390-0399. No comments in code, C# 9 in Sv.Core and Sv.Rules, warnings are errors.

Read first: `docs/LEGO-REPORT.md` (mechanic 17 and missing brick 4), issue 0210 (items 1 and 3), `contract/CONTRACT-v3.0.1-integer-stats.md`, `contract/CONTRACT-v3.1.0-bricks.md` (style of a clause), `src/Sv.Core/Stats/StatLaw.cs`, `StatMachine*.cs` (StatSource, derived stats are already supported by the machine), `src/Sv.Host/HostPack.cs` (passes no stat sources), `src/Sv.Rules/Reference/Interpreter.cs` (the stat fold, the oracle).

Goal (owner's For Honor lesson): a stat's modifiers have a layer, `core` or `external`. The law runs twice: core terms on the base, then external terms on the core result, so designer intent (180 + 10) x 1.1 = 209 is reachable (today one fold gives 214). Default layer is core, so every existing pack and digest stays identical.
Steps:
1. Contract text first: new file `contract/CONTRACT-v3.2.0-stat-layers.md` (syntax `"layer": "core"|"external"` on card modifiers and host stat ops, semantics, determinism, errors).
2. StatLaw: add the second fold (scalar law first, vector twin under `#if NET`), reference interpreter, StatMachine (ops carry the layer), HostPack (cards carry the layer), pack reader and writer, validation.
3. Derived-stat row in the pack (issue 0210 item 3): `"derive": {"from": "Core", "coefficient": 1.0}` on a stat, built into StatSource entries by HostPack and folded by the reference identically.
4. Tests (new files only): law tests for the layered fold on edge values (int32 limits, scale, basis points) against an exact big-integer model; a diff test (reference equals compiled) with layers; a Lego scenario that makes mechanic 17 give 209; rewind restores layered stats.
Verify: all existing suites stay green and unchanged in counts (Laws 516, Scenario 80, Diff 16, Host 27, Lego 119 plus your new ones), every existing digest byte-identical (bench digests A DAF3A1D9BD73BA10, B 6E7618BF0900D3F8, C D3C714D46555B7CE, D 7413F619472566C4; the Lego digests in docs/LEGO-REPORT.md), suites also under `DOTNET_EnableAVX2=0` and `DOTNET_EnableHWIntrinsic=0`, Sv.Core and Sv.Rules build for net10.0 and netstandard2.1 with 0 warnings. Commit atoms, do not push. When the contract is ambiguous take the reading that matches the existing code and list it in the report. Final report under 200 words.
