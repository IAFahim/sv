# Issue 0134 - docs/PRODUCTION-CHECKLIST.md; USING-WITH-TL limits refreshed

Core agent, branch `proto2/core`. Labels: `ai`, `area:docs`.

- New `docs/PRODUCTION-CHECKLIST.md` (one page): what to enable for a real tl project test, measured
  numbers with sources (issues 0125, 0127, 0130, 0133), how to read a digest mismatch (first
  differing tick, then which pair differs), failure modes and their meaning. Every exception text
  quoted was checked against src/Sv.
- `docs/USING-WITH-TL.md` section 6: the "no public Bounds over a host float column" limit is
  gone (`Bounds.StatRows`), sink refill now covers `LoadKeyframe`/`LoadState`, `InboxLanes` and
  `DigestCadence` noted.

## Finding while checking the checklist (pre-existing, not fixed)

`dotnet test sv.slnx -c Release -p:DefineConstants=SV_CHECKED` fails one existing test on sv main
`ac0e065` and on this branch alike: `BindingGenerationTests.ExternalBind_Invalidate_Recalculate_CheckedThrows_ReleaseParks`
expects `Recalculate` to throw after `InvalidateBindings` under SV_CHECKED; it parks instead
(likely since atom 91 parked bindings, when CI was dead). Sv.Tests 508/509 and Sv.Portable.Tests
390/391 in the checked build; all other projects green. Owner decision: either the checked
contract is "park" (the test's checked branch is stale; needs an owner-approved test edit) or
"throw" (a src fix). Existing test bodies are not edited here.
