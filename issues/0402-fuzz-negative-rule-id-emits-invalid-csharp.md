# 0402 fuzz: O6/O9 break when an emit rule has a negative id (PackCompiler accepts it and Kernels.cs emits EmitRule-1)

Status: closed (2026-11-14, fixed by the lead; see Fix)

Found by: agent-1 (`fuzz/agent-1`), `dotnet test tests/Sv.Diff.Tests -c Release --filter MalformedPackDiffTests`, sv commit `ee767fd` (base `cb23af8`)

## Promise broken
O6: Every generated (or accepted) pack compiles with 0 errors and 0 warnings (`KernelCompiler.Build` on packs where `PackCompiler.Compile` returned `Ok`).
O9: A malformed pack is rejected with a diagnostic: never a crash, a hang, an uncompilable emit, or an exception other than a pack error (`tests/Sv.Diff.Tests/MalformedPackDiffTests.cs`, `neg-number` mutation).

## Smallest input

`values.sv.json`:
```json
{"schema":1,"nextLane":1,"rows":[
{"id":0,"name":"C0","class":"channel"}
]}
```

`rules.sv.json`:
```json
{"schema":1,"rows":[
{"id":-1,"name":"emit","layer":"emit","depthLimit":0,"when":true,"then":[{"emit":{"to":"Self","channel":"C0","amount":1}}]}
]}
```

`cards.sv.json`:
```json
{"schema":1,"rows":[]}
```

`templates.sv.json`:
```json
{"schema":1,"rows":[
{"id":1,"name":"base"}
]}
```

`scenario.json`:
```json
{"schema":1,"name":"shrink","frames":1,
"entities":[{"name":"E0","template":"base"}],
"sends":[{"frame":0,"target":"E0","channel":"C0","amount":1}],
"kills":[],
"modifiers":[]}
```

Control check: changing `"id": -1` to `"id": 1` in `rules.sv.json` passes `KernelCompiler.Build` (`item.Ready == true`).

## What happens
Command: `dotnet test tests/Sv.Diff.Tests -c Release --filter MalformedPackDiffTests --logger "console;verbosity=detailed"`

Default-seed failing seeds (`2001`–`2100`, `neg-number` mutation):
- `seed 2042`: `rules.sv.json` `{"id":-1,"name":"rule1013","layer":"emit",...}` -> emits `EmitRule-1(...)` (`Kernels.g.cs` lines 544 and 994, receipt `malformed-neg-number-seed2042.txt`)
- `seed 2080`: `rules.sv.json` `{"id":-1,"name":"rule1015","layer":"emit",...}` -> emits `EmitRule-1(...)` and `SelfRule-1(...)` (`Kernels.g.cs` lines 516, 536, 1418, 1453, receipt `malformed-neg-number-seed2080.txt`)
- `seed 2096`: `rules.sv.json` `{"id":-1,"name":"rule1011","layer":"emit",...}` -> emits `EmitRule-1(...)` (`Kernels.g.cs` lines 530 and 2117, receipt `malformed-neg-number-seed2096.txt`)

Expected: `PackCompiler.Compile` either rejects negative rule ids with a pack diagnostic (`IdOutOfRange`, matching `Validate2Pass` in `src/Sv.Rules/Passes/10_Validate2.cs` lines 28–39 for `values` rows `rows[i].Id < 0 || rows[i].Id > limit`) or emits valid C#.

Actual:
1. `PackReader.ReadRules` (`src/Sv.Rules/Pack/PackReader.cs` lines 1150–1160) only calls `RequireInt(row, "id", out var id)` and checks `DuplicateId`, with no check that `id >= 0`. Downstream passes (`10_Validate2.cs`) do not validate `rule.Id < 0` either, so `PackCompiler.Compile` returns `Ok` with 0 errors.
2. `EmitMethod` (`src/Sv.Rules/Emit/Kernels.cs` line 899: `(self ? "SelfRule" : "EmitRule") + Text(emit.Rule.Id)`) concatenates `emit.Rule.Id` directly into the method identifier (`static void EmitRule-1(...)` / `SelfRule-1(...)`), failing C# compilation with `error CS1003: Syntax error, ',' expected` (at line 994 col 29 for `seed 2042`; line 378 col 29 on the shrunk pack).

