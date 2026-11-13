# 0350 rule bricks v3.1.0: readings taken where the brief and plan are silent

Status: open (owner confirms each reading; the code and `contract/CONTRACT-v3.1.0-bricks.md` follow them today)

Found while adding the five bricks from issue 0330 (branch v3/b1-bricks).

1. **Assign values are per hit.** A record of h merged equal hits sets the same value as one hit, so set is idempotent and
   merge-invariant (T1). The value is the per-hit taken amount (`ConsumedPerHit`), and only counting records (taken != 0) assign.
   Max and min include the frame-start value (refresh, never lower).
2. **Order of set contributions.** Records in canonical order, then same-frame self emits in self-rule order, then flow terms in
   flow id order; the last wins. At most one assign mode per intrinsic (AssignConflict); add terms combine with any mode.
3. **Source mode.** A record without a source sets -1; a same-frame self emit sets the entity's own slot (clause 25: an emit's
   source is the emitting entity). Flows cannot use source mode.
4. **Flow assign syntax.** The mode is the term key (`{"set": [X, C]}`), not a `"mode"` field, matching `add`/`subtract`.
5. **Shape cells.** The cell holds the decimal a designer would write, converted by clause 33 to the Shape's unit (basis points
   for additive and multi, amount for added and cap). An intrinsic used as a percentage needs a decimal scale.
6. **Roll.** One draw per record (hit ordinal 0 everywhere); a per-hit draw would cost one evaluation per hit. The frame input
   is the number of frames stepped before this one (host `World.Frame` at Decide, now also `WorldView.Frame`; reference
   `state.Frame`), so replay after `Rewind` draws the same values. Pack seed default 0, written only when not 0.
7. **Spend.** Counts hits only; spend and absorb on one budget share `used`, in gate order; a rule may not hold both.
8. **Edges.** Cell amounts and intrinsic scales are rejected on edge rules, because edges run after Commit and would read
   post-fold cells, not frame-start values.

Receipts: Laws 481/481 (341 + 140 new), Scenario 80/80, Lego 119/119 (90 + 29 new), Diff 16/16 (12 + 4 new, 160 brick seeds),
on default, `DOTNET_EnableAVX2=0` and `DOTNET_EnableHWIntrinsic=0`. 680 existing diff packs: PackHash and every per-frame
reference digest byte-identical before and after (dumped and compared). The 22 old lego scenarios print the report's digests.
