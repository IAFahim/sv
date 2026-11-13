# 0330 brick-set acceptance (Lego test): missing bricks and readings that need an owner decision

Status: open (owner or senior decides; no production code is wrong against the contract)

Found by L1 (branch v3/lego), full receipts in `docs/LEGO-REPORT.md`. 22 mechanics written as pack rows only (`packs/lego`),
every scenario gives the hand-computed numbers and equal digests on the reference interpreter and compiled kernels
(`dotnet test tests/Sv.Lego.Tests -c Release`: 90 of 90). So this is a design finding, not a bug.

Cannot be said as designed (scenario receipts):
- 16 DoT snapshot: the tick cannot deal the stored snapshot (emit amount is constant or Offered/Taken/NotTaken, `scale` only by
  the emitter's stat; `PackReader.ReadAction`). Second cast adds (SnapPower 32) because nothing assigns.
- 17 core vs external stacking: one fold per stat (clause 31); Strength 214 where the designer means 209. Derived stats have no
  pack row (0210 item 3).

Missing bricks, proposals in the report: (1) assign/set-mode `into`; (2) emit amount from a cell; (3) per-count card modifiers
and Shape by a cell; (4) a second stat layer; (5) deterministic roll; (6) a gate that spends a budget per hit ("spend"),
because gates read the frame-start counter (scenario 09: 3 jumps pass with 2 charges, JumpsDone 7); (7) a same-frame lethal
check (revive is one frame late, scenario 13); (8) masks above bit 63.

Readings to confirm:
- per Hit emits count offered hits, absorbed ones included (`Interpreter.UnitAmount`; scenario 22: Health 55, not 45).
- card modifiers fold once while held, whatever the count (`Interpreter.Stats`, `StatMachine.Cards`).
- self emits exist only for entities with records this frame, so tl's Tick is the only clock (clause 7, by design).
