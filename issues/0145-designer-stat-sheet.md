# Issue 0145 - Stat sheet asset: designer declares stats, intrinsics, conditions; code is generated

Status: closed 2026-11-12 (merged into 0154). Premise of generated per-value structs is wrong; 0154 is the requirement.

Labels: `ai`, `area:unity`, `area:authoring`, `open`.

## Problem

Today a designer cannot reach sv. The columns are hand-written `[TlState]` structs and an `SvLinkRegistry` block
(docs/UNITY.md section 3). tl's model is: designers never write code, programmers write each type once, tl bakes data
and generates ids (`Timelines.g.cs`, `Kinds.g.cs`). sv has no data step.

## Wanted

A stat sheet (JSON, plus a Unity asset that wraps it) with:

```json
{
  "stats": [ { "name": "MaxHp", "base": 100 } ],
  "intrinsics": [ { "name": "Hp", "min": 0, "max": "MaxHp", "start": "max" } ],
  "conditions": [ { "name": "LowHp", "on": "Hp", "enter": 0.3, "leave": 0.4 } ]
}
```

A bake step (same shape as `tl library`) that writes: the `[TlState]` structs (value, delta, stat output), the
`GameStat` enum, the schema, the link registration code, and a content hash pinned beside `Schema.ContentHash`.
Validation errors name the sheet field (same style as tl's JSON errors).

## Done when

- One sheet reproduces the hand-written Hp/MaxHp/LowHp setup of docs/UNITY.md with the same NorthStar2 digests.
- A bad sheet (unknown stat in `max`, two writers) fails the bake with a field name.
- No generated file is edited by hand; regenerate is idempotent (byte equal).

## Notes

tl side: additions only, no change to tl's 13 hashes. Needs the senior lookup in issue 0152 (question 1).


Owner clarification 2026-11-12: re-scoped by issue 0154. The premise of hand-written components and per-value C# in this file is wrong; read 0154 first.
