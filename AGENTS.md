# sv repository rules

`sv` is the state-variable library of the three-system composition (tl × ecs-kernels ×
sv). The README's vendored Composition Contract is the law for every line of math and
every public signature; read it before touching anything.

- The contract changes only through the agent-bus merge point
  (`agent-bus/contract/CONTRACT.md`): bus message first, then vendor the bumped copy
  here verbatim.
- Owner-approved (2026-11-12): while the agent-bus is unreachable, the owner replaces
  the bus as contract approver; approved texts land as new files in `contract/`
  (first: `contract/CONTRACT-v2.1.0.md`).
- Atoms are short: green build + tests, committed, receipts recorded in the issue file
  (`issues/`) before the next atom starts. File the issue files on GitHub verbatim
  once the remote exists.
- Production code: no explanatory comments, no FMA (grep-enforced), ternary/select
  clamps only on floats, unmanaged data path, deterministic order everywhere. Tests
  and tooling may be managed freely.
- Before starting and before finishing any atom, check the agent bus:
  `/home/i/GitHub/agent-bus/bin/check stats --body`. Reply and ack per the bus README.
- `contract/` holds verbatim bus artifacts — never edit them; re-copy on bump.
- The on-disk `/home/i/GitHub/ecs-kernels` tree is a stale pre-refactor copy (bus
  message 0010). Never grep it as evidence of the kernel system's current state; the
  canonical `Kernels.Testing.StrictFloat` kit (Add/Subtract/Multiply/Divide/Sqrt, all
  NoInlining double-emulated) is vendored in `tests/Sv.Tests/StrictFloatOracle.cs` and
  must stay textually identical to the kernel repo's copy. Any diff is a bus incident.
