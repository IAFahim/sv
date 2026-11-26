# 0526 v4: Burst frames with many records per entity are slower than the managed Vector path

Status: open (2026-11-26)

Found by: the `sv Unity columns` bench on the branch that adds `com.sv.columns.entities` (Unity 7000.0.0a21, Ryzen 5 8500G, 12 job threads).

## What we see
The Burst frame (`ColumnsBurstHost`) is as fast as or faster than the managed kernels when ops dominate, and slower when records dominate:

| Shape | Managed Vector | Burst rows |
|---|---|---|
| 20k entities x 500 ops | 0.6 to 2.0 ms | 0.5 to 1.2 ms |
| 1M entities x 500 ops | 25 to 33 ms | 18 to 24 ms |
| 1M x 500 ops + 10 records | 36 to 57 ms | 41 to 51 ms |
| 20k entities x 500 records | 9 to 16 ms | 17 ms |
| 1M entities x 10 records | 29 to 41 ms | 32 to 36 ms |

Ranges are two runs on the self-hosted runner, which is noisy.

## Why
The fused frame reads every record three times: a counting pass, a scatter into chunk order, and the fold. `ColumnWorld.Step` reads each record once into per-writer, per-chunk blocks that it allocates lazily; a Burst job cannot allocate them, and allocating them up front costs writers x chunks x classes x 512 bytes.

## Ideas
- Count per chunk while the record is sent (`ColumnsRecords.ParallelWriter` and `RecordWriter` both know the target), which leaves one scatter and one fold.
- Bucket by part instead of by chunk when records per chunk are many, and fold a part's records in one sweep.
- Preallocate per-writer blocks only for chunks that received records last frame.

Each must keep the digests equal to `Step()`; `FusedFrameTests` and the Unity lockstep test are the gate.
