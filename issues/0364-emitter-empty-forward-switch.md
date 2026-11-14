# 0364 emitter writes an empty `switch (template) { }` in Forward when no template forwards: CS1522, build fails with warnings as errors

Status: closed (K1, commit 6982e60)

Found by BM1 while compiling the 10,000-rule pack (`v3-pack-10k-full` writes it to `%TEMP%\sv-bench-pack10k`; its one template
has no `forwards`). Building a kernels project over it with `TreatWarningsAsErrors` (as `tests/Sv.Kernels.Lego` does):

```
obj\SvPack\Kernels.g.cs(297551,13): error CS1522: Empty switch block
```

The generated `Forward(in WorldView world, int slot, int channel)` holds `switch (template) { }`. The lego and reference packs both
have a forwarding template, so no test sees it. Fix: emit no switch (or a `default: break;`) when no template forwards.
With `<NoWarn>CS1522</NoWarn>` the same project builds (receipt in docs/BENCH-REPORT.md: 9.0 s for 13.7 MB of C#).

Fix (K1): `Forward` ends its template switch with `default: break;`. Receipts: new diff test
`A_pack_without_forwarding_templates_compiles_without_warnings` (Roslyn C# 9, 0 warnings); the 10,000-rule pack (14.4 MB of C#)
builds with `TreatWarningsAsErrors` and LangVersion 9.0 without `NoWarn` in 12 s.
