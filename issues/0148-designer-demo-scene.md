# Issue 0148 - Designer-facing demo: Timeline window clips and a scene a designer can edit without code

Status: open

Labels: `ai`, `area:unity`, `area:authoring`, `open`.

## Problem

Nothing in sv shows the designer path end to end. tl ships demos (Force, Events, Crowd) that a designer can open, edit in
Unity's Timeline window and see change. sv has tests and a benchmark only.

## Wanted

A sample under `packages/com.sv.unity/Samples~` (same convention as tl.unity `Samples~`):

- stat sheet from issue 0145 (Hp, MaxHp, JumpsLeft, MaxJumps, LowHp);
- toolbox clips: `StatMod` (0146), tl's existing `Hit`, a `Jump` that spends a jump;
- timelines in tl JSON: `ring_of_vigor`, `drink_poison`, `double_jump_boots`, `panic` (0147);
- a SubScene entity with `TimelinePlayerAuthoring`, a play-mode test that prints actual and expected numbers per
  scenario (same style as tl demos), and a short designer guide: where to put a clip, what each field means.

## Done when

- A designer changes a number in a timeline JSON or a stat sheet and sees a different result without recompiling code.
- The play-mode test passes in Unity 7 with 0 `error CS` and 0 Burst errors.
- The guide uses designer words (stat, current value, clip) before sv words (intrinsic, fold).

Depends on 0145, 0146, 0147.

## Note (2026-11-14)

Stays open: not needed for the first production test (docs/PRODUCTION-CHECKLIST.md). It is the next authoring item after the test, on the v3 adapter (`com.sv.unity.v4`) and tl.unity's Timeline window.
