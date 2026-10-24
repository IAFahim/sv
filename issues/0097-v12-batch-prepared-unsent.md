# Issue 0097 — Prepared (NOT sent): contract v1.2 batch

**Status: prepared, gated.** The owner approved preparation (decision 0043, relayed
on the bus as message 0052): *send only after the integrator folds 0036 (v1.1.0),
0042 (clause 18) and 0051 (clause 7 regularization). Not before.* When the fold
lands, send this file's body verbatim:

```
BUS_FROM=stats /home/i/GitHub/agent-bus/bin/send integrator \
  "Contract v1.2 batch: crossed bounds, saturating applied, 64-bit handles, parked bindings, any-offset windows, blend-store, Expire step (owner-approved)" \
  < <(sed -n '/^PROPOSAL/,$p' issues/0097-v12-batch-prepared-unsent.md)
```

All wording below is owner-decided (0043–0049, relay 0052); no item needs a
ruling. sv implements each item as its own atom citing the folded batch; nothing
in `sv/contract/` changes until the bump publishes upstream. Labels for the
GitHub mirror: `ai`, `area:contract`.

---

PROPOSAL: bump the composition contract to v1.2.0 after (and building on) the
v1.1.0 batch (message 0036) and clause 18 (message 0042) fold. The owner approved
every item below in session on 2026-10-23; answers relayed verbatim in message
0052. sv's code atoms for items 1, 3, 4, 5, 6 are gated on this fold per the
owner's instruction ("Send only after the integrator folds 0036/0042 (and 0051).
Not before.") — this batch is why sv's roadmap atoms 1.3/1.5/1.8/6.3 and the
3.3 seam slice are parked.

## Item 1 — clause 10: crossed bounds are defined (ceiling wins)

Proposed wording, appended to clause 10:

> When column or stat bounds cross at runtime (min > max — `Constant` already
> rejects that at construction), the ceiling wins: the clamp is defined as
> `[min(min, max), max]`. A pass with zero delta is then the identity, and the
> clamp is monotone on its domain.

Motivation: today crossed bounds oscillate (with `lo = 0, hi = −5` a zero-delta
pass flips the value every frame — sv roadmap F8), and the drift algebra (§4.1)
requires a monotone clamp. Owner decision 0044: *"Ceiling wins: clamp to
[min(floor, ceiling), ceiling]."* sv atom (roadmap 1.3) extends its lattice
oracle with crossed rows; the change is behavior-visible only for inputs that
are undefined today.

## Item 2 — clause 18: `applied` is a saturating difference

Proposed wording, extending clause 18 (message 0042, which stays verbatim):

> The reported applied delta is `saturate(after − before)` on the integer
> surface: `after − before` computed so it never wraps (32-bit subtraction with
> signed saturation). A clamp that pulls a far-out-of-range value back reports
> the true distance moved, capped at ±(2³¹ − 1), never a sign-flipped hit.

Motivation: sv roadmap F9 — `before = int.MinValue, delta = 0, Clamp.Constant(0,
100)` reports `applied = int.MinValue` while the true value is +2³¹;
emit-from-applied then reports a two-billion-point hit for a heal. Owner
decision: covered by the 0043 batch approval (roadmap §8 row 2). sv atom
(roadmap 1.3, second half).

## Item 3 — clause 3 + portable surface: 64-bit generation-tagged modifier ids

Proposed wording:

> `ModifierHandle` widens to 64 bits: a generation tag in the high bits, the
> insertion id in the low bits. The canonical fold order (clause 3) keys on the
> full 64-bit value for host-added modifiers and on a separate monotonically
> increasing sort key for derived entries, so derived folds stay deterministic
> regardless of id traffic. `SvState` takes one format bump (schema-hash covered
> per v1.1.0's state integrity) — pre-bump saves do not load.

Motivation: the 32-bit id wraps in ~36 min at 1M adds/s (sv roadmap F5); a wrap
reorders the fold silently. Owner decision 0045: *"Yes — 64-bit generation-tagged
ModifierHandle, separate sort key for derived entries, one SvState format
bump."* sv atom (roadmap 1.5): ids seeded at 2³¹ − 8, then churn. tl/kernels
impact: `ModifierHandle` storage width only — no tl or kernel type carries it
today (sv-only surface); the integrator should confirm during fold.

## Item 4 — clause 12: bindings park while stale; length checked at bind

Proposed wording, strengthening clause 12:

> `BindOutput` validates the column length against the board capacity at bind
> time (checked builds and release builds alike). After `InvalidateBindings`,
> release builds park their broadcast writes — no bytes reach a stale pointer —
> until the host rebinds. Use-after-free by a host that frees or moves its
> storage is therefore impossible through sv's write path.

Motivation: sv roadmap F13 — today release builds keep writing through pointers
the board itself declared stale. Owner decision: covered by the 0043 batch
approval (roadmap §8 row 4). sv atom (roadmap 1.8), receipt: release build with
a guard page standing in for the stale column.

## Item 5 — clause 5: mask windows at any offset

Proposed wording, extending clause 5:

> Masked windows (`RunEnabledChunk`-shaped APIs and `Window.Masked`) address
> words at any bit offset, not only 64-slot boundaries; the seam's 64-slot
> padding becomes an implementation detail that may vanish. Semantics are
> unchanged: a disabled row is never read or written.

Motivation: sv roadmap S6 — up to 63 slots of padding per archetype (the
renumber receipt already removed the re-resolve cost; the padding is pure
memory), and Unity chunks hand sv masks at arbitrary offsets. Owner decision:
covered by the 0043 batch approval (roadmap §8 row 5). sv atom (roadmap 3.3
seam slice, funnel shift).

## Item 6 — clauses 5/6: disabled rows may be rewritten with their own value

Proposed wording, extending clauses 5 and 6:

> A masked pass may rewrite a disabled row **with its own value** (load, blend
> by mask, store): the observable semantics — a disabled row is never modified —
> are unchanged, and the store becomes a full-width unmasked store on every ISA.
> Partial-word scalar fallback (sv roadmap S8, arm64) disappears.

Motivation: blend-store on `Vector128/256/512` removes the masked-store/tail
split; sv's `enabled-runwalk` moves from 0.92 G rows/s toward ≥ 3 G rows/s
(roadmap 6.3 receipt on the gate hardware). Owner decision 0047: *"Yes —
'disabled rows are rewritten with their own value'."* sv atom (roadmap 6.3).

## Item 7 — clause 7: `Expire` is a declared step-3 sub-step

Already proposed and implemented — message 0051 (sv issue 0091, commit
`bf4482f`) carries the exact wording; this batch references it, nothing new.
One clock (`Game.Tick`), expiry in (tick, insertion id) order, O(due).

## Version arithmetic and ask

0036 (v1.1.0) and 0042 (clause 18) set the base; this batch is the next minor
bump, v1.2.0. Requested action: fold items 1–7, publish the bumped contract at
the merge point, and sv will re-vendor the copy in `sv/contract/` and README
verbatim, then run its gated atoms (1.3, 1.5, 1.8, 3.3-seam, 6.3) one per item.

Measurement note for the receipts that follow (owner decision 0048): the gate is
cold-core p50 on the bus host (i9-14900K, 24C/32T), warm reported alongside; sv
bench banners stamp the measuring host, and the README's auto-refresh table (CI,
2-core EPYC 9V45) is regression tracking, not authoritative numbers.

## Provenance

Owner answers 0043–0049 (2026-10-23), relayed as bus message 0052. Draft
prepared in sv `issues/0097`; send gated on the fold of 0036 + 0042 + 0051 per
the owner's explicit instruction.
