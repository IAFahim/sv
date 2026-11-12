# Issue 0128 - Phase 2 baseline: exact counts, and where "442/446" came from

Core agent, branch `proto2/core` from sv main `ac0e065`. Labels: `ai`, `area:test`.

## Baseline (this Windows host, i9-14900K, SDK 10.0.401, before any change)

`dotnet build sv.slnx -c Release` then `dotnet test sv.slnx -c Release --no-build -p:NuGetAudit=false`:

| Project | Passed / total |
|---|---:|
| Sv.Tests | 495 / 495 |
| Sv.Portable.Tests | 379 / 379 |
| Sv.NorthStar | 27 / 27 |
| Sv.NorthStar2 | 9 / 9 |
| Sv.Unity.Tests | 1 / 1 |

`tests/Sv.Fuzz` is an executable, not a test project.

## 442/446 vs 495/495

- "442/446, 4 path-artefact failures" is not an sv receipt. It comes from the Weave study
  (`_work/plan-connect.md` line 53, quoted by `ask-senior-sv.md` line 72), which ran the suite
  from outside the sv tree and never re-ran it in-tree (plan-connect line 422 says so). The
  first in-tree run on this machine (issue 0117 baseline, sv `a0963c8`) was 446/446; no run
  since reproduced a failure, so the 4 failures were environmental (working-directory paths
  of the study's checkout), not code.
- 446 -> 495: 49 new tests in phase 1, no existing test edited:
  FoldLawTests 12, EventDeriveTests 13, StatOpSinkTests 7, StatMathTests 6, LinkTableTests 3,
  SinkFillTests 3, SetChurnTests 2, ArenaShrinkFloorTests 1, CrossHostDigestPortableTests 1,
  IsaLegTests 1 (+ `HeavyAllocationCollection`, no tests). Sum 49.
- Sv.Unity.Tests 13 -> 1: commit `3036780` deleted the Unity adapter v2 with its facade tests
  (`SvUnityFacadeTests`, `SvUnityV2Tests`) and added `SvTwinNetTests` (1). Those 12 tests
  tested deleted v2 types (plan section 3 "Delete"); they were never part of the 446.
- Known flake class (not reproduced today): per-thread 0 B tests under parallel GC timing,
  fixed by serializing heavy classes (issue 0126).
