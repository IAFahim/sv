# Issue 0148 - Designer-facing demo: Timeline window clips and a scene a designer can edit without code

Status: closed (2026-11-14, rescoped to the v3 adapter and delivered as the Hello sv sample)

Labels: `ai`, `area:unity`, `area:authoring`, `closed`.

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

## Done (2026-11-14)

Rescoped from the deleted v2 adapter to `com.sv.unity.v4`: `packages/com.sv.unity.v4/Samples~/Hello` (Package Manager > sv Unity Adapter v4 > Samples > Hello sv).
- Four pack files a designer edits (`Pack/`), generated code (`Generated/`), a `HelloSv` component that logs the result, and a guide in designer words first (`README.md`: stat, current value, damage type, card; then the sv words).
- A designer changes a number (the guide's example: knight `"Shield":15` to `30`), presses Regenerate in Window > sv > Pack Authoring, and sees a different result without code. The guide's numbers were checked: knight health 100 and shield 0 after frame 1.
- `Tests/HelloSvTests.cs` asserts the sample prints the same numbers and digests as the .NET `samples/Sv.Hello` (D0391ABCE50C62DB, 9078B3C66F1DD3FE). Unity 7000.0.0a16: imported into the harness, EditMode 88/88, 0 error CS, 0 warning CS, 0 error BC.
- Not in this sample: Unity Timeline-window clips. tl inside Unity is proven separately (`packages/com.sv.unity.v4/Tests/Tl`, issue 0400) on tl's plain host.
