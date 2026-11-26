# com.sv.columns.entities

sv v4 in Unity Entities and Burst. The v4 frame runs as two Burst jobs over native memory and gives the same digest as `ColumnWorld.Step()` in .NET.

- `ColumnsWorldAuthoring` and `ColumnsEntityAuthoring`: wire a pack JSON `TextAsset`, pick a template and lane values; bakers turn them into `ColumnsWorldConfig` (pack as a blob), `ColumnsTemplate` and `ColumnsLaneValue`.
- `ColumnsStepSystem` (`ISystem`): spawns and despawns slots for entities, schedules the frame, publishes `ColumnsState` (`View`, `Sender`).
- `ColumnsRecords.ParallelWriter`: send records from any Burst job.
- With tl.unity: `Fixed Step` on the world authoring, the generated cells from `sv pack <pack.json> --glue tl --host unity` (`ColumnsCardSystem<T>`, `ColumnsOutboxSystem<T>`) and `ColumnsTimelineRequestSystem` (assembly `Sv.Columns.Entities.Tl`, compiled when `com.tl.unity` is present), which turns sv events into tl start and stop requests.
- `ColumnsBurstHost`: the same frame without ECS, with `Inputs`, `Rewind` and `Resimulate` when it keeps history (`History` on the world authoring).

See `docs/UNITY.md` in the sv repository. The Arena sample shows a Burst attack system and a Burst health readout.
