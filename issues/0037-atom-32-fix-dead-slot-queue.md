# Issue 0037 — Atom 32: fix — queued pairs of released slots made dead slots set members (heap corruption after renumber)

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Filed as GitHub issue #37 (pending — see receipts). Labels: `ai`, `area:core`,
`kind:bug`, `severity:high`.

## Problem

`Acquire` queues the slot's pairs for the next `Recalculate`. If the slot was released
before that `Recalculate`, `ClearSlotStats` cleared its membership but the pairs stayed
queued, and `Recalculate` processed them: the **dead** slot was added to a set (with
its cleared, empty modifier list) and broadcast to. Consequences:

- dead slots accumulated as set members (and received broadcasts) — memory and
  broadcast work for nothing;
- `RenumberSlots` maps dead slots to -1, so `SetTable.RenumberMembers` turned such
  a member into slot **-1**; the next broadcast of that set wrote `column[-1]` —
  one element before the sv-owned column allocation (glibc: "double free or
  corruption (out)"), or into another stat's last row.

Present since atom 10 (renumbering); the stale membership itself since atom 2.
Reproduced on the session-start commit 21db368 with a churn + renumber scenario.
Found while writing atom 33's mirror tests (the test host crashed).

## Shape

- `Recalculate` clears `Queued` and skips any dequeued pair whose slot is not live.

## Receipts

- New `DeadSlotQueueTests`: acquire+release before recalc leaves member totals equal
  to `LiveCount`; renumber after a transient slot never writes outside a guarded
  external column (sentinels at [-1] and [64] survive). Both fail before the fix.
- The scratch churn + renumber repro that aborted with heap corruption on 21db368,
  10d6c82 and HEAD now runs clean under `MALLOC_CHECK_=3`.
- `CrossHostDigestTests` (atom 30) now emits only over live words (dead rows are
  outside the determinism contract; the old scenario hashed dead-row events that
  depended on phantom broadcasts). Re-baked digest `F27280C52591D65A`, identical
  with AVX2, `DOTNET_EnableAVX2=0` and `DOTNET_EnableHWIntrinsic=0`.
- Tests default: 158 + 21 + 6 passed, 0 failed. SV_CHECKED: 160 + 21 + 6 passed.
- no-FMA grep clean.
