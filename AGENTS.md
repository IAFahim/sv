# sv repository rules

`sv` is the state-variable library of the three-system composition (tl × ecs-kernels ×
sv). The README's vendored Composition Contract is the law for every line of math and
every public signature; read it before touching anything.

- The contract changes only through the agent-bus merge point
  (`agent-bus/contract/CONTRACT.md`): bus message first, then vendor the bumped copy
  here verbatim.
- Atoms are short: green build + tests, committed, receipts recorded in the issue file
  (`issues/`) before the next atom starts. File the issue files on GitHub verbatim
  once the remote exists.
- Production code: no explanatory comments, no FMA (grep-enforced), ternary/select
  clamps only on floats, unmanaged data path, deterministic order everywhere. Tests
  and tooling may be managed freely.
- Before starting and before finishing any atom, check the agent bus:
  `/home/i/GitHub/agent-bus/bin/check stats --body`. Reply and ack per the bus README.
- `contract/` holds verbatim bus artifacts — never edit them; re-copy on bump.
