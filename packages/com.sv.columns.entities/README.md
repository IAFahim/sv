# com.sv.columns.entities

sv v4 in Unity Entities and Burst. The v4 frame runs as two Burst jobs over native memory and gives the same digest as `ColumnWorld.Step()` in .NET.

- `ColumnsWorldAuthoring` and `ColumnsEntityAuthoring`: wire a pack JSON `TextAsset`, pick a template and lane values; bakers turn them into `ColumnsWorldConfig` (pack as a blob), `ColumnsTemplate` and `ColumnsLaneValue`.
- `ColumnsStepSystem` (`ISystem`): spawns and despawns slots for entities, schedules the frame, publishes `ColumnsState` (`View`, `Sender`).
- `ColumnsRecords.ParallelWriter`: send records from any Burst job.
- `ColumnsBurstHost`: the same frame without ECS, with `Inputs`, `Rewind` and `Resimulate` when it keeps history (`History` on the world authoring).

See `docs/UNITY.md` in the sv repository. The Arena sample shows a Burst attack system and a Burst health readout.
