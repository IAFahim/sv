# Arena sample

Units hit each other with Fire through a Burst `IJobEntity` and read their Health back through another.

1. Make a SubScene. Add a GameObject with `ColumnsWorldAuthoring` and wire `arena.json` from this folder.
2. Add two unit GameObjects with `ColumnsEntityAuthoring` (World: the first object; Template: `dummy`).
3. On the attacker add `ArenaAttackAuthoring` (World, Target: the other unit, Channel `Fire`, Amount 5). On the target add `ArenaHealthAuthoring` (World, Lane `Health`).
4. Enter Play mode. `ArenaAttackSystem` sends before `ColumnsStepSystem` steps; `ArenaHealthSystem` copies Health into `ArenaHealth` in the presentation group, which the Entities Hierarchy shows.

`Tests/ArenaSampleTests.cs` runs the same systems in a hand-made world and checks every frame against a plain `ColumnWorld`.
