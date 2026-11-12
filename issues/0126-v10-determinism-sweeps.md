# Issue 0126 - V10 (.NET): determinism sweeps

Agent D, branch `proto/d-frame`. Plan step V10, .NET half (chunk-order and host legs are agent C's Unity work).
Labels: `ai`, `area:test`. No digest difference anywhere.

| Sweep | How | Result |
|---|---|---|
| Workers | NorthStar2 at 1, 2, 4, 7, 11 workers (sv fold/send partitions + `TlWorkers`) | one digest per tick = baked pins |
| Shuffles | 100 seeds: journal order (inbox sends, local delta contributions, StatOps), fold partition order, send-pass row order | identical |
| Replay | 3 ticks live vs replay from the journal | identical |
| ISA legs | `sh eng/isa-legs.sh` (flags asserted first; A2ACAA69E55F79D8, 2B43BC1549AAE751) + NorthStar2, FoldLawTests, EventDeriveTests under `DOTNET_EnableAVX2=0` and `DOTNET_EnableHWIntrinsic=0` | all green, NorthStar2 pins equal on default / no-AVX2 / scalar |
| netstandard2.1 asset | `Sv.Portable.Tests` (pins) and `dotnet test tests/Sv.NorthStar2 -p:SvAsset=netstandard2.1` (asset `.NETStandard,Version=v2.1` asserted) | NorthStar2 pins equal |
| Events | chunk units in 12 random orders = word stream byte for byte (issue 0121) | identical |

Pins: NorthStar2 `939FBDEE2E7DEE50` / `E5CE5FA31824A22E` / `75035C43BBDC14AF`, fold scenario
`0D945D8FD131DA2A`, link table `A47434070C42E4AC`.

Test hygiene: the new allocation-heavy classes (`EventDeriveTests`, `SetChurnTests`) run in a
non-parallel xUnit collection (`HeavyAllocationCollection`); run in parallel they made the existing
per-thread 0 B tests flake (GC timing), 1 in ~4 runs. After: Sv.Tests 495/495 x 8 runs.

Final receipts (`dotnet test sv.slnx -c Release`, 2 runs): Sv.Tests 495, Sv.Portable.Tests 379,
Sv.NorthStar 27, Sv.NorthStar2 9, Sv.Unity.Tests 13, all green. FMA grep clean; no production comments.
