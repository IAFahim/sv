# sv v3 test phase: common brief for every test agent

Date 2026-11-13. Owner order: "Start tests". All v3 code now exists on branch `v3/int` (head 88c9a0d): Sv.Core, Sv.Rules (pack, reference interpreter,
compiler passes, emitter), Sv.Host (frame loop, workers), Sv.Pack (`sv pack|run|trace`), Sv.Bench. Everything builds with 0 warnings. First result:
`sv run` on the worked example gives the SAME digest `E0F445186E2C3672` on the reference interpreter and on compiled kernels (tests/Sv.Kernels.Reference).
This phase proves the code correct. It does NOT chase speed (benchmarks are a later phase).

## Read first (fully, no guessing)

1. `D:\GitHub\_work\sv-proto\BRIEF3.md` (code style, ownership rules, what sv is) and `D:\GitHub\_work\agent-code-brief.md` (style authority).
2. `D:\GitHub\_work\ALL-IN-ONE-sv3.md` sections 0 to 7 (owner intent, R1-R18, worked example) and `D:\GitHub\_work\plan.md` sections 11, 12, 14 (laws, tests, milestones, gates).
3. In your worktree: `docs/V3-HANDOFF.md`, issues 0160, 0170, 0180, 0190, 0200, 0210, `contract/CONTRACT-v3.0.0.md`, `contract/CONTRACT-v3.0.1-integer-stats.md`.
4. `D:\GitHub\unity-cli-skill\skills\unity-cli-field-notes\references\layers-and-library-roles.md`: each layer is tested ALONE first (its law), then adjacent pairs against the reference form, then the whole stack.

## Rules

- You write NEW test projects only (xunit, net10.0, `AllowUnsafeBlocks`, `TreatWarningsAsErrors`). Names and ownership are in your task. Never edit an existing test body, a pinned digest or a `contract/` file.
- You may NOT fix production code. When a test fails because production code is wrong: keep the failing test (do not weaken it), write `issues/NNNN-*.md` in your number range with receipts (inputs, expected, got, file and line of the suspected cause, `Status: open`), and continue with other tests. The manager triages and fixes. A test that fails because the TEST is wrong gets fixed by you.
- Test code is plain too: no comments, readable names, small tests, one law per test. Property tests use a seeded deterministic generator (fixed seeds, printed on failure).
- Do not edit `sv.slnx`. Build and run by path: `dotnet test <csproj> -c Release`.
- Pins: new digests are printed, never "baked" silently. Report any digest that differs between hosts, policies, worker counts or ISA legs: that is a stop-and-report.
- Commit atoms on your branch, do not push. Trailer `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>` unless your harness requires another (say which).
- Usage limits are tight: work efficiently; on a limit stop cleanly with work committed. Final report under 400 words: commits, exact test counts (pass/fail), failing tests with issue numbers, digests, what is not covered, decisions for the owner.
