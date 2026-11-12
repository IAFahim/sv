# 0311 compiled host and reference interpreter run two stat models (cards and modifier handles)

Status: closed (part 2: reference allows many modifiers per source and removes by source; part 1: host folds card terms)

Found by T2 (`tests/Sv.Diff.Tests`). Two differences, each with a minimal receipt:

1. Card modifiers. Pack: stat `S0` default 10.0, intrinsic `Card0`, channel `GiveCard0` into `Card0`; card `card0` value `Card0`
   with modifier `{stat: S0, added: 5.0}`; no rules. Scenario: frame 0 sends `GiveCard0` 1.
   Expected (reference): frame 1 S0 = 15 (digest FC17FE3D9A140D0F). Got (compiled): S0 = 10 (digest C5626E9F306CC260).
   `HandwrittenTests.A_card_modifier_reaches_the_stat_on_both_hosts`. Random packs with card modifiers: 22 of 40 seeds fail
   (5002..5040, `Card_modifiers_on_compiled_kernels_equal_the_reference`).
   Cause: `src/Sv.Host/HostPack.cs:29-85` and `Frame.cs:288` build the `StatMachine` with no card terms; the reference folds
   card modifiers by possession every frame (`src/Sv.Rules/Reference/Interpreter.cs:542-557`).
2. Two modifiers from one source. Same values; scenario modifiers at frame 0: `Added 5 source 1` and `Added 3 source 1`.
   Expected (reference): S0 = 15 (second add rejected as a fault, digest 008A82E88F3720B9). Got (compiled): S0 = 18 (digest
   F8A4C63154AD4608). `HandwrittenTests.Two_modifiers_from_one_source_fold_alike_on_both_hosts`.
   Cause: the reference keys a modifier by handle = source (`Interpreter.cs:504-523`); `StatOp.Add` allows many per source.
   With one modifier per source the generated stat-op property passes 40 of 40.
Part 1 fix (C4, branch v3/c3-cards): `StatMachine` takes a static card-term table (`CardTerm`: stat lane, card lane, kind, value;
`StatMachine.Cards.cs`). `Gather` adds the terms of every held card (lane value > 0) before the one `Canonicalize` then `Fold`, so the
terms and the fold equal the reference. Dirty marks: Commit records per card lane the rows whose value changed
(`FrameContext.CardRows`), the serial `CardPhase` marks the card's stats dirty, the next frame's `Recalculate` folds them (same frame
of delay as the reference); `Spawn` marks the stats of cards its template holds. Templates fold without cards (the reference spawns
with the base). The pending dirty list is state between frames, so each epoch mark keeps a copy and `Rewind` restores it.
Receipts: `dotnet test tests/Sv.Diff.Tests -c Release` 12 of 12; `tests/Sv.Laws.Tests` 341 of 341 (334 + 7 new `CardLaws`);
`tests/Sv.Scenario.Tests` 80 of 80.
