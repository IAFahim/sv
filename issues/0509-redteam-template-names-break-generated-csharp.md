# 0509 red team: template names that collide after mangling, and the name value__, pass the compiler and the emitter but break the generated C#

Status: open (found by red team, not fixed)

Severity: medium (accepted pack, generated code does not compile)
Kind: bug

Found by: red team (opus), branch `redteam/opus`, base sv `7f6bdb4`

## Promise broken
O6: every accepted pack compiles with 0 warnings; malformed packs are rejected with diagnostics.

## Smallest input
`values.sv.json`: `Health` intrinsic, channel `Hit`. No rules or cards.
- `templates.sv.json`: `[{"id":1,"name":"orc-boss"},{"id":2,"name":"orc_boss"}]`
- `templates.sv.json`: `[{"id":1,"name":"value__"},{"id":2,"name":"base"}]`
- `values.sv.json` with an intrinsic named `value__` (any enum: intrinsic, stat, channel, kind, event).

Tests: `tests/Sv.Diff.Tests/RedTeam/RedTeamPackLimitTests.cs`, `Templates_with_awkward_names_are_rejected_or_compile_clean` (`TemplateMangled`, `TemplateReserved`), `A_pack_with_awkward_names_is_rejected_or_compiles_clean` (`EnumReservedName`).

## What happens
Command: `dotnet test tests/Sv.Diff.Tests -c Release --filter "FullyQualifiedName~RedTeamPackLimit"`

Expected: a diagnostic, as for two values `a-b` and `a_b` (the emitter rejects those with `SV0007 ... both become the identifier a_b`).
Actual: `Values.g.cs(30,9): error CS0102: The type 'Template' already contains a definition for 'orc_boss'`; `error CS0076: The enumerator name 'value__' is reserved and cannot be used` (template and intrinsic).

The SV0007 identifier-collision check covers value, channel and kind rows but not templates, and `Names.Identifier` does not treat `value__` as reserved. Also, the SV0007 check runs only in the emitter: `PackCompiler.Compile` accepts a pack with values `a-b` and `a_b`, so `sv run` and the web designer run a pack that `sv pack` then refuses.

## Reproducible
3 of 3, default, `DOTNET_EnableAVX2=0`, `DOTNET_EnableHWIntrinsic=0`, `taskset -c 0,1`.

## Fix direction
Run the identifier-collision check in `PackPlan` over every name that becomes an identifier (templates included) and reject `value__`, so `PackCompiler.Compile` and the emitter agree.