First differing frame: compile time (no frames run; generated `Kernels.g.cs` does not compile).

## Reproducible
4 of 4 modes, deterministic (`3 of 1000 attempts failed` on default seeds `2001`–`2100`):
- `default`: `dotnet test tests/Sv.Diff.Tests -c Release --no-build --filter MalformedPackDiffTests` (`3 of 1000`, first `seed 2042`)
- `noavx2`: `DOTNET_EnableAVX2=0 dotnet test tests/Sv.Diff.Tests -c Release --no-build --filter MalformedPackDiffTests` (`3 of 1000`, first `seed 2042`)
- `scalar`: `DOTNET_EnableHWIntrinsic=0 dotnet test tests/Sv.Diff.Tests -c Release --no-build --filter MalformedPackDiffTests` (`3 of 1000`, first `seed 2042`)
- `contended`: `taskset -c 0,1 dotnet test tests/Sv.Diff.Tests -c Release --no-build --filter MalformedPackDiffTests` (`3 of 1000`, first `seed 2042`)

Shrunk repro: tested against `/tmp/shrink-0402` via temporary `ShrinkRepro`: `Diff.Shrink/Kernels.g.cs(378,29): error CS1003: Syntax error, ',' expected` (`1.31 s`); control (`"id": 1`) passes (`0.855 s`).

## Seen again (`10500000`–`10740000`, all 4 modes `default / noavx2 / scalar / contended`)
`31` seeds across `15` of `25` rounds (`1,000` malformed mutation attempts per round), identical across all `4` modes (`neg-number` -> `"id": -1` on `"layer": "emit"`, `error CS1003` on `EmitRule-1` / `SelfRule-1`):
- `10510000`: `10512030`, `10512038`, `10512086`
- `10520000`: `10522062`, `10522094`
- `10560000`: `10562071`
- `10570000`: `10572032`, `10572072`
- `10580000`: `10582004`, `10582022`, `10582028`, `10582060`
- `10600000`: `10602051`, `10602059`
- `10640000`: `10642056`, `10642058`, `10642072`, `10642088`
- `10650000`: `10652064`, `10652072`, `10652074`
- `10670000`: `10672023`
- `10680000`: `10682005`
- `10690000`: `10692028`
- `10700000`: `10702062`
- `10710000`: `10712006`, `10712014`, `10712024`
- `10720000`: `10722002`
- `10730000`: `10732043`, `10732045`

## Not checked
- Forward fuzzing on an `0402`-fixed tree (`origin/main` remained at `cb23af8` during `10500000`–`10740000`; `MalformedPackDiffTests` compiled all accepted mutations other than these `31` `0402` seeds without C# errors, aside from `3` transient `taskset -c 0,1` ThreadPool starvation 10 s timeouts at `10622001`, `10662001`, and `10722001` that all cleared on isolated and full-round reruns).
- Whether negative `id` values on `card` (`PackReader.cs:790`) or `template` (`PackReader.cs:946`) rows break any runtime invariants when negative (note none of those triggered a C# compile error across default seeds `2001`–`2100` or `10500000`–`10740000`).

## Fix (lead)

`PackReader` rejects a negative id on a rule, card or template row with `SV0009 IdOutOfRange` ("rule id -1 is negative; ids are 0 or more"), so `PackCompiler.Compile`, `sv run`, the web designer and `sv pack` all refuse the pack before any C# is generated. The value-row id range check (0..255 for channels and kinds, 0..65535 otherwise) moved from the emitter's last pass (`Validate2Pass`) into `PackCompiler.Plan`, closing the same split for values (the class of red-team 0509).

Regression: `HandwrittenTests.A_negative_rule_card_or_template_id_is_rejected_when_the_pack_compiles` (rule, card, template at -1 rejected with SV0009; id 1 accepted). The agent's `MalformedPackDiffTests` (with its compile check for accepted mutations) now passes: `malformed: 1000 attempts checked, 0 failed, seeds 2001-2100` (it failed seeds 2042, 2080, 2096 before). Its four harnesses are merged with this fix.
