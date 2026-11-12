# 0313 sv run: the two scenario runners give a send from a not-yet-live source different kinds

Status: open

Found by T2 (`tests/Sv.Scenario.Tests`, `RunnerTests.A_send_from_an_entity_that_spawns_later_gets_one_kind_on_both_runners`).

Receipt: pack `packs/reference`; entities P (`worked_example_player`, holds sniper_ward) and S1 (`sniper`, spawns at frame 1);
frame 0 sends S1 -> P Physical 40 without a kind. Reference runner digest A364550534990ABB, compiled runner digest
B6DE2D0C5EBDA368.

Cause: `src/Sv.Pack/ScenarioRunner.cs:152-154` (reference) takes the source's template Kind when the source is not live (Sniper,
so the ward denies); `ScenarioRunner.cs:293` (compiled) reads the Kind lane of a slot that is not live yet (0, so the record is
taken). One rule is needed for both; `tests/Sv.Diff.Tests/Runs.cs` uses "template Kind unless live" on both hosts.